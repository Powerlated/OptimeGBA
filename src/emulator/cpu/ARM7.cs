using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public unsafe sealed class ARM7
    {
        public enum ARM7Mode
        {
            OldUser = 0x00,
            OldFIQ = 0x01,
            OldIRQ = 0x02,
            OldSupervisor = 0x03,

            User = 0x10,
            FIQ = 0x11,
            IRQ = 0x12,
            Supervisor = 0x13,
            Abort = 0x17,
            Undefined = 0x1B,
            System = 0x1F,
        }

        // 1024 functions, taking the top 10 bits of THUMB
        public static ThumbExecutor[] ThumbDispatch = GenerateThumbDispatch();
        public static ThumbExecutor[] GenerateThumbDispatch()
        {
            ThumbExecutor[] table = new ThumbExecutor[1024];

            for (ushort i = 0; i < 1024; i++)
            {
                ushort opcode = (ushort)(i << 6);
                table[i] = GetInstructionThumb(opcode);
            }

            return table;
        }

        public static ArmExecutor[] ArmDispatch = GenerateArmDispatch();
        public static ArmExecutor[] GenerateArmDispatch()
        {
            ArmExecutor[] table = new ArmExecutor[4096];

            for (uint i = 0; i < 4096; i++)
            {
                uint opcode = ((i & 0xFF0) << 16) | ((i & 0xF) << 4);
                table[i] = GetInstructionArm(opcode);
            }

            return table;
        }

        public bool Errored = false;

        public uint[] ThumbExecutorProfile = new uint[1024];
        public uint[] ArmExecutorProfile = new uint[4096];

        public const uint VectorReset = 0x00;
        public const uint VectorUndefined = 0x04;
        public const uint VectorSoftwareInterrupt = 0x08;
        public const uint VectorPrefetchAbort = 0x0C;
        public const uint VectorDataAbort = 0x10;
        public const uint VectorAddrGreaterThan26Bit = 0x14;
        public const uint VectorIRQ = 0x18;
        public const uint VectorFIQ = 0x1C;

        public GBA Gba;

#if UNSAFE
        public uint* R = Memory.AllocateUnmanagedArray32(16);
#else
        public uint[] R = new uint[16];
#endif

        public uint R8usr;
        public uint R9usr;
        public uint R10usr;
        public uint R11usr;
        public uint R12usr;
        public uint R13usr;
        public uint R14usr;

        public uint R8fiq;
        public uint R9fiq;
        public uint R10fiq;
        public uint R11fiq;
        public uint R12fiq;
        public uint R13fiq;
        public uint R14fiq;

        public uint R13svc;
        public uint R14svc;

        public uint R13abt;
        public uint R14abt;

        public uint R13irq;
        public uint R14irq;

        public uint R13und;
        public uint R14und;

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
        public ARM7Mode Mode = ARM7Mode.System;

        public uint ARMFetch;
        public uint ARMDecode;
        public ushort THUMBFetch;
        public ushort THUMBDecode;
        public uint Pipeline; // 0 for empty, 1 for Fetch filled, 2 for Decode filled, 3 for Execute filled (full)

        public bool PipelineDirty = false;

        public long InstructionsRan = 0;
        public long InstructionsRanInterrupt = 0;

        // DEBUG INFO
        public uint LastIns;
        public uint LastLastIns;

        public ARM7(GBA gba)
        {
            Gba = gba;

            if (Gba.Provider.BootBios)
            {
                // Boot BIOS
                R[15] = 0x00000000;
            }
            else
            {
                // Boot game
                R[15] = 0x08000000;
            }

            // Default Mode
            Mode = ARM7Mode.System;

            R13svc = 0x03007FE0;
            R13irq = 0x03007FA0;
            R13usr = 0x03007F00;

            // Default Stack Pointer
            R[13] = R13usr;

            if (!Gba.Provider.BootBios)
            {
                BiosInit();
            }

            FillPipelineArm();
        }

        public void BiosInit()
        {
            Zero = true;
            Carry = true;

            R[0] = 0x08000000;
            R[1] = 0x000000EA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FillPipelineArm()
        {
            while (Pipeline < 2)
            {
                FetchPipelineArm();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FetchPipelineArm()
        {
            ARMDecode = ARMFetch;
            ARMFetch = Read32InstrFetch(R[15]);
            R[15] += 4;

            Pipeline++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FetchPipelineArmIfNotFull()
        {
            if (Pipeline < 2)
            {
                FetchPipelineArm();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FillPipelineThumb()
        {
            while (Pipeline < 2)
            {
                FetchPipelineThumb();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FetchPipelineThumb()
        {

            THUMBDecode = THUMBFetch;
            THUMBFetch = Read16InstrFetch(R[15]);
            R[15] += 2;

            Pipeline++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FetchPipelineThumbIfNotFull()
        {
            if (Pipeline < 2)
            {
                FetchPipelineThumb();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void FlushPipeline()
        {
            Pipeline = 0;
            if (ThumbState)
            {
                R[15] &= 0xFFFFFFFE;
                FillPipelineThumb();
            }
            else
            {
                R[15] &= 0xFFFFFFFC;
                FillPipelineArm();
            }

            PipelineDirty = false;
        }

        public uint InstructionCycles = 0;

        public uint Execute()
        {
#if OPENTK_DEBUGGER
            InstructionsRan++;
#endif
            InstructionCycles = 0;

            // if (PipelineDirty)
            // {
            //     Error("Pipeline is dirty, NOT executing next instruction!");
            //     return 1;
            // }
            ResetDebug();

            // if (R[15] == 0x03006498)
            // {
            //     Error("Breakpoint");
            // }

            // if (R[2] == 0xFF03FF03)
            // {
            //     Error("R2 Breakpoint");
            // }

            // if (R[14] == 0x3FC0FFC0)
            // {
            //     Error("R14 Breakpoint");
            // }

            if (!ThumbState) // ARM mode
            {
                // Current Instruction Fetch

                LineDebug($"R15: ${Util.HexN(R[15], 4)}");

                uint ins = ARMDecode;
                Pipeline--;
#if OPENTK_DEBUGGER
                LastLastIns = LastIns;
                LastIns = ins;
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

                // Fill the pipeline if it's not full
                FetchPipelineArmIfNotFull();
            }
            else // THUMB mode
            {
                LineDebug($"R15: ${Util.HexN(R[15], 4)}");

                ushort ins = THUMBDecode;
                Pipeline--;
#if OPENTK_DEBUGGER
                LastLastIns = LastIns;
                LastIns = ins;
#endif

                LineDebug($"Ins: ${Util.HexN(ins, 4)} InsBin:{Util.Binary(ins, 16)}");

                int decodeBits = ins >> 6;
#if OPENTK_DEBUGGER
                ThumbExecutorProfile[decodeBits]++;
#endif
                ThumbDispatch[decodeBits](this, ins);

                // Fill the pipeline if it's not full
                FetchPipelineThumbIfNotFull();
            }

            if (Gba.HwControl.AvailableAndEnabled && !IRQDisable)
            {
                // Error("sdfkjadfdjsjklfads interupt lol");
                InstructionsRanInterrupt = InstructionsRan;

                SPSR_irq = GetCPSR();
                if (ThumbState)
                {
                    FillPipelineThumb();
                    R14irq = R[15] - 0;
                }
                else
                {
                    FillPipelineArm();
                    R14irq = R[15] - 4;
                }
                SetMode((uint)ARM7Mode.IRQ); // Go into SVC / Supervisor mode
                ThumbState = false; // Back to ARM state
                IRQDisable = true;
                // FIQDisable = true;

                R[15] = VectorIRQ;
                FlushPipeline();

                // Error("IRQ, ENTERING IRQ MODE!");
            }

            return InstructionCycles;
        }


        public static ArmExecutor GetInstructionArm(uint ins)
        {
            if ((ins & 0b1110000000000000000000000000) == 0b1010000000000000000000000000) // B
            {
                return Arm.B;
            } // id mask    0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111111100000000000011110000) == 0b0001001000000000000000010000) // BX
            {
                return Arm.BX;
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
                return Arm.SpecialLDRSTR;
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1100000000000000000000000000) == 0b0000000000000000000000000000) // Data Processing // ALU
            {
                // Bits 27, 26 are 0, so data processing / ALU
                // LineDebug("Data Processing / FSR Transfer");
                // ALU Operations
                uint opcode = (ins >> 21) & 0xF;
                (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);

                // LineDebug($"Rn: R{rn}");
                // LineDebug($"Rd: R{rd}");

                switch (opcode)
                {
                    case 0x0: // AND
                        return Arm.DataAND;
                    case 0x1: // EOR
                        return Arm.DataEOR;
                    case 0x2: // SUB
                        return Arm.DataSUB;
                    case 0x3: // RSB
                        return Arm.DataRSB;
                    case 0x4: // ADD
                        return Arm.DataADD;
                    case 0x5: // ADC
                        return Arm.DataADC;
                    case 0x6: // SBC
                        return Arm.DataSBC;
                    case 0x7: // RSC
                        return Arm.DataRSC;
                    case 0x8: // TST
                        return Arm.DataTST;
                    case 0x9: // TEQ
                        return Arm.DataTEQ;
                    case 0xA: // CMP
                        return Arm.DataCMP;
                    case 0xB: // CMN
                        return Arm.DataCMN;
                    case 0xC: // ORR
                        return Arm.DataORR;
                    case 0xD: // MOV
                        return Arm.DataMOV;
                    case 0xE: // BIC
                        return Arm.DataBIC;
                    case 0xF: // MVN
                        return Arm.DataMVN;
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1100000000000000000000000000) == 0b0100000000000000000000000000) // LDR / STR
            {
                bool L = BitTest(ins, 20);
                if (L)
                {
                    return Arm.RegularLDR;
                }
                else
                {
                    return Arm.RegularSTR;
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1110000000000000000000000000) == 0b1000000000000000000000000000) // LDM / STM
            {
                bool L = BitTest(ins, 20); // Load vs Store

                if (L)
                {
                    return Arm.LDM;
                }
                else
                {
                    return Arm.STM;
                }
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1111000000000000000000000000) == 0b1111000000000000000000000000) // SWI - Software Interrupt
            {
                return Arm.SWI;
            }
            return Arm.Invalid;
        }

        public static ThumbExecutor GetInstructionThumb(ushort ins)
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
                default:
                    Error($"Invalid condition? {Util.Hex(code, 1)}");
                    return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetUserReg(uint reg)
        {
            if (Mode == ARM7Mode.User && Mode == ARM7Mode.OldUser && Mode == ARM7Mode.System)
            {
                throw new Exception("GetUserReg() called in User or System mode");
            }

            switch (reg)
            {
                case 0x0: return R[0];
                case 0x1: return R[1];
                case 0x2: return R[2];
                case 0x3: return R[3];
                case 0x4: return R[4];
                case 0x5: return R[5];
                case 0x6: return R[6];
                case 0x7: return R[7];
                case 0x8: return R8usr;
                case 0x9: return R9usr;
                case 0xA: return R10usr;
                case 0xB: return R11usr;
                case 0xC: return R12usr;
                case 0xD: return R13usr;
                case 0xE: return R14usr;
                case 0xF: return R[15];

                default:
                    Error($"Invalid R: {reg}");
                    return 0;
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetUserReg(uint reg, uint val)
        {
            if (Mode == ARM7Mode.User && Mode == ARM7Mode.OldUser && Mode == ARM7Mode.System)
            {
                throw new Exception("SetUserReg() called in User or System mode");
            }

            switch (reg)
            {
                case 0x0: R[0] = val; break;
                case 0x1: R[1] = val; break;
                case 0x2: R[2] = val; break;
                case 0x3: R[3] = val; break;
                case 0x4: R[4] = val; break;
                case 0x5: R[5] = val; break;
                case 0x6: R[6] = val; break;
                case 0x7: R[7] = val; break;
                case 0x8: R8usr = val; break;
                case 0x9: R9usr = val; break;
                case 0xA: R10usr = val; break;
                case 0xB: R11usr = val; break;
                case 0xC: R12usr = val; break;
                case 0xD: R13usr = val; break;
                case 0xE: R14usr = val; break;
                case 0xF: R[15] = val; PipelineDirty = true; break;

                default:
                    Error($"Invalid R: {reg}");
                    return;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LogicalShiftLeft32(uint n, byte bits)
        {
            return n << bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LogicalShiftRight32(uint n, byte bits)
        {
            return n >> bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ArithmeticShiftRight32(uint n, byte bits)
        {
            return (uint)((int)n >> bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight32(uint n, byte bits)
        {
            return (n >> bits) | (n << (32 - bits));
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
            ThumbState = BitTest(val, 5);

            SetMode(val & 0b01111);
        }

        public uint GetSPSR()
        {
            switch (Mode)
            {
                case ARM7Mode.FIQ:
                case ARM7Mode.OldFIQ:
                    return SPSR_fiq;
                case ARM7Mode.Supervisor:
                case ARM7Mode.OldSupervisor:
                    return SPSR_svc;
                case ARM7Mode.Abort:
                    return SPSR_abt;
                case ARM7Mode.IRQ:
                case ARM7Mode.OldIRQ:
                    return SPSR_irq;
                case ARM7Mode.Undefined:
                    return SPSR_und;

            }

            Error("No SPSR in this mode!");
            return GetCPSR();
        }

        public void SetSPSR(uint set)
        {
            switch (Mode)
            {
                case ARM7Mode.FIQ:
                case ARM7Mode.OldFIQ:
                    SPSR_fiq = set;
                    return;
                case ARM7Mode.Supervisor:
                case ARM7Mode.OldSupervisor:
                    SPSR_svc = set;
                    return;
                case ARM7Mode.Abort:
                    SPSR_abt = set;
                    return;
                case ARM7Mode.IRQ:
                case ARM7Mode.OldIRQ:
                    SPSR_irq = set;
                    return;
                case ARM7Mode.Undefined:
                    SPSR_und = set;
                    return;

            }

            SetCPSR(set);

            // Error("No SPSR in this mode!");
        }

        public void SetMode(uint mode)
        {
            // Bit 4 of mode is always set 
            mode |= 0b10000;
            // Store registers based on current mode
            switch (Mode)
            {
                case ARM7Mode.User:
                case ARM7Mode.OldUser:
                case ARM7Mode.System:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13usr = R[13];
                    R14usr = R[14];
                    LineDebug("Saved Registers: User / System");
                    break;

                case ARM7Mode.FIQ:
                case ARM7Mode.OldFIQ:
                    R8fiq = R[8];
                    R9fiq = R[9];
                    R10fiq = R[10];
                    R11fiq = R[11];
                    R12fiq = R[12];
                    R13fiq = R[13];
                    R14fiq = R[14];
                    LineDebug("Saved Registers: FIQ");
                    break;

                case ARM7Mode.Supervisor:
                case ARM7Mode.OldSupervisor:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13svc = R[13];
                    R14svc = R[14];
                    LineDebug("Saved Registers: Supervisor");
                    break;

                case ARM7Mode.Abort:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13abt = R[13];
                    R14abt = R[14];
                    LineDebug("Saved Registers: Abort");
                    break;

                case ARM7Mode.IRQ:
                case ARM7Mode.OldIRQ:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13irq = R[13];
                    R14irq = R[14];
                    LineDebug("Saved Registers: IRQ");
                    break;

                case ARM7Mode.Undefined:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13und = R[13];
                    R14und = R[14];
                    LineDebug("Saved Registers: Undefined");
                    break;
            }

            switch (mode)
            {
                case 0x00:
                    Mode = ARM7Mode.OldUser;
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13usr = R[13];
                    R14usr = R[14];
                    LineDebug($"Mode Switch: OldUser");
                    break;
                case 0x01:
                    Mode = ARM7Mode.OldFIQ;
                    R[8] = R8fiq;
                    R[9] = R9fiq;
                    R[10] = R10fiq;
                    R[11] = R11fiq;
                    R[12] = R12fiq;
                    R[13] = R13fiq;
                    R[14] = R14fiq;
                    LineDebug($"Mode Switch: OldFIQ");
                    break;
                case 0x02:
                    Mode = ARM7Mode.OldIRQ;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13irq;
                    R[14] = R14irq;
                    LineDebug($"Mode Switch: OldIRQ");
                    break;
                case 0x03:
                    Mode = ARM7Mode.OldSupervisor;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13svc;
                    R[14] = R14svc;
                    LineDebug($"Mode Switch: OldSupervisor");
                    break;

                case 0x10:
                    Mode = ARM7Mode.User;
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13usr = R[13];
                    R14usr = R[14];
                    LineDebug($"Mode Switch: User");
                    break;
                case 0x11:
                    Mode = ARM7Mode.FIQ;
                    R[8] = R8fiq;
                    R[9] = R9fiq;
                    R[10] = R10fiq;
                    R[11] = R11fiq;
                    R[12] = R12fiq;
                    R[13] = R13fiq;
                    R[14] = R14fiq;
                    LineDebug($"Mode Switch: FIQ");
                    break;
                case 0x12:
                    Mode = ARM7Mode.IRQ;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13irq;
                    R[14] = R14irq;
                    LineDebug($"Mode Switch: IRQ");
                    break;
                case 0x13:
                    Mode = ARM7Mode.Supervisor;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13svc;
                    R[14] = R14svc;
                    LineDebug($"Mode Switch: Supervisor");
                    break;
                case 0x17:
                    Mode = ARM7Mode.Abort;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13abt;
                    R[14] = R14abt;
                    LineDebug($"Mode Switch: Abort");
                    break;
                case 0x1B:
                    Mode = ARM7Mode.Undefined;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13und;
                    R[14] = R14und;
                    LineDebug($"Mode Switch: Undefined");
                    break;
                case 0x1F:
                    Mode = ARM7Mode.System;
                    R[8] = R8usr;
                    R[9] = R9usr;
                    R[10] = R10usr;
                    R[11] = R11usr;
                    R[12] = R12usr;
                    R[13] = R13usr;
                    R[14] = R14usr;
                    LineDebug($"Mode Switch: System");
                    break;
                default:
                    Error($"Invalid SetMode: {mode}");
                    return;
            }
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
            return ((val1 ^ val2) & 0x80000000u) != 0 && ((val1 ^ result) & 0x80000000u) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CheckOverflowAdd(uint val1, uint val2, uint result)
        {
            return ((val1 ^ val2) & 0x80000000u) == 0 && ((val1 ^ result) & 0x80000000u) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            return Gba.Mem.Read8(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16(uint addr)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            return Gba.Mem.Read16(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32(uint addr)
        {
            InstructionCycles += Timing32[(addr >> 24) & 0xF];
            return Gba.Mem.Read32(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16InstrFetch(uint addr)
        {
            InstructionCycles += Timing8And16InstrFetch[(addr >> 24) & 0xF];
            return Gba.Mem.Read16(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32InstrFetch(uint addr)
        {
            InstructionCycles += Timing32InstrFetch[(addr >> 24) & 0xF];
            return Gba.Mem.Read32(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write8(uint addr, byte val)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            Gba.Mem.Write8(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
            InstructionCycles += Timing8And16[(addr >> 24) & 0xF];
            Gba.Mem.Write16(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write32(uint addr, uint val)
        {
            InstructionCycles += Timing32[(addr >> 24) & 0xF];
            Gba.Mem.Write32(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ICycle()
        {
            InstructionCycles += 1;
        }

        public static readonly byte[] Timing8And16 = {
            1, // BIOS
            1, // Unused
            3, // EWRAM
            1, // IWRAM
            1, // I/O Registers
            1, // PPU Palettes
            1, // PPU VRAM
            1, // PPU OAM

            5, // Game Pak ROM/FlashROM 
            5, // Game Pak ROM/FlashROM 
            5, // Game Pak ROM/FlashROM 
            5, // Game Pak ROM/FlashROM 
            5, // Game Pak ROM/FlashROM 
            5, // Game Pak ROM/FlashROM

            5, // Game Pak SRAM/Flash
            5, // Game Pak SRAM/Flash
        };

        public static readonly byte[] Timing32 = {
            1, // BIOS
            1, // Unused
            6, // EWRAM
            1, // IWRAM
            1, // I/O Registers
            2, // PPU Palettes
            2, // PPU VRAM
            1, // PPU OAM

            8, // Game Pak ROM/FlashROM 
            8, // Game Pak ROM/FlashROM 
            8, // Game Pak ROM/FlashROM 
            8, // Game Pak ROM/FlashROM 
            8, // Game Pak ROM/FlashROM 
            8, // Game Pak ROM/FlashROM

            8, // Game Pak SRAM/Flash
            8, // Game Pak SRAM/Flash
        };

        public static readonly byte[] Timing8And16InstrFetch = {
            1, // BIOS
            1, // Unused
            3, // EWRAM
            1, // IWRAM
            1, // I/O Registers
            1, // PPU Palettes
            1, // PPU VRAM
            1, // PPU OAM

            // Compensate for no prefetch buffer 5 -> 2
            2, // Game Pak ROM/FlashROM 
            2, // Game Pak ROM/FlashROM 
            2, // Game Pak ROM/FlashROM 
            2, // Game Pak ROM/FlashROM 
            2, // Game Pak ROM/FlashROM 
            2, // Game Pak ROM/FlashROM

            5, // Game Pak SRAM/Flash
            5, // Game Pak SRAM/Flash
        };

        public static readonly byte[] Timing32InstrFetch = {
            1, // BIOS
            1, // Unused
            6, // EWRAM
            1, // IWRAM
            1, // I/O Registers
            2, // PPU Palettes
            2, // PPU VRAM
            1, // PPU OAM

            // Compensate for no prefetch buffer 8 -> 4
            4, // Game Pak ROM/FlashROM 
            4, // Game Pak ROM/FlashROM 
            4, // Game Pak ROM/FlashROM 
            4, // Game Pak ROM/FlashROM 
            4, // Game Pak ROM/FlashROM 
            4, // Game Pak ROM/FlashROM

            8, // Game Pak SRAM/Flash
            8, // Game Pak SRAM/Flash
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (uint rd, bool setFlags) ArmDataOperandDecode(uint ins)
        {
            bool setFlags = (ins & BIT_20) != 0;
            uint rd = (ins >> 12) & 0xF; // Rd, SBZ for CMP

            return (rd, setFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (uint shifterOperand, bool shifterCarryOut, uint rnVal) ArmDataShiftAndApplyFlags(uint ins)
        {
            // ----- When using register as 2nd operand -----
            // Shift by immediate or shift by register
            bool useImmediate32 = (ins & BIT_25) != 0;

            uint shifterOperand = 0;
            bool shifterCarryOut = false;

            if (useImmediate32)
            {
                uint rn = (ins >> 16) & 0xF; // Rn
                // uint rs = (ins >> 8) & 0xF;
                // uint rm = ins & 0xF;
                uint rnVal = R[rn];
                // uint rsVal = R[rs];
                // uint rmVal = R[rm];

                uint rotateBits = ((ins >> 8) & 0xF) * 2;
                uint constant = ins & 0xFF;

                shifterOperand = RotateRight32(constant, (byte)rotateBits);
                if (rotateBits == 0)
                {
                    shifterCarryOut = Carry;
                }
                else
                {
                    shifterCarryOut = BitTest(shifterOperand, 31);
                }

                LineDebug($"Immediate32: {Util.Hex(shifterOperand, 8)}");

                return (shifterOperand, shifterCarryOut, rnVal);
            }
            else
            {
                bool regShift = (ins & BIT_4) != 0;

                byte shiftBits;
                uint shiftType = (ins >> 5) & 0b11;

                if (!regShift)
                {
                    // Immediate Shift
                    LineDebug("Immediate Shift");
                    shiftBits = (byte)((ins >> 7) & 0b11111);

                    uint rn = (ins >> 16) & 0xF; // Rn
                    // uint rs = (ins >> 8) & 0xF;
                    uint rm = ins & 0xF;
                    uint rnVal = R[rn];
                    // uint rsVal = R[rs];
                    uint rmVal = R[rm];

                    switch (shiftType)
                    {
                        case 0b00: // LSL
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = Carry;
                            }
                            else
                            {
                                shifterOperand = LogicalShiftLeft32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(32 - shiftBits));
                            }
                            break;
                        case 0b01: // LSR
                            if (shiftBits == 0)
                            {
                                shifterOperand = 0;
                                shifterCarryOut = BitTest(rmVal, 31);
                            }
                            else
                            {
                                shifterOperand = LogicalShiftRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            break;
                        case 0b10: // ASR
                            if (shiftBits == 0)
                            {
                                if (!BitTest(rmVal, 31))
                                {
                                    shifterOperand = 0;
                                    shifterCarryOut = false;
                                }
                                else
                                {
                                    shifterOperand = 0xFFFFFFFF;
                                    shifterCarryOut = true;
                                }
                            }
                            else
                            {
                                shifterOperand = ArithmeticShiftRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            break;
                        case 0b11: // ROR
                            if (shiftBits == 0)
                            {
                                shifterOperand = LogicalShiftLeft32(Carry ? 1U : 0, 31) | LogicalShiftRight32(rmVal, 1);
                                shifterCarryOut = BitTest(rmVal, 0);
                            }
                            else
                            {
                                shifterOperand = RotateRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            break;
                    }

                    return (shifterOperand, shifterCarryOut, rnVal);
                }
                else
                {
                    // Register shift
                    LineDebug("Register Shift");

                    uint rn = (ins >> 16) & 0xF; // Rn
                    uint rs = (ins >> 8) & 0xF;
                    uint rm = ins & 0xF;
                    LineDebug("RS: " + rs);

                    ICycle();

                    R[15] += 4;
                    uint rnVal = R[rn];
                    uint rsVal = R[rs];
                    uint rmVal = R[rm];
                    R[15] -= 4;

                    shiftBits = (byte)(rsVal & 0b11111111);

                    switch (shiftType)
                    {
                        case 0b00:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = Carry;
                                break;
                            }

                            if (shiftBits >= 32)
                            {
                                if (shiftBits > 32)
                                {
                                    shifterCarryOut = false;
                                }
                                else
                                {
                                    shifterCarryOut = BitTest(rmVal, 0);
                                }
                                shifterOperand = 0;
                                break;
                            }

                            shifterOperand = rmVal << shiftBits;
                            shifterCarryOut = BitTest(rmVal, (byte)(32 - shiftBits));
                            break;
                        case 0b01:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = Carry;
                            }
                            else if (shiftBits < 32)
                            {
                                shifterOperand = LogicalShiftRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            else if (shiftBits == 32)
                            {
                                shifterOperand = 0;
                                shifterCarryOut = BitTest(rmVal, 31);
                            }
                            else
                            {
                                shifterOperand = 0;
                                shifterCarryOut = false;
                            }
                            break;
                        case 0b10:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = Carry;
                            }
                            else if (shiftBits < 32)
                            {
                                shifterOperand = ArithmeticShiftRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            else if (shiftBits >= 32)
                            {
                                if (!BitTest(rmVal, 31))
                                {
                                    shifterOperand = 0;
                                    shifterCarryOut = false;
                                }
                                else
                                {
                                    shifterOperand = 0xFFFFFFFF;
                                    shifterCarryOut = true;
                                }
                            }
                            break;
                        case 0b11:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = Carry;
                            }
                            else
                            {
                                shifterOperand = RotateRight32(rmVal, (byte)(shiftBits & 0b11111));
                                shifterCarryOut = BitTest(rmVal, (byte)((shiftBits & 0b11111) - 1));
                            }
                            break;
                    }

                    return (shifterOperand, shifterCarryOut, rnVal);
                }
            }
        }
    }
}
