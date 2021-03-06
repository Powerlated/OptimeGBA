using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public unsafe sealed class Arm7
    {
        public enum Arm7Mode
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

        public Gba Gba;

#if UNSAFE
        public uint* R = Memory.AllocateUnmanagedArray32(16);

        ~Arm7() {
            Memory.FreeUnmanagedArray(R);
        }
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
        public Arm7Mode Mode = Arm7Mode.System;

        public uint Fetch;
        public uint Decode;
        public uint Pipeline; // 0 for empty, 1 for Fetch filled, 2 for Decode filled, 3 for Execute filled (full)

        public bool PipelineDirty = false;

        public long InstructionsRan = 0;
        public long InstructionsRanInterrupt = 0;

        // DEBUG INFO
        public uint LastIns;
        public uint LastLastIns;

        public Arm7(Gba gba)
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
            Mode = Arm7Mode.System;

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
            Decode = Fetch;
            Fetch = Read32InstrFetch(R[15]);
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
            Decode = Fetch;
            Fetch = Read16InstrFetch(R[15]);
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
            if (!ThumbState) // ARM mode
            {
                ExecuteArm();
            }
            else // THUMB mode
            {
                ExecuteThumb();
            }

            CheckInterrupts();

            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExecuteArm()
        {
            InstructionsRan++;
            InstructionCycles = 0;

            LineDebug($"R15: ${Util.HexN(R[15], 4)}");

            uint ins = Decode;
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

            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ExecuteThumb()
        {
            InstructionsRan++;
            InstructionCycles = 0;

            LineDebug($"R15: ${Util.HexN(R[15], 4)}");

            ushort ins = (ushort)Decode;
            int decodeBits = ins >> 6;

            Pipeline--;
#if OPENTK_DEBUGGER
            LastLastIns = LastIns;
            LastIns = ins;
            ThumbExecutorProfile[decodeBits]++;
#endif
            LineDebug($"Ins: ${Util.HexN(ins, 4)} InsBin:{Util.Binary(ins, 16)}");

            ThumbDispatch[decodeBits](this, ins);

            // Fill the pipeline if it's not full
            FetchPipelineThumbIfNotFull();

            return InstructionCycles;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CheckInterrupts()
        {
            if (Gba.HwControl.AvailableAndEnabled && !IRQDisable)
            {
                DispatchInterrupt();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void DispatchInterrupt()
        {
            // Error("sdfkjadfdjsjklfads interupt lol");
#if OPENTK_DEBUGGER
            InstructionsRanInterrupt = InstructionsRan;
#endif

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
            SetMode((uint)Arm7Mode.IRQ); // Go into SVC / Supervisor mode
            ThumbState = false; // Back to ARM state
            IRQDisable = true;
            // FIQDisable = true;

            R[15] = VectorIRQ;
            FlushPipeline();

            // Error("IRQ, ENTERING IRQ MODE!");
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
                if (BitTest(ins, 22)) // use byte
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

                // LineDebug($"Rn: R{rn}");
                // LineDebug($"Rd: R{rd}");

                return ((ins >> 21) & 0xF) switch // opcode
                {
                    0x0 => Arm.DataAND, // AND
                    0x1 => Arm.DataEOR, // EOR
                    0x2 => Arm.DataSUB, // SUB
                    0x3 => Arm.DataRSB, // RSB
                    0x4 => Arm.DataADD, // ADD
                    0x5 => Arm.DataADC, // ADC
                    0x6 => Arm.DataSBC, // SBC
                    0x7 => Arm.DataRSC, // RSC
                    0x8 => Arm.DataTST, // TST
                    0x9 => Arm.DataTEQ, // TEQ
                    0xA => Arm.DataCMP, // CMP
                    0xB => Arm.DataCMN, // CMN
                    0xC => Arm.DataORR, // ORR
                    0xD => Arm.DataMOV, // MOV
                    0xE => Arm.DataBIC, // BIC
                    0xF => Arm.DataMVN // MVN
                };
            }
            // id mask      0b1111111100000000000011110000     0b1111111100000000000011110000
            else if ((ins & 0b1100000000000000000000000000) == 0b0100000000000000000000000000) // LDR / STR
            {
                if (BitTest(ins, 20))
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
                if (BitTest(ins, 20)) // Load vs Store
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
                                return ((ins >> 9) & 0b11) switch
                                {
                                    0b00 => Thumb.ImmAluADD1, // ADD (3)
                                    0b01 => Thumb.ImmAluSUB1, // SUB (3)
                                    0b10 => Thumb.ImmAluADD2, // ADD (1) // MOV (2)
                                    0b11 => Thumb.ImmAluSUB2 // SUB (1)
                                };
                        }
                    }
                    break;
                case 0b001: // Add/subtract/compare/move immediate
                    return ((ins >> 11) & 0b11) switch
                    {
                        0b00 => Thumb.MovImmediate, // MOV (1)
                        0b01 => Thumb.CmpImmediate, // CMP (1)
                        0b10 => Thumb.AddImmediate, // ADD (2)
                        0b11 => Thumb.SubImmediate // SUB (2)
                    };
                case 0b010:
                    {
                        if ((ins & 0b1111110000000000) == 0b0100000000000000) // Data Processing
                        {
                            return ((uint)((ins >> 6) & 0xFU)) switch // opcode
                            {
                                0x0 => Thumb.DataAND, // AND
                                0x1 => Thumb.DataEOR, // EOR
                                0x2 => Thumb.DataLSL, // LSL (2)
                                0x3 => Thumb.DataLSR, // LSR (2)
                                0x4 => Thumb.DataASR, // ASR (2)
                                0x5 => Thumb.DataADC, // ADC
                                0x6 => Thumb.DataSBC, // SBC
                                0x7 => Thumb.DataROR, // ROR
                                0x8 => Thumb.DataTST, // TST
                                0x9 => Thumb.DataTST, // NEG / RSB (wrong value returned?)
                                0xA => Thumb.DataCMP, // CMP (2)
                                0xB => Thumb.DataCMN, // CMN
                                0xC => Thumb.DataORR, // ORR
                                0xD => Thumb.DataMUL, // MUL
                                0xE => Thumb.DataBIC, // BIC
                                0xF => Thumb.DataMVN // MVN
                            };
                        }
                        else if ((ins & 0b1111110000000000) == 0b0100010000000000) // Special Data Processing / Branch-exchange instruction set
                        {
                            return ((ins >> 8) & 0b11) switch
                            {
                                0b00 => Thumb.SpecialDataADD, // ADD (4)
                                0b01 => Thumb.SpecialDataCMP, // CMP (3)
                                0b10 => Thumb.SpecialDataMOV, // MOV (3)
                                0b11 => Thumb.SpecialDataBX // BX
                            };
                        }
                        else if ((ins & 0b1111100000000000) == 0b0100100000000000) // LDR (3) - Load from literal pool
                        {
                            return Thumb.LDRLiteralPool;
                        }
                        else if ((ins & 0b1111000000000000) == 0b0101000000000000) // Load/store register offset
                        {
                            // Remove unused variables?
                            uint rd = (uint)((ins >> 0) & 0b111);
                            uint rn = (uint)((ins >> 3) & 0b111);
                            uint rm = (uint)((ins >> 6) & 0b111);

                            return ((ins >> 9) & 0b111) switch
                            {
                                0b000 => Thumb.RegOffsSTR, // STR (2)
                                0b001 => Thumb.RegOffsSTRH, // STRH (2)
                                0b010 => Thumb.RegOffsSTRB, // STRB (2)
                                0b011 => Thumb.RegOffsLDRSB, // LDRSB
                                0b100 => Thumb.RegOffsLDR, // LDR (2)
                                0b101 => Thumb.RegOffsLDRH, // LDRH (2)
                                0b110 => Thumb.RegOffsLDRB, // LDRB (2)
                                0b111 => Thumb.RegOffsLDRSH, // LDRSH
                                    // default:
                                    //     Error("Load/store register offset invalid opcode");
                            };
                        }
                    }
                    break;
                case 0b011: // Load/store word/byte immediate offset
                    return ((ins >> 11) & 0b11) switch
                    {
                        0b01 => Thumb.ImmOffsLDR, // LDR (1)
                        0b00 => Thumb.ImmOffsSTR, // STR (1)
                        0b10 => Thumb.ImmOffsSTRB, // STRB (1)
                        0b11 => Thumb.ImmOffsLDRB // LDRB (1)
                    };
                case 0b100:
                    {
                        if ((ins & 0b1111000000000000) == 0b1000000000000000) // STRH (1) / LDRH (1) - Load/Store Halfword Immediate Offset
                        {
                            if (BitTest(ins, 11)) // load
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
            bool? result = code switch
            {
                0x0 => Zero, // Zero, Equal, Z=1
                0x1 => !Zero, // Nonzero, Not Equal, Z=0
                0x2 => Carry, // Unsigned higher or same, C=1
                0x3 => !Carry, // Unsigned lower, C=0
                0x4 => Negative, // Signed Negative, Minus, N=1
                0x5 => !Negative, // Signed Positive or Zero, Plus, N=0
                0x6 =>  Overflow, // Signed Overflow, V=1
                0x7 => !Overflow, // Signed No Overflow, V=0
                0x8 => Carry && !Zero, // Unsigned Higher, C=1 && Z=0
                0x9 => !Carry || Zero, // Unsigned Lower or Same
                0xA => Negative == Overflow, // Signed Greater or Equal
                0xB => Negative != Overflow, // Signed Less Than
                0xC => !Zero && Negative == Overflow, // Signed Greater Than
                0xD => Zero || (Negative != Overflow), // Signed less or Equal, Z=1 or N!=V
                0xE => true, // Always
                _ => null // Default case
            };

            if (result is null)
            {
                Error($"Invalid condition? {Util.Hex(code, 1)}");
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetUserReg(uint reg)
        {
            if (Mode is Arm7Mode.User or Arm7Mode.OldUser or Arm7Mode.System)
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
            if (Mode is Arm7Mode.User or Arm7Mode.OldUser or Arm7Mode.System)
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

            return val | GetMode();
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
                Gba.StateChange();
            }
            ThumbState = newThumbState;

            SetMode(val & 0b01111);
        }

        public uint GetSPSR()
        {
            switch (Mode)
            {
                case Arm7Mode.FIQ:
                case Arm7Mode.OldFIQ:
                    return SPSR_fiq;
                case Arm7Mode.Supervisor:
                case Arm7Mode.OldSupervisor:
                    return SPSR_svc;
                case Arm7Mode.Abort:
                    return SPSR_abt;
                case Arm7Mode.IRQ:
                case Arm7Mode.OldIRQ:
                    return SPSR_irq;
                case Arm7Mode.Undefined:
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
                case Arm7Mode.Supervisor:
                case Arm7Mode.OldSupervisor:
                    SPSR_svc = set;
                    return;
                case Arm7Mode.Abort:
                    SPSR_abt = set;
                    return;
                case Arm7Mode.IRQ:
                case Arm7Mode.OldIRQ:
                    SPSR_irq = set;
                    return;
                case Arm7Mode.Undefined:
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
                case Arm7Mode.User:
                case Arm7Mode.OldUser:
                case Arm7Mode.System:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13usr = R[13];
                    R14usr = R[14];
                    LineDebug("Saved Registers: User / System");
                    break;

                case Arm7Mode.FIQ:
                case Arm7Mode.OldFIQ:
                    R8fiq = R[8];
                    R9fiq = R[9];
                    R10fiq = R[10];
                    R11fiq = R[11];
                    R12fiq = R[12];
                    R13fiq = R[13];
                    R14fiq = R[14];
                    LineDebug("Saved Registers: FIQ");
                    break;

                case Arm7Mode.Supervisor:
                case Arm7Mode.OldSupervisor:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13svc = R[13];
                    R14svc = R[14];
                    LineDebug("Saved Registers: Supervisor");
                    break;

                case Arm7Mode.Abort:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13abt = R[13];
                    R14abt = R[14];
                    LineDebug("Saved Registers: Abort");
                    break;

                case Arm7Mode.IRQ:
                case Arm7Mode.OldIRQ:
                    R8usr = R[8];
                    R9usr = R[9];
                    R10usr = R[10];
                    R11usr = R[11];
                    R12usr = R[12];
                    R13irq = R[13];
                    R14irq = R[14];
                    LineDebug("Saved Registers: IRQ");
                    break;

                case Arm7Mode.Undefined:
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
                    Mode = Arm7Mode.OldUser;
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
                    Mode = Arm7Mode.OldFIQ;
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
                    Mode = Arm7Mode.OldIRQ;
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
                    Mode = Arm7Mode.OldSupervisor;
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
                    Mode = Arm7Mode.User;
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
                    Mode = Arm7Mode.FIQ;
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
                    Mode = Arm7Mode.IRQ;
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
                    Mode = Arm7Mode.Supervisor;
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
                    Mode = Arm7Mode.Abort;
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
                    Mode = Arm7Mode.Undefined;
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
                    Mode = Arm7Mode.System;
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
