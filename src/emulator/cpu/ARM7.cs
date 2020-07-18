using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class ARM7
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

        GBA Gba;
        public uint R0;
        public uint R1;
        public uint R2;
        public uint R3;
        public uint R4;
        public uint R5;
        public uint R6;
        public uint R7;
        public uint R8;
        public uint R9;
        public uint R10;
        public uint R11;
        public uint R12;
        public uint R13;
        public uint R14;
        public uint R15;

        public bool Negative = false;
        public bool Zero = false;
        public bool Carry = false;
        public bool Overflow = false;
        public bool Sticky = false;
        public bool IRQDisable = false;
        public bool FIQDisable = false;
        public bool ThumbState = false;
        ARM7Mode Mode = ARM7Mode.System;

        // DEBUG INFO
        public uint LastIns;

        public ARM7(GBA gba)
        {
            Gba = gba;
            R15 = 0;
            // Boot BIOS
            // R15 = 0x00000000;
            // Boot game
            R15 = 0x08000000;

            // Default Mode
            Mode = ARM7Mode.System;

            // Default Stack Pointer
            R13 = 0x03007F00;
        }

        public void Execute()
        {
            ResetDebug();

            // Current Instruction Fetch
            LineDebug($"R15: ${R15:X}");
            byte f0 = Gba.Mem.Read8(R15++);
            byte f1 = Gba.Mem.Read8(R15++);
            byte f2 = Gba.Mem.Read8(R15++);
            byte f3 = Gba.Mem.Read8(R15++);

            uint ins = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));
            LastIns = ins;

            LineDebug($"Ins: ${ins:X}");
            LineDebug($"Cond: ${ins >> 28:X}");

            uint condition = (ins >> 28) & 0xF;

            bool conditionMet = false;
            switch (condition)
            {
                case 0x0: // Zero, Equal, Z = 1
                    conditionMet = Zero;
                    break;
                case 0x1: // Nonzero, Not Equal, Z = 0
                    conditionMet = !Zero;
                    break;
                case 0x4: // Signed Negative, Minus, N=1
                    conditionMet = Negative;
                    break;
                case 0xD: // Signed less or Equal, Z=1 or N!=V
                    conditionMet = Zero || (Overflow != Negative);
                    break;
                case 0xE: // Always
                    conditionMet = true;
                    break;
                default:
                    throw new Exception($"Invalid condition? {Util.Hex(condition, 1)}");
            }

            if (conditionMet)
            {
                if ((ins & 0b1110000000000000000000000000) == 0b1010000000000000000000000000)
                {
                    int offset = (int)(ins & 0b111111111111111111111111) << 2;
                    // Signed with Two's Complement
                    if ((offset & BIT_25) != 0)
                    {
                        LineDebug("Backward Branch");
                        offset -= (int)BIT_26;
                    }
                    else
                    {
                        LineDebug("Forward Branch");
                    }

                    // Link - store return address in R14
                    if ((ins & BIT_24) != 0)
                    {
                        R14 = R15;
                    }

                    R15 = (uint)(R15 + offset + 4);
                }
                else if ((ins & 0b1101101100000000000000000000) == 0b0001001000000000000000000000)
                {
                    LineDebug("PSR Transfer (MRS, MSR)");
                    // MSR
                    uint UnallocMask = 0x0FFFFF00;
                    uint UserMask = 0xF0000000;
                    uint PrivMask = 0x0000000F;
                    uint StateMask = 0x00000020;

                    bool writeSPSR = BitTest(ins, 22);

                    bool setControl = BitTest(ins, 16);
                    bool setExtension = BitTest(ins, 17);
                    bool setStatus = BitTest(ins, 18);
                    bool setFlags = BitTest(ins, 19);

                    bool useImmediate = BitTest(ins, 25);

                    uint operand;

                    if (useImmediate)
                    {
                        uint rotateBits = ((ins >> 8) & 0xF) * 2;
                        uint constant = ins & 0xFF;

                        operand = RotateRight32(constant, (byte)rotateBits);
                    }
                    else
                    {
                        operand = GetReg(ins & 0xF);
                    }

                    uint byteMask =
                        (setControl ? 0x000000FFu : 0) |
                        (setExtension ? 0x0000FF00u : 0) |
                        (setStatus ? 0x00FF0000u : 0) |
                        (setFlags ? 0xFF000000u : 0);

                    uint mask;

                    if (!writeSPSR)
                    {
                        // TODO: Fix privileged mode functionality in CPSR MSR
                        if (Mode != ARM7Mode.User)
                        {
                            // Privileged
                            mask = byteMask & (UserMask | PrivMask);
                        }
                        else
                        {
                            // Unprivileged
                            mask = byteMask & UserMask;
                        }
                        SetCPSR((GetCPSR() & ~mask) | (operand & mask));
                    }
                    else
                    {
                        // TODO: Add SPSR functionality to MSR
                        throw new Exception("Attempted using SPSR MSR");
                    }

                }
                else if ((ins & 0b1100000000000000000000000000) == 0b0000000000000000000000000000)
                {
                    // Bits 27, 26 are 0, so data processing / ALU
                    LineDebug("Data Processing / FSR Transfer");
                    // ALU Operations
                    bool immediate32 = (ins & BIT_25) != 0;
                    uint opcode = (ins >> 21) & 0xF;
                    bool setCondition = (ins & BIT_20) != 0;
                    uint rn = (ins >> 16) & 0xF; // Rn
                    uint rd = (ins >> 12) & 0xF; // Rd, SBZ for CMP

                    uint regShiftId = (ins >> 8) & 0b1111;
                    uint immShift = (ins >> 7) & 0b11111;

                    // ----- When using register as 2nd operand -----
                    // Shift by immediate or shift by register
                    bool shiftByReg = (ins & BIT_4) != 0;
                    LineDebug($"Shift by reg: {shiftByReg}");

                    uint op2Reg = ins & 0b1111;

                    // ----- When using immediate as 2nd operand -----
                    uint operandShift = (ins >> 8) & 0b1111;
                    uint operand = ins & 0b11111111;

                    uint shifterOperand = 0;

                    if (immediate32)
                    {
                        uint rotateBits = ((ins >> 8) & 0xF) * 2;
                        uint constant = ins & 0xFF;

                        shifterOperand = RotateRight32(constant, (byte)rotateBits);

                        LineDebug($"Immediate32: {Util.Hex(shifterOperand, 8)}");
                    }
                    else
                    {
                        bool regShift = (ins & BIT_4) != 0;

                        uint rm = ins & 0xF;
                        byte shiftBits;
                        uint shiftType = (ins >> 5) & 0b11;

                        if (!regShift)
                        {
                            // Immediate Shift
                            shiftBits = (byte)((ins >> 7) & 0b11111);
                        }
                        else
                        {
                            // Register shift
                            uint rs = (ins >> 8) & 0xF;
                            shiftBits = (byte)(GetReg(rs) & 0b11111);
                        }

                        switch (shiftType)
                        {
                            case 0b00:
                                shifterOperand = LogicalShiftLeft32(rm, shiftBits);
                                break;
                            case 0b01:
                                shifterOperand = LogicalShiftRight32(rm, shiftBits);
                                break;
                            case 0b10:
                                shifterOperand = ArithmeticShiftRight32(rm, shiftBits);
                                break;
                            case 0b11:
                                shifterOperand = RotateRight32(rm, shiftBits);
                                break;
                        }

                    }

                    switch (opcode)
                    {
                        case 0x4: // ADD
                            {
                                uint rnValue = GetReg(rn);
                                uint final = rnValue + shifterOperand;
                                SetReg(rd, final);
                                if (setCondition && rd == 15)
                                {
                                    // TODO: CPSR = SPSR if current mode has SPSR
                                }
                                else if (setCondition)
                                {
                                    Negative = BitTest(final, 31); // N
                                    Zero = final == 0; // Z
                                    Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // C
                                    Overflow = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // V
                                }
                            }
                            break;
                        case 0x9: // TEQ
                            {
                                uint reg = GetReg(rn);
                                uint aluOut = reg ^ shifterOperand;
                                if (setCondition)
                                {
                                    Negative = BitTest(aluOut, 31); // N
                                    Zero = aluOut == 0; // Z
                                    Carry = BitTest(shifterOperand, 31); // C
                                }
                            }
                            break;
                        case 0xA: // CMP
                            // SBZ means should be zero, not relevant to the current code, just so you know

                            {
                                uint reg = GetReg(rn);
                                uint aluOut = reg - shifterOperand;
                                if (setCondition)
                                {
                                    Negative = BitTest(aluOut, 31); // N
                                    Zero = aluOut == 0; // Z
                                    Carry = !(shifterOperand > reg); // C
                                    Overflow = shifterOperand > reg; // V
                                }
                            }
                            break;
                        case 0xD: // MOV
                            {
                                SetReg(rd /*Rd*/, shifterOperand);
                                if (setCondition)
                                {
                                    Negative = BitTest(shifterOperand, 31); // N
                                    Zero = shifterOperand == 0; // Z
                                    Carry = BitTest(shifterOperand, 31); // C


                                    if (rd == 15)
                                    {
                                        // TODO: Set CPSR to SPSR here
                                    }
                                }
                            }
                            break;
                        default:
                            throw new Exception($"ALU Opcode Unimplemented: {opcode:X}");
                    }


                }
                else if ((ins & 0b1100010000000000000000000000) == 0b0100000000000000000000000000)
                {
                    // LDR/STR (Load Register)/(Store Register)
                    LineDebug("LDR/STR (Load Register)/(Store Register)");

                    uint rn = (ins >> 16) & 0xF;
                    uint rd = (ins >> 12) & 0xF;

                    uint rnValue = GetReg(rn);

                    bool U = BitTest(ins, 23); // invert

                    uint addr;
                    if ((ins & 0b1111001000000000111111110000) == 0b0111000000000000000000000000)
                    {
                        // Register offset
                        LineDebug($"Register Offset");
                        uint rmValue = GetReg(ins & 0xF);

                        if (U)
                        {
                            addr = rnValue + rmValue;
                        }
                        else
                        {
                            addr = rnValue - rmValue;
                        }
                    }
                    else if ((ins & 0b1111001000000000000000000000) == 0b0101000000000000000000000000)
                    {
                        // Immediate offset
                        LineDebug($"Immediate Offset");

                        uint rm = GetReg(ins & 0xF);

                        uint rotateBits = ((ins >> 8) & 0xF) * 2;
                        uint constant = ins & 0xFF;

                        uint offs = RotateRight32(constant, (byte)rotateBits);

                        if (U)
                        {
                            addr = rnValue + offs;
                        }
                        else
                        {
                            addr = rnValue - offs;
                        }
                    }
                    else
                    {
                        throw new Exception($"Unimplemented load/store");
                    }

                    bool L = BitTest(ins, 20);
                    bool B = BitTest(ins, 22);
                    if (L)
                    {
                        uint loadVal;
                        if (B)
                        {
                            loadVal = Gba.Mem.Read8(addr);
                        }
                        else
                        {
                            loadVal = Gba.Mem.Read32(addr);
                        }

                        LineDebug($"LDR Addr: {Util.Hex(addr, 8)}");
                        LineDebug($"LDR Value: {Util.Hex(loadVal, 8)}");

                        SetReg(rd, loadVal);
                    }
                    else
                    {
                        uint storeVal = GetReg(rd);
                        if (B)
                        {
                            Gba.Mem.Write8(addr, (byte)storeVal);
                        }
                        else
                        {
                            Gba.Mem.Write32(addr, storeVal);
                        }

                        LineDebug($"STR Addr: {Util.Hex(addr, 8)}");
                        LineDebug($"STR Value: {Util.Hex(storeVal, 8)}");
                    }
                }
                else
                {
                    throw new Exception("Unimplemented opcode");
                }
            }
        }

        public uint GetReg(uint reg)
        {
            switch (reg)
            {
                case 0x0: return R0;
                case 0x1: return R1;
                case 0x2: return R2;
                case 0x3: return R3;
                case 0x4: return R4;
                case 0x5: return R5;
                case 0x6: return R6;
                case 0x7: return R7;
                case 0x8: return R8;
                case 0x9: return R9;
                case 0xA: return R10;
                case 0xB: return R11;
                case 0xC: return R12;
                case 0xD: return R13;
                case 0xE: return R14;
                case 0xF: return R15;

                default:
                    throw new Exception($"Invalid GetReg: {reg}");
            }

        }


        public void SetReg(uint reg, uint val)
        {
            switch (reg)
            {
                case 0x0: R0 = val; break;
                case 0x1: R1 = val; break;
                case 0x2: R2 = val; break;
                case 0x3: R3 = val; break;
                case 0x4: R4 = val; break;
                case 0x5: R5 = val; break;
                case 0x6: R6 = val; break;
                case 0x7: R7 = val; break;
                case 0x8: R8 = val; break;
                case 0x9: R9 = val; break;
                case 0xA: R10 = val; break;
                case 0xB: R11 = val; break;
                case 0xC: R12 = val; break;
                case 0xD: R13 = val; break;
                case 0xE: R14 = val; break;
                case 0xF: R15 = val; break;

                default:
                    throw new Exception($"Invalid SetReg: {reg}");
            }
        }


        public uint LogicalShiftLeft32(uint n, byte bits)
        {
            return n << bits;
        }

        public uint LogicalShiftRight32(uint n, byte bits)
        {
            return n >> bits;
        }

        public uint ArithmeticShiftRight32(uint n, byte bits)
        {
            uint logical = n >> bits;
            uint mask = BitTest(n, 31) ? 0xFFFFFFFF : 0;

            return logical | (mask << (32 - bits));
        }

        public uint RotateRight32(uint n, byte bits)
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
            // ThumbState = BitTest(val, 5);

            SetMode(val & 0b11111);
        }

        public void SetMode(uint mode)
        {
            switch (mode)
            {
                case 0x00: Mode = ARM7Mode.OldUser; break;
                case 0x01: Mode = ARM7Mode.OldFIQ; break;
                case 0x02: Mode = ARM7Mode.OldIRQ; break;
                case 0x03: Mode = ARM7Mode.OldSupervisor; break;

                case 0x10: Mode = ARM7Mode.User; break;
                case 0x11: Mode = ARM7Mode.FIQ; break;
                case 0x12: Mode = ARM7Mode.IRQ; break;
                case 0x13: Mode = ARM7Mode.Supervisor; break;
                case 0x17: Mode = ARM7Mode.Abort; break;
                case 0x1B: Mode = ARM7Mode.Undefined; break;
                case 0x1F: Mode = ARM7Mode.System; break;
                default:
                    throw new Exception($"Invalid SetMode: {mode}");
            }
        }

        public uint GetMode()
        {
            return (uint)Mode;
        }

        public String Debug = "";

        public void ResetDebug()
        {
            Debug = "";
        }

        public void LineDebug(String s)
        {
            Debug += $"{s}\n";
        }
    }
}
