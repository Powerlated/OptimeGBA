using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using static OptimeGBA.Bits;
using static Util;
using static OptimeGBA.MemoryUtil;

namespace OptimeGBA
{
    // ARM DDI 0100I manual and GBATek used for implementation of this CPU

    public enum Arm7Mode
    {
        OldUSR = 0x00,
        OldFIQ = 0x01,
        OldIRQ = 0x02,
        OldSVC = 0x03,

        USR = 0x10, // User
        FIQ = 0x11, // Fast Interrupt Request
        IRQ = 0x12, // Interrupt Request
        SVC = 0x13, // Supervisor Call
        ABT = 0x17, // Abort
        UND = 0x1B, // Undefined Instruction
        SYS = 0x1F, // System
    }

    public unsafe sealed class Arm7
    {

        // 1024 functions, taking the top 10 bits of THUMB
        public ThumbExecutor[] ThumbDispatch;
        public ThumbExecutor[] GenerateThumbDispatch()
        {
            ThumbExecutor[] table = new ThumbExecutor[1024];

            for (ushort i = 0; i < 1024; i++)
            {
                ushort opcode = (ushort)(i << 6);
                table[i] = GetInstructionThumb(opcode);
            }

            return table;
        }

        public ArmExecutor[] ArmDispatch;
        public ArmExecutor[] GenerateArmDispatch()
        {
            ArmExecutor[] table = new ArmExecutor[4096];

            for (uint i = 0; i < 4096; i++)
            {
                uint opcode = ((i & 0xFF0) << 16) | ((i & 0xF) << 4);
                table[i] = GetInstructionArm(opcode);
            }

            return table;
        }

        public uint VectorReset;
        public uint VectorUndefined;
        public uint VectorSoftwareInterrupt;
        public uint VectorPrefetchAbort;
        public uint VectorDataAbort;
        public uint VectorAddrGreaterThan26Bit;
        public uint VectorIRQ;
        public uint VectorFIQ;

        public Memory Mem;

        public Action PreExecutionCallback;

        public bool Armv5;

        public uint* R = MemoryUtil.AllocateUnmanagedArray32(16);

        ~Arm7()
        {
            MemoryUtil.FreeUnmanagedArray(R);

            MemoryUtil.FreeUnmanagedArray(Timing8And16);
            MemoryUtil.FreeUnmanagedArray(Timing32);
            MemoryUtil.FreeUnmanagedArray(Timing8And16InstrFetch);
            MemoryUtil.FreeUnmanagedArray(Timing32InstrFetch);
        }

        public uint[] Rusr = new uint[7];
        public uint[] Rfiq = new uint[7];
        public uint[] Rsvc = new uint[2];
        public uint[] Rabt = new uint[2];
        public uint[] Rirq = new uint[2];
        public uint[] Rund = new uint[2];

        public uint SPSR_fiq;
        public uint SPSR_svc;
        public uint SPSR_abt;
        public uint SPSR_irq;
        public uint SPSR_und;

        public bool Negative = false;
        public bool Zero = false;
        public bool Carry = false;
        public bool Overflow = false;
        public bool Sticky = false;
        public bool IRQDisable = false;
        public bool FIQDisable = false;
        public bool ThumbState = false;
        public Arm7Mode Mode = Arm7Mode.SYS;

        public bool Halted;
        public bool PipelineDirty = false;

        // DEBUG INFO
        public long InstructionsRan = 0;
        public uint LastIns;
        public uint LastLastIns;
        public bool LastThumbState;
        public bool LastLastThumbState;
        public bool InterruptServiced;

        public bool Errored = false;

        public static uint[] ThumbExecutorProfile = new uint[1024];
        public static uint[] ArmExecutorProfile = new uint[4096];

        public bool FlagInterrupt;

        public Action StateChange;

        public Cp15 Cp15;
        public Arm7(Action stateChange, Memory mem, bool vectorMode, bool armv5, Cp15 cp15)
        {
            StateChange = stateChange;
            Mem = mem;
            Armv5 = armv5;
            Cp15 = cp15;

            ThumbDispatch = GenerateThumbDispatch();
            ArmDispatch = GenerateArmDispatch();

            // Default Mode
            Mode = Arm7Mode.SYS;

            SetVectorMode(vectorMode);
            R[15] = VectorReset;
        }

        public void SetTimingsTable(byte* table, params byte[] list)
        {
            for (uint i = 0; i < 16; i++)
            {
                table[i] = list[i];
            }
        }

        public void SetVectorMode(bool high)
        {
            if (high)
            {
                VectorReset = 0xFFFF0000;
                VectorUndefined = 0xFFFF0004;
                VectorSoftwareInterrupt = 0xFFFF0008;
                VectorPrefetchAbort = 0xFFFF000C;
                VectorDataAbort = 0xFFFF0010;
                VectorAddrGreaterThan26Bit = 0xFFFF0014;
                VectorIRQ = 0xFFFF0018;
                VectorFIQ = 0xFFFF001C;
            }
            else
            {
                VectorReset = 0x00;
                VectorUndefined = 0x04;
                VectorSoftwareInterrupt = 0x08;
                VectorPrefetchAbort = 0x0C;
                VectorDataAbort = 0x10;
                VectorAddrGreaterThan26Bit = 0x14;
                VectorIRQ = 0x18;
                VectorFIQ = 0x1C;
            }
        }

        public void BiosInit()
        {
            Zero = true;
            Carry = true;

            R[0] = 0x08000000;
            R[1] = 0x000000EA;
        }

        public void InitFlushPipeline()
        {
            if (ThumbState)
            {
                R[15] += 4;
                InstructionCycles += Timing8And16InstrFetch[(R[15] >> 24) & 0xF] * 2U;
            }
            else
            {
                R[15] += 8;
                InstructionCycles += Timing32InstrFetch[(R[15] >> 24) & 0xF] * 2U;
            }
        }

        public void FlushPipeline()
        {
            if (ThumbState)
            {
                R[15] &= ~1U;
                R[15] += 2;
                InstructionCycles += Timing8And16InstrFetch[(R[15] >> 24) & 0xF] * 2U;
            }
            else
            {
                R[15] &= ~3U;
                R[15] += 4;
                InstructionCycles += Timing32InstrFetch[(R[15] >> 24) & 0xF] * 2U;
            }
        }

        public uint InstructionCycles = 0;

        public uint Execute()
        {
            CheckInterrupts();
            if (!ThumbState) // ARM mode
            {
                ExecuteArm();
            }
            else // THUMB mode
            {
                ExecuteThumb();
            }


            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExecuteArm()
        {
            InstructionsRan++;
            InstructionCycles = 0;

            LineDebug($"R15: ${Util.HexN(R[15], 4)}");

            uint ins = Read32InstrFetch(R[15] - 8);

#if OPENTK_DEBUGGER
            LastLastIns = LastIns;
            LastIns = ins;
            LastLastThumbState = LastThumbState;
            LastThumbState = ThumbState;

            if (PreExecutionCallback != null)
                PreExecutionCallback();
#endif

            LineDebug($"Ins: ${Util.HexN(ins, 8)} InsBin:{Util.Binary(ins, 32)}");
            LineDebug($"Cond: ${ins >> 28:X}");

            uint condition = (ins >> 28) & 0xF;

            bool conditionMet = CheckCondition(condition);

            if (conditionMet)
            {
                uint decodeBits = ((ins >> 16) & 0xFF0) | ((ins >> 4) & 0xF);
#if OPENTK_DEBUGGER
                ArmExecutorProfile[decodeBits]++;
#endif
                ArmDispatch[decodeBits](this, ins);
            }

            if (!ThumbState)
            {
                R[15] += 4;
            }
            else
            {
                R[15] += 2;
            }

            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExecuteThumb()
        {
            InstructionsRan++;
            InstructionCycles = 0;

            LineDebug($"R15: ${Util.HexN(R[15], 4)}");

            ushort ins = (ushort)Read16InstrFetch(R[15] - 4);

            int decodeBits = ins >> 6;

#if OPENTK_DEBUGGER
            LastLastIns = LastIns;
            LastIns = ins;
            LastLastThumbState = LastThumbState;
            LastThumbState = ThumbState;
            ThumbExecutorProfile[decodeBits]++;

            if (PreExecutionCallback != null)
                PreExecutionCallback();
#endif
            LineDebug($"Ins: ${Util.HexN(ins, 4)} InsBin:{Util.Binary(ins, 16)}");

            ThumbDispatch[decodeBits](this, ins);

            if (ThumbState)
            {
                R[15] += 2;
            }
            else
            {
                R[15] += 4;
            }

            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckInterrupts()
        {
            if (FlagInterrupt && !IRQDisable)
            {
                DispatchInterrupt();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchInterrupt()
        {
            // Error("sdfkjadfdjsjklfads interupt lol");
#if OPENTK_DEBUGGER
            InterruptServiced = true;
#endif

            SPSR_irq = GetCPSR();

            SetMode(Arm7Mode.IRQ); // Go into SVC / Supervisor mode

            if (ThumbState)
            {
                R[14] = R[15] - 0;
            }
            else
            {
                R[14] = R[15] - 4;
            }

            ThumbState = false; // Back to ARM state
            IRQDisable = true;
            // FIQDisable = true;

            R[15] = VectorIRQ;
            InitFlushPipeline();

            // Error("IRQ, ENTERING IRQ MODE!");
        }

        public ArmExecutor GetInstructionArm(uint ins)
        {
            if ((ins & 0b1110000000000000000000000000) == 0b1010000000000000000000000000) // B
            {
                if (BitTest(ins, 24))
                {
                    return Arm.BL;
                }
                else
                {
                    return Arm.B;
                }
            } // id mask    0b1111111100000000000011010000     0b1111111100000000000011110000
            else if ((ins & 0b1111111100000000000011010000) == 0b0001001000000000000000010000) // BX
            {
                return Arm.BX;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111100100000000000011110000) == 0b0001000000000000000001010000) // QADD/QSUB/QDADD/QDSUB
            {
                if (Armv5)
                {
                    if (BitTest(ins, 21))
                    {
                        return Arm.QSUB;
                    }
                    else
                    {
                        return Arm.QADD;
                    }
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111100100000000000010010000) == 0b0001000000000000000010000000) // ARMv5 signed multiply
            {
                if (Armv5)
                {
                    uint id = ((ins >> 20) & 0b11111111);
                    if (id == 0b00010100)
                    {
                        return Arm.SMLALxy;
                    }
                    else if (id == 0b00010110)
                    {
                        return Arm.SMULxy;
                    }
                    else if (id == 0b00010000)
                    {
                        return Arm.SMLAxy;
                    }
                }

                return Arm.Invalid;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111111100000000000011110000) == 0b0001011000000000000000010000) // Count Leading Zeros
            {
                return Arm.CLZ;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111101100000000000011110000) == 0b0001000000000000000010010000) // SWP / SWPB
            {
                bool useByte = BitTest(ins, 22);
                if (useByte)
                {
                    return Arm.SWPB;
                }
                else
                {
                    return Arm.SWP;
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111101100000000000000000000) == 0b0011001000000000000000000000) // MSR - Immediate Operand
            {
                return Arm.MSR;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111101100000000000011110000) == 0b0001001000000000000000000000) // MSR - Register Operand
            {
                return Arm.MSR;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000            
            else if ((ins & 0b1111101100000000000011110000) == 0b0001000000000000000000000000) // MRS
            {
                return Arm.MRS;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111110000000000000011110000) == 0b0000000000000000000010010000) // Multiply Regular
            {
                return Arm.MUL;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111100000000000000011110000) == 0b0000100000000000000010010000) // Multiply Long
            {
                return Arm.MULL;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1110000000000000000010010000) == 0b0000000000000000000010010000) // Halfword, Signed Byte, Doubleword Loads and Stores
            {

                bool L = BitTest(ins, 20);
                bool S = BitTest(ins, 6);
                bool H = BitTest(ins, 5);
                if (!L && !S && H) return Arm.STRH;
                if (!L && S && !H && Armv5) return Arm.LDRD;
                if (!L && S && H && Armv5) return Arm.STRD;
                if (L && !S && H) return Arm.LDRH;
                if (L && S && !H) return Arm.LDRSB;
                if (L && S && H) return Arm.LDRSH;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1100000000000000000000000000) == 0b0000000000000000000000000000) // Data Processing // ALU
            {
                // Bits 27, 26 are 0, so data processing / ALU
                // LineDebug("Data Processing / FSR Transfer");
                // ALU Operations
                uint opcode = (ins >> 21) & 0xF;
                bool setFlags = (ins & BIT_20) != 0;
                bool useImmediate32 = (ins & BIT_25) != 0;

                // LineDebug($"Rn: R{rn}");
                // LineDebug($"Rd: R{rd}");

                if (setFlags)
                {
                    if (useImmediate32)
                    {
                        switch (opcode)
                        {
                            case 0x0: return Arm.DataANDS_Imm;
                            case 0x1: return Arm.DataEORS_Imm;
                            case 0x2: return Arm.DataSUBS_Imm;
                            case 0x3: return Arm.DataRSBS_Imm;
                            case 0x4: return Arm.DataADDS_Imm;
                            case 0x5: return Arm.DataADCS_Imm;
                            case 0x6: return Arm.DataSBCS_Imm;
                            case 0x7: return Arm.DataRSCS_Imm;
                            case 0x8: return Arm.DataTSTS_Imm;
                            case 0x9: return Arm.DataTEQS_Imm;
                            case 0xA: return Arm.DataCMPS_Imm;
                            case 0xB: return Arm.DataCMNS_Imm;
                            case 0xC: return Arm.DataORRS_Imm;
                            case 0xD: return Arm.DataMOVS_Imm;
                            case 0xE: return Arm.DataBICS_Imm;
                            case 0xF: return Arm.DataMVNS_Imm;
                        }
                    }
                    else
                    {
                        switch (opcode)
                        {
                            case 0x0: return Arm.DataANDS_Reg;
                            case 0x1: return Arm.DataEORS_Reg;
                            case 0x2: return Arm.DataSUBS_Reg;
                            case 0x3: return Arm.DataRSBS_Reg;
                            case 0x4: return Arm.DataADDS_Reg;
                            case 0x5: return Arm.DataADCS_Reg;
                            case 0x6: return Arm.DataSBCS_Reg;
                            case 0x7: return Arm.DataRSCS_Reg;
                            case 0x8: return Arm.DataTSTS_Reg;
                            case 0x9: return Arm.DataTEQS_Reg;
                            case 0xA: return Arm.DataCMPS_Reg;
                            case 0xB: return Arm.DataCMNS_Reg;
                            case 0xC: return Arm.DataORRS_Reg;
                            case 0xD: return Arm.DataMOVS_Reg;
                            case 0xE: return Arm.DataBICS_Reg;
                            case 0xF: return Arm.DataMVNS_Reg;
                        }
                    }
                }
                else
                {
                    if (useImmediate32)
                    {
                        switch (opcode)
                        {
                            case 0x0: return Arm.DataAND_Imm;
                            case 0x1: return Arm.DataEOR_Imm;
                            case 0x2: return Arm.DataSUB_Imm;
                            case 0x3: return Arm.DataRSB_Imm;
                            case 0x4: return Arm.DataADD_Imm;
                            case 0x5: return Arm.DataADC_Imm;
                            case 0x6: return Arm.DataSBC_Imm;
                            case 0x7: return Arm.DataRSC_Imm;
                            case 0x8: return Arm.DataTST_Imm;
                            case 0x9: return Arm.DataTEQ_Imm;
                            case 0xA: return Arm.DataCMP_Imm;
                            case 0xB: return Arm.DataCMN_Imm;
                            case 0xC: return Arm.DataORR_Imm;
                            case 0xD: return Arm.DataMOV_Imm;
                            case 0xE: return Arm.DataBIC_Imm;
                            case 0xF: return Arm.DataMVN_Imm;
                        }
                    }
                    else
                    {
                        switch (opcode)
                        {
                            case 0x0: return Arm.DataAND_Reg;
                            case 0x1: return Arm.DataEOR_Reg;
                            case 0x2: return Arm.DataSUB_Reg;
                            case 0x3: return Arm.DataRSB_Reg;
                            case 0x4: return Arm.DataADD_Reg;
                            case 0x5: return Arm.DataADC_Reg;
                            case 0x6: return Arm.DataSBC_Reg;
                            case 0x7: return Arm.DataRSC_Reg;
                            case 0x8: return Arm.DataTST_Reg;
                            case 0x9: return Arm.DataTEQ_Reg;
                            case 0xA: return Arm.DataCMP_Reg;
                            case 0xB: return Arm.DataCMN_Reg;
                            case 0xC: return Arm.DataORR_Reg;
                            case 0xD: return Arm.DataMOV_Reg;
                            case 0xE: return Arm.DataBIC_Reg;
                            case 0xF: return Arm.DataMVN_Reg;
                        }
                    }
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1100000000000000000000000000) == 0b0100000000000000000000000000) // LDR / STR
            {
                bool L = BitTest(ins, 20);
                bool useRegister = BitTest(ins, 25);
                if (useRegister)
                {
                    if (L)
                    {
                        return Arm.RegularLDR_Reg;
                    }
                    else
                    {
                        return Arm.RegularSTR_Reg;
                    }
                }
                else
                {
                    if (L)
                    {
                        return Arm.RegularLDR_Imm;
                    }
                    else
                    {
                        return Arm.RegularSTR_Imm;
                    }
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1110000000000000000000000000) == 0b1000000000000000000000000000) // LDM / STM
            {
                bool L = BitTest(ins, 20); // Load vs Store

                if (Armv5)
                {
                    if (L)
                    {
                        return Arm.LDM_V5;
                    }
                    else
                    {
                        return Arm.STM_V5;
                    }
                }
                else
                {
                    if (L)
                    {
                        return Arm.LDM;
                    }
                    else
                    {
                        return Arm.STM;
                    }
                }
            }
            else if ((ins & 0b1111000000000000000000010000) == 0b1110000000000000000000010000) // Coprocessor register transfers
            {
                if (Armv5)
                {
                    if (BitTest(ins, 20))
                    {
                        return Arm.MRC;
                    }
                    else
                    {
                        return Arm.MCR;
                    }
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111000000000000000000000000) == 0b1111000000000000000000000000) // SWI - Software Interrupt
            {
                return Arm.SWI;
            }
            return Arm.Invalid;
        }

        public ThumbExecutor GetInstructionThumb(ushort ins)
        {
            switch ((ins >> 13) & 0b111)
            {
                case 0b000: // Shift by immediate, Add/subtract register, Add/subtract immediate
                    {
                        switch ((ins >> 11) & 0b11)
                        {
                            case 0b00: // LSL (1)
                                return Thumb.ImmShiftLSL;
                            case 0b01: // LSR (1)
                                return Thumb.ImmShiftLSR;
                            case 0b10: // ASR (1)
                                return Thumb.ImmShiftASR;
                            case 0b11: // Add/subtract/compare/move immediate
                                {
                                    switch ((ins >> 9) & 0b11)
                                    {
                                        case 0b00: // ADD (3)
                                            return Thumb.ImmAluADD1;
                                        case 0b01: // SUB (3)
                                            return Thumb.ImmAluSUB1;
                                        case 0b10: // ADD (1) // MOV (2)
                                            return Thumb.ImmAluADD2;
                                        case 0b11: // SUB (1)
                                            return Thumb.ImmAluSUB2;
                                    }
                                }
                                break;
                        }
                    }
                    break;
                case 0b001: // Add/subtract/compare/move immediate
                    {

                        switch ((ins >> 11) & 0b11)
                        {
                            case 0b00: // MOV (1)
                                return Thumb.MovImmediate;
                            case 0b01: // CMP (1)
                                return Thumb.CmpImmediate;
                            case 0b10: // ADD (2)
                                return Thumb.AddImmediate;
                            case 0b11: // SUB (2)
                                return Thumb.SubImmediate;
                        }
                    }
                    break;
                case 0b010:
                    {
                        if ((ins & 0b1111110000000000) == 0b0100000000000000) // Data Processing
                        {

                            uint opcode = (uint)((ins >> 6) & 0xFU);
                            switch (opcode)
                            {
                                case 0x0: // AND
                                    return Thumb.DataAND;
                                case 0x1: // EOR
                                    return Thumb.DataEOR;
                                case 0x2: // LSL (2)
                                    return Thumb.DataLSL;
                                case 0x3: // LSR (2)
                                    return Thumb.DataLSR;
                                case 0x4: // ASR (2)
                                    return Thumb.DataASR;
                                case 0x5: // ADC
                                    return Thumb.DataADC;
                                case 0x6: // SBC
                                    return Thumb.DataSBC;
                                case 0x7: // ROR
                                    return Thumb.DataROR;
                                case 0x8: // TST
                                    return Thumb.DataTST;
                                case 0x9: // NEG / RSB
                                    return Thumb.DataNEG;
                                case 0xA: // CMP (2)
                                    return Thumb.DataCMP;
                                case 0xB:  // CMN
                                    return Thumb.DataCMN;
                                case 0xC: // ORR
                                    return Thumb.DataORR;
                                case 0xD: // MUL
                                    return Thumb.DataMUL;
                                case 0xE: // BIC
                                    return Thumb.DataBIC;
                                case 0xF: // MVN
                                    return Thumb.DataMVN;
                            }
                        }
                        else if ((ins & 0b1111110000000000) == 0b0100010000000000) // Special Data Processing / Branch-exchange instruction set
                        {
                            switch ((ins >> 8) & 0b11)
                            {
                                case 0b00: // ADD (4)
                                    return Thumb.SpecialDataADD;
                                case 0b01: // CMP (3)
                                    return Thumb.SpecialDataCMP;
                                case 0b10:// MOV (3)
                                    return Thumb.SpecialDataMOV;
                                case 0b11: // BX
                                    return Thumb.SpecialDataBX;
                            }
                        }
                        else if ((ins & 0b1111100000000000) == 0b0100100000000000) // LDR (3) - Load from literal pool
                        {
                            return Thumb.LDRLiteralPool;
                        }
                        else if ((ins & 0b1111000000000000) == 0b0101000000000000) // Load/store register offset
                        {
                            uint rd = (uint)((ins >> 0) & 0b111);
                            uint rn = (uint)((ins >> 3) & 0b111);
                            uint rm = (uint)((ins >> 6) & 0b111);

                            switch ((ins >> 9) & 0b111)
                            {
                                case 0b000: // STR (2)
                                    return Thumb.RegOffsSTR;
                                case 0b001: // STRH (2)
                                    return Thumb.RegOffsSTRH;
                                case 0b010: // STRB (2)
                                    return Thumb.RegOffsSTRB;
                                case 0b011: // LDRSB
                                    return Thumb.RegOffsLDRSB;
                                case 0b100: // LDR (2)
                                    return Thumb.RegOffsLDR;
                                case 0b101: // LDRH (2)
                                    return Thumb.RegOffsLDRH;
                                case 0b110: // LDRB (2)
                                    return Thumb.RegOffsLDRB;
                                case 0b111: // LDRSH
                                    return Thumb.RegOffsLDRSH;
                                    // default:
                                    //     Error("Load/store register offset invalid opcode");
                            }
                        }
                    }
                    break;
                case 0b011: // Load/store word/byte immediate offset
                    {

                        switch ((ins >> 11) & 0b11)
                        {
                            case 0b01: // LDR (1)
                                return Thumb.ImmOffsLDR;
                            case 0b00: // STR (1)
                                return Thumb.ImmOffsSTR;
                            case 0b10: // STRB (1)
                                return Thumb.ImmOffsSTRB;
                            case 0b11: // LDRB (1)
                                return Thumb.ImmOffsLDRB;
                        }
                    }
                    break;
                case 0b100:
                    {
                        if ((ins & 0b1111000000000000) == 0b1000000000000000) // STRH (1) / LDRH (1) - Load/Store Halfword Immediate Offset
                        {
                            bool load = BitTest(ins, 11);
                            if (load)
                            {
                                return Thumb.ImmLDRH;
                            }
                            else
                            {
                                return Thumb.ImmSTRH;
                            }
                        }
                        else if ((ins & 0b1111100000000000) == 0b1001100000000000) // LDR (4) - Load from stack
                        {
                            return Thumb.StackLDR;
                        }
                        else if ((ins & 0b1111100000000000) == 0b1001000000000000) // STR (3) - Store to stack
                        {
                            return Thumb.StackSTR;
                        }
                    }
                    break;
                case 0b101:
                    {
                        if ((ins & 0b1111000000000000) == 0b1011000000000000) // Miscellaneous (categorized like in the ARM reference manual)
                        {
                            if ((ins & 0b1111011000000000) == 0b1011010000000000) // POP & PUSH
                            {
                                if (BitTest(ins, 11))
                                {
                                    return Thumb.POP;
                                }
                                else
                                {
                                    return Thumb.PUSH;
                                }
                            }
                            else if ((ins & 0b1111111110000000) == 0b1011000000000000) // ADD (7)
                            {
                                return Thumb.MiscImmADD;
                            }
                            else if ((ins & 0b1111111110000000) == 0b1011000010000000) // SUB (4)
                            {
                                return Thumb.MiscImmSUB;
                            }
                            else if ((ins & 0b1111111111000000) == 0b1011101011000000) // REVSH
                            {
                                return Thumb.MiscREVSH;
                            }
                        }
                        else if ((ins & 0b1111100000000000) == 0b1010000000000000) // ADD (5) - Add to PC 
                        {
                            return Thumb.MiscPcADD;
                        }
                        else if ((ins & 0b1111100000000000) == 0b1010100000000000) // ADD (6) - Add to SP
                        {
                            return Thumb.MiscSpADD;
                        }
                    }
                    break;
                case 0b110:
                    {
                        if ((ins & 0b1111000000000000) == 0b1100000000000000) // LDMIA, STMIA - Load/Store Multiple
                        {
                            if (BitTest(ins, 11))
                            {
                                return Thumb.LDMIA;
                            }
                            else
                            {
                                return Thumb.STMIA;
                            }
                        }
                        else if ((ins & 0b1111111100000000) == 0b1101111100000000) // SWI - Software Interrupt
                        {
                            return Thumb.SWI;
                        }
                        else if ((ins & 0b1111000000000000) == 0b1101000000000000) // B (1) - Conditional
                        {
                            return Thumb.ConditionalB;
                        }
                    }
                    break;
                case 0b111:
                    {
                        if ((ins & 0b1111100000000000) == 0b1110000000000000) // B (2) - Unconditional
                        {
                            return Thumb.UnconditionalB;
                        }
                        else if ((ins & 0b1110000000000000) == 0b1110000000000000) // BL, BLX - Branch With Link (Optional Exchange)
                        {
                            uint H = (uint)((ins >> 11) & 0b11);
                            switch (H)
                            {
                                case 0b10: return Thumb.BLUpperFill;
                                case 0b11: return Thumb.BLToThumb;
                                case 0b01: return Thumb.BLToArm;
                            }
                        }
                    }
                    break;
                    // default:
                    //     Error("Unknown THUMB instruction");
            }

            return Thumb.Invalid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool CheckCondition(uint code)
        {
            // Unconditional execution is most common, do a quick check 
            // instead of going through a slow switch
            if (code == 0xE)
            {
                return true;
            }

            switch (code)
            {
                case 0x0: // Zero, Equal, Z=1
                    return Zero;
                case 0x1: // Nonzero, Not Equal, Z=0
                    return !Zero;
                case 0x2: // Unsigned higher or same, C=1
                    return Carry;
                case 0x3: // Unsigned lower, C=0
                    return !Carry;
                case 0x4: // Signed Negative, Minus, N=1
                    return Negative;
                case 0x5: // Signed Positive or Zero, Plus, N=0
                    return !Negative;
                case 0x6: // Signed Overflow, V=1
                    return Overflow;
                case 0x7: // Signed No Overflow, V=0
                    return !Overflow;
                case 0x8: // Unsigned Higher, C=1 && Z=0
                    return Carry && !Zero;
                case 0x9: // Unsigned Lower or Same
                    return !Carry || Zero;
                case 0xA: // Signed Greater or Equal
                    return Negative == Overflow;
                case 0xB: // Signed Less Than
                    return Negative != Overflow;
                case 0xC: // Signed Greater Than
                    return !Zero && Negative == Overflow;
                case 0xD: // Signed less or Equal, Z=1 or N!=V
                    return Zero || (Negative != Overflow);
                case 0xE: // Always
                    return true;
                case 0xF: // some ARMv5 instructions have 0xF as condition code in encoding
                    return true;
            }

            return false;
        }

        public uint GetCPSR()
        {
            uint val = 0;

            if (Negative) val = BitSet(val, 31);
            if (Zero) val = BitSet(val, 30);
            if (Carry) val = BitSet(val, 29);
            if (Overflow) val = BitSet(val, 28);
            if (Sticky) val = BitSet(val, 27);

            if (IRQDisable) val = BitSet(val, 7);
            if (FIQDisable) val = BitSet(val, 6);
            if (ThumbState) val = BitSet(val, 5);

            val |= GetMode();
            return val;
        }

        public void SetCPSR(uint val)
        {
            Negative = BitTest(val, 31);
            Zero = BitTest(val, 30);
            Carry = BitTest(val, 29);
            Overflow = BitTest(val, 28);
            Sticky = BitTest(val, 27);

            IRQDisable = BitTest(val, 7);
            FIQDisable = BitTest(val, 6);
            bool newThumbState = BitTest(val, 5);
            if (newThumbState != ThumbState)
            {
                StateChange();
            }
            ThumbState = newThumbState;

            SetMode((Arm7Mode)(val & 0b01111));
        }

        public uint GetSPSR()
        {
            switch (Mode)
            {
                case Arm7Mode.FIQ:
                case Arm7Mode.OldFIQ:
                    return SPSR_fiq;
                case Arm7Mode.SVC:
                case Arm7Mode.OldSVC:
                    return SPSR_svc;
                case Arm7Mode.ABT:
                    return SPSR_abt;
                case Arm7Mode.IRQ:
                case Arm7Mode.OldIRQ:
                    return SPSR_irq;
                case Arm7Mode.UND:
                    return SPSR_und;

            }

            // Error("No SPSR in this mode!");
            return GetCPSR();

        }
        public void SetSPSR(uint set)
        {
            switch (Mode)
            {
                case Arm7Mode.FIQ:
                case Arm7Mode.OldFIQ:
                    SPSR_fiq = set;
                    return;
                case Arm7Mode.SVC:
                case Arm7Mode.OldSVC:
                    SPSR_svc = set;
                    return;
                case Arm7Mode.ABT:
                    SPSR_abt = set;
                    return;
                case Arm7Mode.IRQ:
                case Arm7Mode.OldIRQ:
                    SPSR_irq = set;
                    return;
                case Arm7Mode.UND:
                    SPSR_und = set;
                    return;

            }

            SetCPSR(set);

            // Error("No SPSR in this mode!");
        }

        public uint GetModeReg(uint reg, Arm7Mode mode)
        {
            if (mode == Mode)
            {
                return R[reg];
            }

            switch (mode)
            {
                case Arm7Mode.USR:
                case Arm7Mode.SYS: return Rusr[reg - 8];
                case Arm7Mode.FIQ: return Rfiq[reg - 8];
                case Arm7Mode.IRQ: return Rirq[reg - 13];
                case Arm7Mode.SVC: return Rsvc[reg - 13];
                case Arm7Mode.ABT: return Rabt[reg - 13];
                case Arm7Mode.UND: return Rund[reg - 13];
            }

            return 0;
        }

        public void SetModeReg(uint reg, Arm7Mode mode, uint val)
        {
            if (mode == Mode)
            {
                R[reg] = val;
            }

            switch (mode)
            {
                case Arm7Mode.USR:
                case Arm7Mode.SYS: Rusr[reg - 8] = val; break;
                case Arm7Mode.FIQ: Rfiq[reg - 8] = val; break;
                case Arm7Mode.IRQ: Rirq[reg - 13] = val; break;
                case Arm7Mode.SVC: Rsvc[reg - 13] = val; break;
                case Arm7Mode.ABT: Rabt[reg - 13] = val; break;
                case Arm7Mode.UND: Rund[reg - 13] = val; break;
            }
        }

        public void SetMode(Arm7Mode mode)
        {
            // Bit 4 of mode is always set 
            mode |= (Arm7Mode)0b10000;

            // Store registers based on current mode
            switch (Mode)
            {
                case Arm7Mode.USR:
                case Arm7Mode.SYS: for (uint i = 0; i < 7; i++) Rusr[i] = R[8 + i]; break;
                case Arm7Mode.FIQ: for (uint i = 0; i < 7; i++) Rfiq[i] = R[8 + i]; break;
                case Arm7Mode.SVC: for (uint i = 0; i < 2; i++) Rsvc[i] = R[13 + i]; break;
                case Arm7Mode.ABT: for (uint i = 0; i < 2; i++) Rabt[i] = R[13 + i]; break;
                case Arm7Mode.IRQ: for (uint i = 0; i < 2; i++) Rirq[i] = R[13 + i]; break;
                case Arm7Mode.UND: for (uint i = 0; i < 2; i++) Rund[i] = R[13 + i]; break;
            }

            switch (mode)
            {
                case Arm7Mode.USR:
                case Arm7Mode.SYS: for (uint i = 5; i < 7; i++) R[8 + i] = Rusr[i]; break;
                case Arm7Mode.FIQ: for (uint i = 0; i < 7; i++) R[8 + i] = Rfiq[i]; break;
                case Arm7Mode.SVC: for (uint i = 0; i < 2; i++) R[13 + i] = Rsvc[i]; break;
                case Arm7Mode.ABT: for (uint i = 0; i < 2; i++) R[13 + i] = Rabt[i]; break;
                case Arm7Mode.IRQ: for (uint i = 0; i < 2; i++) R[13 + i] = Rirq[i]; break;
                case Arm7Mode.UND: for (uint i = 0; i < 2; i++) R[13 + i] = Rund[i]; break;
            }

            if (Mode == Arm7Mode.FIQ)
                for (uint i = 0; i < 5; i++) R[8 + i] = Rusr[i];

            Mode = mode;
        }

        public uint GetMode()
        {
            return (uint)Mode;
        }

        public String Debug = "";

        [Conditional("DONT")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ResetDebug()
        {
            Debug = "";
        }

        [Conditional("DONT")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LineDebug(String s)
        {
            Debug += $"{s}\n";
        }

        // [Conditional("DEBUG")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Error(String s)
        {
            Debug += $"ERROR:\n";
            Debug += $"{s}\n";

            Errored = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckOverflowSub(uint val1, uint val2, uint result)
        {
            return ((val1 ^ val2) & ((val1 ^ result)) & 0x80000000) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckOverflowAdd(uint val1, uint val2, uint result)
        {
            return (~(val1 ^ val2) & ((val1 ^ result)) & 0x80000000) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            return Mem.Read8(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16(uint addr)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            return Mem.Read16(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32(uint addr)
        {
            InstructionCycles += Timing32[(addr >> 24) & 0xF];
            return Mem.Read32(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16InstrFetch(uint addr)
        {
            InstructionCycles += Timing8And16InstrFetch[(addr >> 24) & 0xF];
            return Mem.Read16(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32InstrFetch(uint addr)
        {
            InstructionCycles += Timing32InstrFetch[(addr >> 24) & 0xF];
            return Mem.Read32(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write8(uint addr, byte val)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            Mem.Write8(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            Mem.Write16(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write32(uint addr, uint val)
        {
            InstructionCycles += Timing32[(addr >> 24) & 0xF];
            Mem.Write32(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ICycle()
        {
            InstructionCycles += 1;
        }

        public byte* Timing8And16 = AllocateUnmanagedArray(16);
        public byte* Timing32 = AllocateUnmanagedArray(16);
        public byte* Timing8And16InstrFetch = AllocateUnmanagedArray(16);
        public byte* Timing32InstrFetch = AllocateUnmanagedArray(16);

        public uint GetCurrentInstrAddr()
        {
            return (uint)(R[15] - (ThumbState ? 4 : 8));
        }
    }
}
