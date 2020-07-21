using System;
using System.Runtime.CompilerServices;
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

        public bool Errored = false;

        GBA Gba;
        public uint R0;
        public uint R1;
        public uint R2;
        public uint R3;
        public uint R5;
        public uint R4;
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
        ARM7Mode Mode = ARM7Mode.System;

        public uint ARMFetch;
        public uint ARMDecode;
        public ushort THUMBFetch;
        public ushort THUMBDecode;
        public uint Pipeline; // 0 for empty, 1 for Fetch filled, 2 for Decode filled, 3 for Execute filled (full)

        // DEBUG INFO
        public uint LastIns;

        public ARM7(GBA gba)
        {
            Gba = gba;

            R0 = 0x08000000;
            R1 = 0x000000EA;
            R15 = 0;
            // Boot BIOS
            // R15 = 0x00000000;
            // Boot game
            R15 = 0x08000000;

            // Default Mode
            Mode = ARM7Mode.System;

            R13svc = 0x03007FE0;
            R13irq = 0x03007FA0;
            R13usr = 0x03007F00;

            // Default Stack Pointer
            R13 = R13usr;

            BiosInit();
        }

        public void BiosInit()
        {
            Zero = true;
            Carry = true;
        }

        public void FillPipelineArm()
        {
            while (Pipeline < 2)
            {
                byte f0 = Gba.Mem.Read8(R15++);
                byte f1 = Gba.Mem.Read8(R15++);
                byte f2 = Gba.Mem.Read8(R15++);
                byte f3 = Gba.Mem.Read8(R15++);

                ARMDecode = ARMFetch;
                ARMFetch = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                Pipeline++;
            }
        }

        public void FillPipelineThumb()
        {
            while (Pipeline < 2)
            {
                byte f0 = Gba.Mem.Read8(R15++);
                byte f1 = Gba.Mem.Read8(R15++);

                THUMBDecode = THUMBFetch;
                THUMBFetch = (ushort)((f1 << 8) | (f0 << 0));

                Pipeline++;
            }
        }

        public void FlushPipeline()
        {
            Pipeline = 0;
            if (ThumbState)
            {
                FillPipelineThumb();
            }
            else
            {
                FillPipelineArm();
            }
        }

        public void Execute()
        {
            ResetDebug();

            if (!ThumbState) // ARM mode
            {
                // Current Instruction Fetch

                LineDebug($"R15: ${Util.HexN(R15, 4)}");

                // Fill the pipeline if it's not full
                FillPipelineArm();

                uint ins = ARMDecode;
                Pipeline--;
                LastIns = ins;

                LineDebug($"Ins: ${Util.HexN(ins, 8)} InsBin:{Util.Binary(ins, 32)}");
                LineDebug($"Cond: ${ins >> 28:X}");

                uint condition = (ins >> 28) & 0xF;

                bool conditionMet = CheckCondition(condition);

                if (conditionMet)
                {
                    if ((ins & 0b1110000000000000000000000000) == 0b1010000000000000000000000000) // B
                    {
                        LineDebug("B | Branch");
                        // B
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
                            R14 = R15 - 4;
                        }

                        R15 = (uint)(R15 + offset);
                        FlushPipeline();
                    }
                    else if ((ins & 0b1111111100000000000011110000) == 0b0001001000000000000000010000) // BX
                    {
                        // BX - branch and optional switch to Thumb state
                        LineDebug("BX");

                        uint rm = ins & 0xF;
                        uint rmValue = GetReg(rm);

                        ThumbState = BitTest(rmValue, 0);
                        if (ThumbState)
                        {
                            LineDebug("Switch to THUMB State");
                        }
                        else
                        {
                            LineDebug("Switch to ARM State");
                        }

                        R15 = (rmValue & 0xFFFFFFFE);
                        FlushPipeline();
                    }
                    else if ((ins & 0b1111101100000000000011110000) == 0b0001000000000000000010010000) // SWP / SWPB
                    {
                        bool useByte = BitTest(ins, 22);

                        uint rm = (ins >> 0) & 0xF;
                        uint rd = (ins >> 12) & 0xF;
                        uint rn = (ins >> 16) & 0xF;

                        uint addr = GetReg(rn);
                        uint storeValue = GetReg(rm);



                        if (useByte)
                        {
                            LineDebug("SWPB");
                            byte readVal = Gba.Mem.Read8(addr);
                            Gba.Mem.Write8(addr, (byte)storeValue);
                            SetReg(rd, readVal);
                        }
                        else
                        {
                            LineDebug("SWP");
                            uint readVal = Gba.Mem.Read32(addr);
                            Gba.Mem.Write32(addr, storeValue);
                            SetReg(rd, readVal);
                        }
                    }
                    else if ((ins & 0b1111101100000000000000000000) == 0b0001000000000000000000000000) // MRS
                    {
                        LineDebug("MRS");

                        bool useSPSR = BitTest(ins, 22);

                        uint rd = (ins >> 12) & 0xF;

                        if (useSPSR)
                        {
                            LineDebug("Rd from SPSR");
                            SetReg(rd, GetSPSR());
                        }
                        else
                        {
                            LineDebug("Rd from CPSR");
                            SetReg(rd, GetCPSR());
                        }
                    }
                    else if ((ins & 0b1101101100000000000000000000) == 0b0001001000000000000000000000) // MSR
                    {
                        LineDebug("MSR");
                        // MSR

                        bool useSPSR = BitTest(ins, 22);

                        LineDebug("MSR");
                        // uint UnallocMask = 0x0FFFFF00;
                        uint UserMask = 0xF0000000;
                        uint PrivMask = 0x0000000F;
                        uint StateMask = 0x00000020;

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

                        if (!useSPSR)
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
                            mask = byteMask & (UserMask | PrivMask | StateMask);
                            SetSPSR((GetSPSR() & ~mask) | (operand & mask));
                        }
                    }
                    else if ((ins & 0b1111110000000000000011110000) == 0b0000000000000000000010010000) // Multiply Regular
                    {
                        uint rd = (ins >> 16) & 0xF;
                        uint rs = (ins >> 8) & 0xF;
                        uint rm = (ins >> 0) & 0xF;
                        uint rsValue = GetReg(rs);
                        uint rmValue = GetReg(rm);

                        LineDebug($"R{rm} * R{rs}");
                        LineDebug($"${Util.HexN(rmValue, 8)} * ${Util.HexN(rsValue, 8)}");

                        bool setFlags = BitTest(ins, 20);

                        uint final;
                        if (BitTest(ins, 21))
                        {
                            uint rnValue = GetReg((ins >> 12) & 0xF);
                            LineDebug("Multiply Accumulate");
                            final = (rsValue * rmValue) + rnValue;
                        }
                        else
                        {
                            LineDebug("Multiply Regular");
                            final = rsValue * rmValue;
                        }
                        SetReg(rd, final);

                        if (setFlags)
                        {
                            Negative = BitTest(final, 31);
                            Zero = final == 0;
                        }
                    }
                    else if ((ins & 0b1111100000000000000011110000) == 0b0000100000000000000010010000) // Multiply Long
                    {
                        bool signed = BitTest(ins, 22);
                        bool accumulate = BitTest(ins, 21);
                        bool setFlags = BitTest(ins, 20);

                        uint rdHi = (ins >> 16) & 0xF;
                        uint rdLo = (ins >> 12) & 0xF;
                        uint rs = (ins >> 8) & 0xF;
                        uint rm = (ins >> 0) & 0xF;
                        ulong rsVal = GetReg(rs);
                        ulong rmVal = GetReg(rm);

                        LineDebug("Multiply Long");

                        ulong longLo;
                        ulong longHi;
                        if (accumulate)
                        {
                            LineDebug("Accumulate");

                            if (signed)
                            {
                                // SMLAL
                                long rmValExt = (long)rmVal;
                                long rsValExt = (long)rsVal;

                                const long sub = (1L << 32);

                                if ((rmVal & (1u << 31)) != 0) rmValExt -= sub;
                                if ((rsVal & (1u << 31)) != 0) rsValExt -= sub;

                                longLo = (ulong)(((rsValExt * rmValExt) & 0xFFFFFFFF) + GetReg(rdLo));
                                longHi = (ulong)((rsValExt * rmValExt) >> 32) + GetReg(rdHi) + (longLo > 0xFFFFFFFF ? 1U : 0);
                            }
                            else
                            {
                                // UMLAL
                                longLo = ((rsVal * rmVal) & 0xFFFFFFFF) + GetReg(rdLo);
                                longHi = ((rsVal * rmVal) >> 32) + GetReg(rdHi) + (longLo > 0xFFFFFFFF ? 1U : 0);
                            }
                        }
                        else
                        {
                            LineDebug("No Accumulate");

                            if (signed)
                            {
                                // SMULL
                                long rmValExt = (long)rmVal;
                                long rsValExt = (long)rsVal;

                                const long sub = (1L << 32);

                                if ((rmVal & (1u << 31)) != 0) rmValExt -= sub;
                                if ((rsVal & (1u << 31)) != 0) rsValExt -= sub;

                                longLo = (ulong)((rsValExt * rmValExt));
                                longHi = (ulong)((rsValExt * rmValExt) >> 32);
                            }
                            else
                            {
                                // UMULL
                                longLo = (rmVal * rsVal);
                                longHi = ((rmVal * rsVal) >> 32);
                            }
                        }

                        LineDebug($"RdLo: R{rdLo}");
                        LineDebug($"RdHi: R{rdHi}");
                        LineDebug($"Rm: R{rm}");
                        LineDebug($"Rs: R{rs}");


                        SetReg(rdLo, (uint)longLo);
                        SetReg(rdHi, (uint)longHi);

                        if (setFlags)
                        {
                            Negative = BitTest((uint)longHi, 31);
                            Zero = GetReg(rdLo) == 0 && GetReg(rdHi) == 0;
                        }
                    }
                    else if ((ins & 0b1110000000000000000010010000) == 0b0000000000000000000010010000) // Halfword, Signed Byte, Doubleword Loads and Stores
                    {
                        LineDebug("Halfword, Signed Byte, Doubleword Loads & Stores");
                        LineDebug("LDR|STR H|SH|SB|D");

                        bool L = BitTest(ins, 20);
                        bool S = BitTest(ins, 6);
                        bool H = BitTest(ins, 5);


                        bool W = BitTest(ins, 21); // Writeback to base register
                        bool immediateOffset = BitTest(ins, 22);
                        bool U = BitTest(ins, 23); // Add / Subtract offset
                        bool P = BitTest(ins, 24); // Use post-indexed / offset or pre-indexed 

                        uint rd = (ins >> 12) & 0xF;
                        uint rn = (ins >> 16) & 0xF;

                        uint baseAddr = GetReg(rn);

                        uint offset;
                        if (immediateOffset)
                        {
                            LineDebug("Immediate Offset");
                            uint immed = (ins & 0xF) | ((ins >> 4) & 0xF0);
                            offset = immed;
                        }
                        else
                        {
                            LineDebug("Register Offset");
                            uint rm = ins & 0xF;
                            offset = GetReg(rm);
                        }

                        uint addr = baseAddr;
                        if (P)
                        {
                            if (U)
                            {
                                addr += offset;
                            }
                            else
                            {
                                addr -= offset;
                            }
                        }

                        uint loadVal = 0;
                        if (L)
                        {
                            if (S)
                            {
                                if (H)
                                {
                                    LineDebug("Load signed halfword");

                                    int val = (int)Gba.Mem.Read16(addr);
                                    if ((val & BIT_15) != 0)
                                    {
                                        val -= (int)BIT_16;
                                    }

                                    loadVal = (uint)val;
                                }
                                else
                                {
                                    LineDebug("Load signed byte");

                                    int val = (int)Gba.Mem.Read8(addr);
                                    if ((val & BIT_7) != 0)
                                    {
                                        val -= (int)BIT_8;
                                    }

                                    loadVal = (uint)val;
                                }
                            }
                            else
                            {
                                if (H)
                                {
                                    LineDebug("Load unsigned halfword");
                                    loadVal = Gba.Mem.Read16(addr);
                                }
                            }
                        }
                        else
                        {
                            if (S)
                            {
                                if (H)
                                {
                                    LineDebug("Store doubleword");
                                    Error("UNIMPLEMENTED");
                                }
                                else
                                {
                                    LineDebug("Load doubleword");
                                    Error("UNIMPLEMENTED");
                                }
                            }
                            else
                            {
                                if (H)
                                {
                                    LineDebug("Store halfword");
                                    Gba.Mem.Write16(addr, (ushort)GetReg(rd));
                                }
                            }
                        }

                        if (!P)
                        {
                            if (U)
                            {
                                addr = baseAddr + offset;
                            }
                            else
                            {
                                addr = baseAddr - offset;
                            }
                        }

                        if (W || !P)
                        {
                            SetReg(rn, addr);
                        }

                        if (L) {
                            SetReg(rd, loadVal);
                        }

                        LineDebug($"Writeback: {(W ? "Yes" : "No")}");
                        LineDebug($"Offset / pre-indexed addressing: {(P ? "Yes" : "No")}");

                    }
                    else if ((ins & 0b1100000000000000000000000000) == 0b0000000000000000000000000000) // Data Processing // ALU
                    {
                        // Bits 27, 26 are 0, so data processing / ALU
                        LineDebug("Data Processing / FSR Transfer");
                        // ALU Operations
                        bool immediate32 = (ins & BIT_25) != 0;
                        uint opcode = (ins >> 21) & 0xF;
                        bool setFlags = (ins & BIT_20) != 0;
                        uint rn = (ins >> 16) & 0xF; // Rn
                        uint rd = (ins >> 12) & 0xF; // Rd, SBZ for CMP

                        // ----- When using register as 2nd operand -----
                        // Shift by immediate or shift by register

                        uint shifterOperand = 0;
                        bool shifterCarryOut = Carry;

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
                            uint rmVal = GetReg(rm);
                            byte shiftBits;
                            uint shiftType = (ins >> 5) & 0b11;

                            if (!regShift)
                            {
                                // Immediate Shift
                                shiftBits = (byte)((ins >> 7) & 0b11111);

                                switch (shiftType)
                                {
                                    case 0b00:
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
                                    case 0b01:
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
                                    case 0b10:
                                        if (shiftBits == 0)
                                        {
                                            shifterCarryOut = BitTest(rmVal, 31);
                                            if (!BitTest(rmVal, 31))
                                            {
                                                shifterOperand = 0;
                                            }
                                            else
                                            {
                                                shifterOperand = 0xFFFFFFFF;
                                            }
                                        }
                                        else
                                        {
                                            shifterOperand = ArithmeticShiftRight32(rmVal, shiftBits);
                                            shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                                        }
                                        break;
                                    case 0b11:
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
                            }
                            else
                            {
                                // Register shift
                                uint rs = (ins >> 8) & 0xF;
                                uint rsVal = GetReg(rs);

                                if (rs == 15) rsVal += 4;
                                if (rm == 15) rmVal += 4;

                                shiftBits = (byte)(rsVal & 0b11111111);



                                switch (shiftType)
                                {
                                    case 0b00:
                                        if (shiftBits == 0)
                                        {
                                            shifterOperand = rmVal;
                                            shifterCarryOut = Carry;
                                        }
                                        else if (shiftBits < 32)
                                        {
                                            shifterOperand = LogicalShiftLeft32(rmVal, shiftBits);
                                            shifterCarryOut = BitTest(rmVal, (byte)(32 - shiftBits));
                                        }
                                        else if (shiftBits == 32)
                                        {
                                            shifterOperand = 0;
                                            shifterCarryOut = BitTest(rmVal, 0);
                                        }
                                        else
                                        {
                                            shifterOperand = 0;
                                            shifterCarryOut = false;
                                        }
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
                                        else if ((shiftBits & 0b11111) == 0)
                                        {
                                            shifterOperand = rmVal;
                                            shifterCarryOut = BitTest(rmVal, 31);
                                        }
                                        else if ((shiftBits & 0b11111) > 0)
                                        {
                                            shifterOperand = RotateRight32(rmVal, (byte)(shiftBits & 0b11111));
                                            shifterCarryOut = BitTest(rmVal, (byte)((shiftBits & 0b11111) - 1));
                                        }
                                        break;
                                }
                            }
                        }

                        LineDebug($"Rn: R{rn}");
                        LineDebug($"Rd: R{rd}");

                        switch (opcode)
                        {
                            case 0x0: // AND
                                {
                                    LineDebug("AND");

                                    uint rnValue = GetReg(rn);

                                    uint final = rnValue & shifterOperand;
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31);
                                        Zero = final == 0;
                                        Carry = shifterCarryOut;
                                    }
                                }
                                break;
                            case 0x1: // EOR
                                {
                                    LineDebug("EOR");

                                    uint rnValue = GetReg(rn);

                                    uint final = rnValue ^ shifterOperand;
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31);
                                        Zero = final == 0;
                                        Carry = shifterCarryOut;
                                    }
                                }
                                break;
                            case 0x2: // SUB
                                {
                                    LineDebug("SUB");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = rnValue - shifterOperand;

                                    SetReg(rd, aluOut);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = !(shifterOperand > rnValue); // C
                                        Overflow = CheckOverflow(rnValue, shifterOperand, aluOut); // V
                                    }
                                }
                                break;
                            case 0x3: // RSB
                                {
                                    LineDebug("RSB");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = shifterOperand - rnValue;

                                    SetReg(rd, aluOut);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = !(rnValue > shifterOperand); // C
                                        Overflow = CheckOverflow(shifterOperand, rnValue, aluOut); // V
                                    }
                                }
                                break;
                            case 0x4: // ADD
                                {
                                    LineDebug("ADD");

                                    uint rnValue = GetReg(rn);
                                    uint final = rnValue + shifterOperand;
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31); // N
                                        Zero = final == 0; // Z
                                        Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // C
                                    }
                                }
                                break;
                            case 0x5: // ADC
                                {
                                    LineDebug("ADC");

                                    uint rnValue = GetReg(rn);
                                    uint final = rnValue + shifterOperand + (Carry ? 1U : 0);
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31); // N
                                        Zero = final == 0; // Z
                                        Carry = (long)rnValue + (long)shifterOperand + (Carry ? 1U : 0) > 0xFFFFFFFFL; // C
                                        Overflow = (long)rnValue + (long)shifterOperand + (Carry ? 1U : 0) > 0xFFFFFFFFL; // V
                                    }
                                }
                                break;
                            case 0x6: // SBC
                                {
                                    LineDebug("SBC");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = rnValue - shifterOperand - (!Carry ? 1U : 0U);

                                    SetReg(rd, aluOut);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = !(shifterOperand + (!Carry ? 1U : 0U) > rnValue); // C
                                        Overflow = CheckOverflow(shifterOperand, rnValue + (!Carry ? 1U : 0), aluOut); // V
                                    }
                                }
                                break;
                            case 0x7: // RSC
                                {
                                    LineDebug("RSC");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = shifterOperand - rnValue - (!Carry ? 1U : 0U);

                                    SetReg(rd, aluOut);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = !(rnValue + (!Carry ? 1U : 0U) > shifterOperand); // C
                                        Overflow = CheckOverflow(shifterOperand, rnValue + (!Carry ? 1U : 0), aluOut); // V
                                    }
                                }
                                break;
                            case 0x8: // TST
                                {
                                    LineDebug("TST");

                                    uint rnValue = GetReg(rn);
                                    uint final = rnValue & shifterOperand;

                                    Negative = BitTest(final, 31);
                                    Zero = final == 0;
                                    Carry = shifterCarryOut;
                                }
                                break;
                            case 0x9: // TEQ
                                {
                                    LineDebug("TEQ");

                                    uint reg = GetReg(rn);
                                    uint aluOut = reg ^ shifterOperand;
                                    if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = shifterCarryOut; // C
                                    }
                                }
                                break;
                            case 0xA: // CMP
                                {
                                    // SBZ means should be zero, not relevant to the current code, just so you know
                                    LineDebug("CMP");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = rnValue - shifterOperand;
                                    if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = rnValue >= shifterOperand; // C
                                        Overflow = CheckOverflow(rnValue, shifterOperand, aluOut); // V
                                    }
                                }
                                break;
                            case 0xB: // CMN
                                {
                                    LineDebug("CMN");

                                    uint rnValue = GetReg(rn);
                                    uint aluOut = rnValue - ~shifterOperand;
                                    if (setFlags)
                                    {
                                        Negative = BitTest(aluOut, 31); // N
                                        Zero = aluOut == 0; // Z
                                        Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // C
                                        Overflow = CheckOverflow(rnValue, ~shifterOperand, aluOut); // V
                                    }
                                }
                                break;
                            case 0xC: // ORR
                                {
                                    LineDebug("ORR");

                                    uint rnValue = GetReg(rn);

                                    uint final = rnValue | shifterOperand;
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31);
                                        Zero = final == 0;
                                        Carry = shifterCarryOut;
                                    }
                                }
                                break;
                            case 0xD: // MOV
                                {
                                    LineDebug("MOV");

                                    SetReg(rd /*Rd*/, shifterOperand);
                                    if (setFlags)
                                    {
                                        Negative = BitTest(shifterOperand, 31); // N
                                        Zero = shifterOperand == 0; // Z
                                        Carry = shifterCarryOut; // C

                                        if (rd == 15)
                                        {
                                            // TODO: Set CPSR to SPSR here
                                        }
                                    }

                                    if (rd == 15)
                                    {
                                        FlushPipeline();
                                    }
                                }
                                break;
                            case 0xE: // BIC
                                {
                                    LineDebug("BIC");

                                    uint final = GetReg(rn) & ~shifterOperand;
                                    SetReg(rd, final);
                                    if (setFlags && rd == 15)
                                    {
                                        // TODO: CPSR = SPSR if current mode has SPSR
                                        throw new Exception("CPSR = SPSR if current mode has SPSR");
                                    }
                                    else if (setFlags)
                                    {
                                        Negative = BitTest(final, 31); // N
                                        Zero = final == 0; // Z
                                        Carry = shifterCarryOut; // C
                                    }
                                }
                                break;
                            case 0xF: // MVN
                                {
                                    LineDebug("MVN");

                                    SetReg(rd /*Rd*/, ~shifterOperand);
                                    if (setFlags)
                                    {
                                        Negative = BitTest(~shifterOperand, 31); // N
                                        Zero = ~shifterOperand == 0; // Z
                                        Carry = shifterCarryOut; ; // C


                                        if (rd == 15)
                                        {
                                            // TODO: Set CPSR to SPSR here
                                        }
                                    }

                                    if (rd == 15)
                                    {
                                        FlushPipeline();
                                    }
                                }
                                break;
                            default:
                                Error($"ALU Opcode Unimplemented: {opcode:X}");
                                return;
                        }


                    }
                    else if ((ins & 0b1100000000000000000000000000) == 0b0100000000000000000000000000) // LDR / STR
                    {
                        // LDR/STR (Load Register)/(Store Register)
                        LineDebug("LDR/STR (Load Register)/(Store Register)");

                        uint rn = (ins >> 16) & 0xF;
                        uint rd = (ins >> 12) & 0xF;
                        uint rnValue = GetReg(rn);

                        bool registerOffset = BitTest(ins, 25);
                        bool P = BitTest(ins, 24); // post-indexed / offset addressing 
                        bool U = BitTest(ins, 23); // invert
                        bool B = BitTest(ins, 22);
                        bool W = BitTest(ins, 21);
                        bool L = BitTest(ins, 20);


                        uint offset;
                        if (registerOffset)
                        {
                            // Register offset
                            LineDebug($"Register Offset");
                            uint rmVal = GetReg(ins & 0xF);

                            if ((ins & 0b111111110000) == 0b000000000000)
                            {
                                LineDebug($"Non-scaled");
                                offset = rmVal;
                            }
                            else
                            {
                                LineDebug($"Scaled");

                                uint shiftType = (ins >> 5) & 0b11;
                                byte shiftBits = (byte)((ins >> 7) & 0b11111);
                                switch (shiftType)
                                {
                                    case 0b00:
                                        offset = LogicalShiftLeft32(rmVal, shiftBits);
                                        break;
                                    case 0b01:
                                        if (shiftBits == 0)
                                        {
                                            offset = 0;
                                        }
                                        else
                                        {
                                            offset = LogicalShiftRight32(rmVal, shiftBits);
                                        }
                                        break;
                                    case 0b10:
                                        if (shiftBits == 0)
                                        {
                                            if (BitTest(rmVal, 31))
                                            {
                                                offset = 0xFFFFFFFF;
                                            }
                                            else
                                            {
                                                offset = 0;
                                            }
                                        }
                                        else
                                        {
                                            offset = ArithmeticShiftRight32(rmVal, shiftBits);
                                        }
                                        break;
                                    case 0b11:
                                        if (shiftBits == 0)
                                        {
                                            offset = LogicalShiftLeft32(Carry ? 1U : 0, 31) | (LogicalShiftRight32(rmVal, 1));
                                        }
                                        else
                                        {
                                            offset = RotateRight32(rmVal, shiftBits);
                                        }
                                        break;
                                    default:
                                        throw new Exception("Invalid shift code?");
                                }
                            }

                        }
                        else
                        {
                            // Immediate offset
                            LineDebug($"Immediate Offset");

                            // if (L && U && !registerOffset && rd == 0 && (ins & 0b111111111111) == 0) Error("sdfsdf");


                            // This IS NOT A SHIFTED 32-BIT IMMEDIATE, IT'S PLAIN 12-BIT!
                            offset = ins & 0b111111111111;
                        }

                        uint addr = rnValue;
                        if (P)
                        {
                            if (U)
                            {
                                addr += offset;
                            }
                            else
                            {
                                addr -= offset;
                            }
                        }

                        LineDebug($"Rn: R{rn}");
                        LineDebug($"Rd: R{rd}");

                        uint loadVal = 0;
                        if (L)
                        {
                            if (B)
                            {
                                loadVal = Gba.Mem.Read8(addr);
                            }
                            else
                            {

                                if ((addr & 0b11) != 0)
                                {

                                    // If the address isn't word-aligned
                                    uint data = Gba.Mem.Read32(addr & 0xFFFFFFFC);
                                    loadVal = RotateRight32(data, (byte)(8 * (addr & 0b11)));

                                    // Error("Misaligned LDR");
                                }
                                else
                                {
                                    loadVal = Gba.Mem.Read32(addr);
                                }
                            }

                            LineDebug($"LDR Addr: {Util.Hex(addr, 8)}");
                            LineDebug($"LDR Value: {Util.Hex(loadVal, 8)}");
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
                                Gba.Mem.Write32(addr & 0xFFFFFFFC, storeVal);
                            }

                            LineDebug($"STR Addr: {Util.Hex(addr, 8)}");
                            LineDebug($"STR Value: {Util.Hex(storeVal, 8)}");
                        }

                        if (!P)
                        {
                            if (U)
                            {
                                addr += offset;
                            }
                            else
                            {
                                addr -= offset;
                            }
                        }

                        if (W || !P)
                        {
                            SetReg(rn, addr);
                        }

                        // Register loading happens after writeback, so if writeback register and Rd are the same, 
                        // the writeback value would be overwritten by Rd.
                        if (L)
                        {
                            SetReg(rd, loadVal);
                        }
                    }
                    else if ((ins & 0b1110000000000000000000000000) == 0b1000000000000000000000000000) // LDM / STM
                    {
                        LineDebug("LDM / STM");

                        bool P = BitTest(ins, 24); // post-indexed / offset addressing 
                        bool U = BitTest(ins, 23); // invert
                        bool S = BitTest(ins, 22);
                        bool W = BitTest(ins, 21);
                        bool L = BitTest(ins, 20); // Load vs Store

                        bool loadsPc = BitTest(ins, 15);
                        bool useUserModeRegs = S && (!L || !loadsPc) && (Mode != ARM7Mode.User && Mode != ARM7Mode.OldUser);

                        if (S)
                        {
                            if (L && loadsPc)
                            {
                                LineDebug("Load CPSR from SPSR");
                                SetCPSR(GetSPSR());
                            }
                        }

                        // if (U && P && W) Error("U & P & W");

                        LineDebug(L ? "Load" : "Store");
                        LineDebug(P ? "No Include Base" : "Include Base");
                        LineDebug(U ? "Upwards" : "Downwards");

                        uint rn = (ins >> 16) & 0xF;

                        uint addr = GetReg(rn);

                        String regs = "";

                        bool disableWriteback = false;
                        // No writeback if base register is included in the register list when loading.

                        if (U)
                        {
                            for (byte r = 0; r < 16; r++)
                            {
                                if (BitTest(ins, r))
                                {
                                    if (r == rn && L) disableWriteback = true;
                                    regs += $"R{r} ";

                                    if (P) addr += 4;

                                    if (!useUserModeRegs)
                                    {
                                        if (L)
                                        { // Load
                                            SetReg(r, Gba.Mem.Read32(addr & 0xFFFFFFFC));
                                        }
                                        else
                                        { // Store
                                            Gba.Mem.Write32(addr & 0xFFFFFFFC, GetReg(r));
                                        }
                                    }
                                    else
                                    {
                                        if (L)
                                        { // Load
                                            SetUserReg(r, Gba.Mem.Read32(addr & 0xFFFFFFFC));
                                        }
                                        else
                                        { // Store
                                            Gba.Mem.Write32(addr & 0xFFFFFFFC, GetUserReg(r));
                                        }
                                    }

                                    if (!P) addr += 4;
                                }
                            }
                        }
                        else
                        {
                            for (byte ri = 0; ri < 16; ri++)
                            {
                                byte r = (byte)(ri ^ 0b1111);
                                if (BitTest(ins, r))
                                {
                                    if (r == rn && L) disableWriteback = true;
                                    regs += $"R{r} ";

                                    if (P) addr -= 4;

                                    if (!useUserModeRegs)
                                    {
                                        if (L)
                                        { // Load
                                            SetReg(r, Gba.Mem.Read32(addr & 0xFFFFFFFC));
                                        }
                                        else
                                        { // Store
                                            Gba.Mem.Write32(addr & 0xFFFFFFFC, GetReg(r));
                                        }
                                    }
                                    else
                                    {
                                        if (L)
                                        { // Load
                                            SetUserReg(r, Gba.Mem.Read32(addr & 0xFFFFFFFC));
                                        }
                                        else
                                        { // Store
                                            Gba.Mem.Write32(addr & 0xFFFFFFFC, GetUserReg(r));
                                        }
                                    }

                                    if (!P) addr -= 4;
                                }
                            }
                        }

                        if (W && !disableWriteback)
                        {
                            SetReg(rn, addr);
                        }

                        LineDebug(regs);
                    }
                    else
                    {
                        Error("Unimplemented opcode");
                    }
                }

            }
            else // THUMB mode
            {
                LineDebug($"R15: ${Util.HexN(R15, 4)}");

                // Fill the pipeline if it's not full
                FillPipelineThumb();

                ushort ins = THUMBDecode;
                Pipeline--;
                LastIns = ins;

                LineDebug($"Ins: ${Util.HexN(ins, 4)} InsBin:{Util.Binary(ins, 16)}");

                if ((ins & 0b1111100000000000) == 0b0110100000000000) // LDR (1)
                {
                    LineDebug("LDR (1) | Base + Immediate");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnValue = GetReg((uint)((ins >> 3) & 0b111));
                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    uint addr = rnValue + (immed5 * 4);
                    LineDebug($"Addr: {Util.HexN(addr, 8)}");

                    SetReg(rd, Gba.Mem.Read32(addr));
                }
                else if ((ins & 0b1111111000000000) == 0b0101100000000000) // LDR (2)
                {
                    LineDebug("LDR (2)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint rnVal = GetReg(rn);
                    uint rm = (uint)((ins >> 6) & 0b111);
                    uint rmVal = GetReg(rm);

                    uint addr = rnVal + rmVal;

                    SetReg(rd, Gba.Mem.Read32(addr));
                }
                else if ((ins & 0b1111100000000000) == 0b0100100000000000) // LDR (3)
                {
                    LineDebug("LDR (3) | PC Relative, 8-bit Immediate");

                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint immed8 = (uint)((ins >> 0) & 0xFF);

                    uint addr = (R15 & 0xFFFFFFFC) + (immed8 * 4);
                    LineDebug($"Addr: {Util.HexN(addr, 8)}");

                    SetReg(rd, Gba.Mem.Read32(addr));
                }
                else if ((ins & 0b1111100000000000) == 0b1001100000000000) // LDR (4)
                {
                    LineDebug("LDR (4)");

                    uint immed8 = (uint)((ins >> 0) & 0xFF);
                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint rdVal = GetReg(rd);

                    uint addr = R13 + (immed8 * 4);

                    SetReg(rd, Gba.Mem.Read32(addr));
                }
                else if ((ins & 0b1111100000000000) == 0b0110000000000000) // STR (1)
                {
                    LineDebug("STR (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnValue = GetReg((uint)((ins >> 3) & 0b111));
                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    uint addr = rnValue + (immed5 * 4);
                    LineDebug($"Addr: {Util.HexN(addr, 8)}");

                    Gba.Mem.Write32(addr, GetReg(rd));
                }
                else if ((ins & 0b1111111000000000) == 0b0101000000000000) // STR (2)
                {
                    LineDebug("STR (2)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint rm = (uint)((ins >> 6) & 0b111);
                    uint rnVal = GetReg(rn);
                    uint rmVal = GetReg(rm);

                    uint addr = rnVal + rmVal;
                    Gba.Mem.Write32(addr, GetReg(rd));
                }
                else if ((ins & 0b1111100000000000) == 0b1001000000000000) // STR (3)
                {
                    LineDebug("STR (3)");

                    uint immed8 = (uint)((ins >> 0) & 0xFF);
                    uint rd = (uint)((ins >> 8) & 0b111);

                    uint addr = R13 + (immed8 * 4);
                    Gba.Mem.Write32(addr, GetReg(rd));
                }
                else if ((ins & 0b1111011000000000) == 0b1011010000000000) // POP & PUSH
                {
                    if (BitTest(ins, 11))
                    {
                        LineDebug("POP");

                        String regs = "";
                        uint addr = R13;

                        if (BitTest(ins, 0)) { addr += 4; }
                        if (BitTest(ins, 1)) { addr += 4; }
                        if (BitTest(ins, 2)) { addr += 4; }
                        if (BitTest(ins, 3)) { addr += 4; }
                        if (BitTest(ins, 4)) { addr += 4; }
                        if (BitTest(ins, 5)) { addr += 4; }
                        if (BitTest(ins, 6)) { addr += 4; }
                        if (BitTest(ins, 7)) { addr += 4; }
                        if (BitTest(ins, 8)) { addr += 4; }

                        if (BitTest(ins, 8)) { regs += "PC "; addr -= 4; R13 += 4; R15 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 7)) { regs += "R7 "; addr -= 4; R13 += 4; R7 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 6)) { regs += "R6 "; addr -= 4; R13 += 4; R6 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 5)) { regs += "R5 "; addr -= 4; R13 += 4; R5 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 4)) { regs += "R4 "; addr -= 4; R13 += 4; R4 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 3)) { regs += "R3 "; addr -= 4; R13 += 4; R3 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 2)) { regs += "R2 "; addr -= 4; R13 += 4; R2 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 1)) { regs += "R1 "; addr -= 4; R13 += 4; R1 = Gba.Mem.Read32(addr); }
                        if (BitTest(ins, 0)) { regs += "R0 "; addr -= 4; R13 += 4; R0 = Gba.Mem.Read32(addr); }

                        LineDebug(regs);
                    }
                    else
                    {
                        LineDebug("PUSH");

                        String regs = "";
                        uint addr = R13;

                        if (BitTest(ins, 0)) { addr -= 4; }
                        if (BitTest(ins, 1)) { addr -= 4; }
                        if (BitTest(ins, 2)) { addr -= 4; }
                        if (BitTest(ins, 3)) { addr -= 4; }
                        if (BitTest(ins, 4)) { addr -= 4; }
                        if (BitTest(ins, 5)) { addr -= 4; }
                        if (BitTest(ins, 6)) { addr -= 4; }
                        if (BitTest(ins, 7)) { addr -= 4; }
                        if (BitTest(ins, 8)) { addr -= 4; }

                        if (BitTest(ins, 0)) { regs += "R0 "; Gba.Mem.Write32(addr, R0); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 1)) { regs += "R1 "; Gba.Mem.Write32(addr, R1); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 2)) { regs += "R2 "; Gba.Mem.Write32(addr, R2); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 3)) { regs += "R3 "; Gba.Mem.Write32(addr, R3); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 4)) { regs += "R4 "; Gba.Mem.Write32(addr, R4); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 5)) { regs += "R5 "; Gba.Mem.Write32(addr, R5); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 6)) { regs += "R6 "; Gba.Mem.Write32(addr, R6); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 7)) { regs += "R7 "; Gba.Mem.Write32(addr, R7); addr += 4; R13 -= 4; }
                        if (BitTest(ins, 8)) { regs += "LR "; Gba.Mem.Write32(addr, R14); addr += 4; R13 -= 4; }

                        LineDebug(regs);
                    }
                }
                else if ((ins & 0b1111100000000000) == 0b0000000000000000) // LSL (1)
                {
                    LineDebug("LSL (1) | Logical Shift Left");

                    uint immed5 = (uint)((ins >> 6) & 0b11111);
                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rmValue = GetReg((uint)((ins >> 3) & 0b111));

                    if (immed5 == 0)
                    {
                        SetReg(rd, rmValue);
                    }
                    else
                    {
                        Carry = BitTest(rmValue, (byte)(32 - immed5));
                        SetReg(rd, LogicalShiftLeft32(rmValue, (byte)immed5));
                    }
                    Negative = BitTest(GetReg(rd), 31);
                    Zero = GetReg(rd) == 0;

                }
                else if ((ins & 0b1111111111000000) == 0b0100000010000000) // LSL (2)
                {
                    LineDebug("LSL (2) | Logical Shift Left");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdValue = GetReg((uint)((ins >> 0) & 0b111));
                    uint rsValue = GetReg((uint)((ins >> 3) & 0b111));

                    if ((rsValue & 0xFF) == 0)
                    {
                        // Do nothing
                    }
                    else if ((rsValue & 0xFF) < 32)
                    {
                        Carry = BitTest(rdValue, (byte)(32 - (rsValue & 0xFF)));
                        rdValue = LogicalShiftLeft32(rdValue, (byte)(rsValue & 0xFF));
                    }
                    else if ((rsValue & 0xFF) == 32)
                    {
                        Carry = BitTest(rdValue, 0);
                        rdValue = 0;
                    }
                    else
                    {
                        Carry = false;
                        rdValue = 0;
                    }

                    SetReg(rd, rdValue);

                    Negative = BitTest(rdValue, 31);
                    Zero = rdValue == 0;
                }
                else if ((ins & 0b1111100000000000) == 0b0010100000000000) // CMP (1)
                {
                    LineDebug("CMP (1)");

                    uint immed8 = (uint)(ins & 0xFF);
                    uint rnVal = GetReg((uint)((ins >> 8) & 0b111));

                    uint alu_out = rnVal - immed8;

                    Negative = BitTest(alu_out, 31);
                    Zero = alu_out == 0;
                    Carry = !(immed8 > rnVal);
                    Overflow = CheckOverflow(rnVal, immed8, alu_out);
                }
                else if ((ins & 0b1111111110000000) == 0b0100001010000000) // CMP (2)
                {
                    LineDebug("CMP (2)");

                    uint rnVal = GetReg((uint)((ins >> 0) & 0b111));
                    uint rmVal = GetReg((uint)((ins >> 3) & 0b111));

                    uint alu_out = rnVal - rmVal;

                    Negative = BitTest(alu_out, 31);
                    Zero = alu_out == 0;
                    Carry = !(rmVal > rnVal);
                    Overflow = CheckOverflow(rnVal, rmVal, alu_out);
                }
                else if ((ins & 0b1111111100000000) == 0b0100010100000000) // CMP (3)
                {
                    LineDebug("CMP (3)");

                    uint rn = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    rn += BitTest(ins, 7) ? BIT_3 : 0;
                    rm += BitTest(ins, 6) ? BIT_3 : 0;

                    uint rnVal = GetReg(rn);
                    uint rmVal = GetReg(rm);

                    uint alu_out = rnVal - rmVal;

                    Negative = BitTest(alu_out, 31);
                    Zero = alu_out == 0;
                    Carry = !(rmVal > rnVal);
                    Overflow = CheckOverflow(rnVal, rmVal, alu_out);
                }
                else if ((ins & 0b1111100000000000) == 0b0000100000000000) // LSR (1)
                {
                    LineDebug("LSR (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    uint rmVal = GetReg(rm);

                    if (immed5 == 0)
                    {
                        Carry = BitTest(rmVal, 31);
                        SetReg(rd, 0);
                    }
                    else
                    {
                        Carry = BitTest(rmVal, (byte)(immed5 - 1));
                        SetReg(rd, LogicalShiftRight32(rmVal, (byte)immed5));
                    }
                }
                else if ((ins & 0b1111111111000000) == 0b0100000011000000) // LSR (2)
                {
                    LineDebug("LSR (2)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rs = (uint)((ins >> 3) & 0b111);

                    uint rdVal = GetReg(rd);
                    uint rsVal = GetReg(rs);

                    if ((rsVal & 0xFF) == 0)
                    {
                        // everything unaffected
                    }
                    else if ((rsVal & 0xFF) < 32)
                    {
                        Carry = BitTest(rdVal, (byte)((rsVal & 0xFF) - 1));
                        SetReg(rd, LogicalShiftRight32(rdVal, (byte)(rsVal & 0xFF)));
                    }
                    else if ((rsVal & 0xFF) == 32)
                    {
                        Carry = BitTest(rdVal, 31);
                    }
                    else
                    {
                        Carry = false;
                        SetReg(rd, 0);
                    }

                    rdVal = GetReg(rd);

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111000000000000) == 0b1101000000000000) // B
                {
                    LineDebug("B | Conditional Branch");
                    uint cond = (uint)((ins >> 8) & 0xF);
                    bool condition = CheckCondition(cond);

                    if (condition)
                    {
                        // B
                        int offset = (int)(ins & 0xFF) << 1;
                        // Signed with Two's Complement
                        if ((offset & BIT_8) != 0)
                        {
                            LineDebug("Taken, Backward Branch");
                            offset -= (int)BIT_9;
                        }
                        else
                        {
                            LineDebug("Taken, Forward Branch");
                        }

                        R15 = (uint)(R15 + offset);
                        FlushPipeline();
                    }
                    else
                    {
                        LineDebug("Not Taken");
                    }
                }
                else if ((ins & 0b1111100000000000) == 0b0010000000000000) // MOV
                {
                    LineDebug("MOV | Move large immediate to register");

                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint immed8 = ins & 0xFFu;

                    SetReg(rd, immed8);

                    Negative = false;
                    Zero = immed8 == 0;
                }
                else if ((ins & 0b1110000000000000) == 0b1110000000000000) // BL, BLX
                {
                    LineDebug("BL, BLX | Branch With Link (And Exchange)");

                    uint H = (uint)((ins >> 11) & 0b11);
                    int offset11 = ins & 0b11111111111;

                    switch (H)
                    {
                        case 0b10:
                            {
                                offset11 <<= 12;

                                // Sign extend
                                if ((offset11 & BIT_22) != 0)
                                {
                                    LineDebug("Negative");
                                    offset11 -= (int)BIT_23;
                                }
                                else
                                {
                                    LineDebug("Positive");
                                }

                                LineDebug($"offset11: {offset11}");
                                R14 = (uint)(R15 + offset11);
                                LineDebug("Upper fill");
                            }
                            break;
                        case 0b11:
                            {
                                uint oldR14 = R14;
                                R14 = (R15 - 2) | 1;
                                R15 = (uint)(oldR14 + (offset11 << 1));
                                R15 &= 0xFFFFFFFE;
                                FlushPipeline();
                                LineDebug($"Jump to ${Util.HexN(R15, 8)}");
                                LineDebug("Stay in THUMB state");
                            }
                            break;
                        case 0b01:
                            {
                                uint oldR14 = R14;
                                R14 = (R15 - 2) | 1;
                                R15 = (uint)((oldR14 + (offset11 << 1)) & 0xFFFFFFFC);
                                R15 &= 0xFFFFFFFE;
                                FlushPipeline();
                                ThumbState = false;
                                LineDebug($"Jump to ${Util.HexN(R15, 8)}");
                                LineDebug("Exit THUMB state");
                            }
                            break;
                    }
                }
                else if ((ins & 0b1111111111000000) == 0b0100000101000000) // ADC
                {
                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    uint rdVal = GetReg(rd);
                    uint rmVal = GetReg(rm);

                    uint final = rdVal + rmVal + (Carry ? 1U : 0);
                    SetReg(rd, final);

                    Negative = BitTest(final, 31);
                    Zero = rdVal == 0;
                    Carry = (long)rdVal + (long)rmVal + (Carry ? 1U : 0) > 0xFFFFFFFF;
                    Overflow = CheckOverflow(rd, rm + (Carry ? 1U : 0), final);
                }
                else if ((ins & 0b1111111000000000) == 0b0001110000000000) // ADD (1)
                {
                    LineDebug("ADD (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnVal = GetReg((uint)((ins >> 3) & 0b111));
                    uint immed3 = (uint)((ins >> 6) & 0b111);

                    uint final = rnVal + immed3;

                    SetReg(rd, final);
                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = (long)rnVal + (long)immed3 > 0xFFFFFFFF;
                    Overflow = CheckOverflow(rnVal, immed3, final);
                }
                else if ((ins & 0b1111100000000000) == 0b0011000000000000) // ADD (2)
                {
                    LineDebug("ADD (2)");

                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint immed8 = (uint)((ins >> 0) & 0xFF);

                    uint final = rdVal + immed8;

                    SetReg(rd, final);
                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = (long)rdVal + (long)immed8 > 0xFFFFFFFF;
                    Overflow = CheckOverflow(rdVal, immed8, final);
                }
                else if ((ins & 0b1111111000000000) == 0b0001100000000000) // ADD (3)
                {
                    LineDebug("ADD (3)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnVal = GetReg((uint)((ins >> 3) & 0b111));
                    uint rmVal = GetReg((uint)((ins >> 6) & 0b111));
                    uint final = rnVal + rmVal;

                    SetReg(rd, final);
                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = (long)rnVal + (long)rmVal > 0xFFFFFFFF;
                    Overflow = CheckOverflow(rmVal, rnVal, final);
                }
                else if ((ins & 0b1111111100000000) == 0b0100010000000000) // ADD (4)
                {
                    LineDebug("ADD (4)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    rd += BitTest(ins, 7) ? BIT_3 : 0;
                    rm += BitTest(ins, 6) ? BIT_3 : 0;
                    uint rdVal = GetReg(rd);
                    uint rmVal = GetReg(rm);

                    uint final = rdVal + rmVal;
                    SetReg(rd, final);
                }
                else if ((ins & 0b1111100000000000) == 0b1010000000000000) // ADD (5)
                {
                    LineDebug("ADD (5)");

                    uint immed8 = (uint)(ins & 0xFF);
                    uint rd = (uint)((ins >> 8) & 0b111);

                    SetReg(rd, (R15 & 0xFFFFFFFC) + (immed8 * 4));
                }
                else if ((ins & 0b1111100000000000) == 0b1010100000000000) // ADD (6)
                {
                    LineDebug("ADD (6)");

                    uint immed8 = (uint)(ins & 0xFF);
                    uint rd = (uint)((ins >> 8) & 0b111);

                    SetReg(rd, R13 + (immed8 << 2));
                }
                else if ((ins & 0b1111111110000000) == 0b1011000000000000) // ADD (7)
                {
                    LineDebug("ADD (7)");
                    uint immed7 = (uint)(ins & 0b1111111);
                    R13 = R13 + (immed7 << 2);
                }
                else if ((ins & 0b1111111111000000) == 0b0100001000000000) // TST
                {
                    LineDebug("TST");

                    uint rn = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    uint rnValue = GetReg(rn);
                    uint rmValue = GetReg(rm);

                    uint final = rnValue & rmValue;

                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                }
                else if ((ins & 0b1111111111000000) == 0b0100001110000000) // BIC
                {
                    LineDebug("BIC");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    uint rdValue = GetReg(rd);
                    uint rmValue = GetReg(rm);

                    uint final = rdValue & (~rmValue);
                    SetReg(rd, final);

                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                }
                else if ((ins & 0b1111000000000000) == 0b1100000000000000) // LDMIA, STMIA
                {
                    if (BitTest(ins, 11))
                    {
                        LineDebug("LDMIA | Load Multiple Increment After");

                        uint rn = (uint)((ins >> 8) & 0b111);
                        uint addr = GetReg(rn);

                        String regs = "";

                        if (BitTest(ins, 0)) { regs += "R0 "; R0 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 1)) { regs += "R1 "; R1 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 2)) { regs += "R2 "; R2 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 3)) { regs += "R3 "; R3 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 4)) { regs += "R4 "; R4 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 5)) { regs += "R5 "; R5 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 6)) { regs += "R6 "; R6 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 7)) { regs += "R7 "; R7 = Gba.Mem.Read32(addr); addr += 4; SetReg(rn, GetReg(rn) + 4); }

                        LineDebug(regs);
                    }
                    else
                    {
                        LineDebug("STMIA | Store Multiple Increment After");

                        uint rn = (uint)((ins >> 8) & 0b111);
                        uint addr = GetReg(rn);

                        String regs = "";

                        if (BitTest(ins, 0)) { regs += "R0 "; Gba.Mem.Write32(addr, R0); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 1)) { regs += "R1 "; Gba.Mem.Write32(addr, R1); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 2)) { regs += "R2 "; Gba.Mem.Write32(addr, R2); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 3)) { regs += "R3 "; Gba.Mem.Write32(addr, R3); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 4)) { regs += "R4 "; Gba.Mem.Write32(addr, R4); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 5)) { regs += "R5 "; Gba.Mem.Write32(addr, R5); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 6)) { regs += "R6 "; Gba.Mem.Write32(addr, R6); addr += 4; SetReg(rn, GetReg(rn) + 4); }
                        if (BitTest(ins, 7)) { regs += "R7 "; Gba.Mem.Write32(addr, R7); addr += 4; SetReg(rn, GetReg(rn) + 4); }

                        LineDebug(regs);
                    }
                }
                else if ((ins & 0b1111111111000000) == 0b0100000110000000) // SBC
                {
                    LineDebug("SBC");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    uint rdVal = GetReg(rd);
                    uint rmVal = GetReg(rm);

                    uint final = rdVal - rmVal - (!Carry ? 1U : 0);
                    SetReg(rd, final);

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                    Carry = !((long)rmVal + (!Carry ? 1U : 0) > rdVal);
                    Overflow = CheckOverflow(rd, rm - (!Carry ? 1U : 0), final);
                }
                else if ((ins & 0b1111111000000000) == 0b0001111000000000) // SUB (1)
                {
                    LineDebug("SUB (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint immed3 = (uint)((ins >> 6) & 0b111);

                    uint rdVal = GetReg(rd);
                    uint rnVal = GetReg(rn);

                    uint final = rnVal - immed3;
                    SetReg(rd, final);

                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = !(immed3 > rnVal);
                    Overflow = CheckOverflow(rnVal, immed3, final);
                }
                else if ((ins & 0b1111100000000000) == 0b0011100000000000) // SUB (2)
                {
                    LineDebug("SUB (2)");

                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint immed8 = (uint)((ins >> 0) & 0xFF);

                    uint rdVal = GetReg(rd);

                    uint final = rdVal - immed8;
                    SetReg(rd, final);

                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = !(immed8 > rdVal);
                    Overflow = CheckOverflow(rdVal, immed8, final);
                }
                else if ((ins & 0b1111111000000000) == 0b0001101000000000) // SUB (3)
                {
                    LineDebug("SUB (3)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnValue = GetReg((uint)((ins >> 3) & 0b111));
                    uint rmValue = GetReg((uint)((ins >> 6) & 0b111));

                    uint final = rnValue - rmValue;
                    SetReg(rd, final);

                    Negative = BitTest(final, 31);
                    Zero = final == 0;
                    Carry = !(rmValue > rnValue);
                    Overflow = CheckOverflow(rnValue, rmValue, final);
                }
                else if ((ins & 0b1111111110000000) == 0b1011000010000000) // SUB (4)
                {
                    LineDebug("SUB (4)");

                    uint immed7 = (uint)(ins & 0b1111111);
                    R13 = R13 - (immed7 << 2);
                }
                else if ((ins & 0b1111100000000000) == 0b0001000000000000) // ASR (1)
                {
                    LineDebug("ASR (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rmValue = GetReg((uint)((ins >> 3) & 0b111));
                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    if (immed5 == 0)
                    {
                        Carry = BitTest(rmValue, 31);
                        if (BitTest(rmValue, 31))
                        {
                            SetReg(rd, 0xFFFFFFFF);
                        }
                        else
                        {
                            SetReg(rd, 0);
                        }
                    }
                    else
                    {
                        Carry = BitTest(rmValue, (byte)(immed5 - 1));
                        SetReg(rd, ArithmeticShiftRight32(rmValue, (byte)immed5));
                    }

                    Negative = BitTest(GetReg(rd), 31);
                    Zero = GetReg(rd) == 0;
                }
                else if ((ins & 0b1111111111000000) == 0b0100000100000000) // ASR (2)
                {
                    LineDebug("ASR (2)");


                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rs = (uint)((ins >> 3) & 0b111);
                    uint rsVal = GetReg(rs);

                    if ((rsVal & 0xFF) == 0)
                    {
                        // Do nothing
                    }
                    else if ((rsVal & 0xFF) < 32)
                    {
                        Carry = BitTest(rdVal, (byte)((rsVal & 0xFF) - 1));
                        rdVal = ArithmeticShiftRight32(rdVal, (byte)(rsVal & 0xFF));
                    }
                    else
                    {
                        Carry = BitTest(rdVal, 31);
                        if (!Carry)
                        {
                            rdVal = 0;
                        }
                        else
                        {
                            rdVal = 0xFFFFFFFF;
                        }
                    }

                    SetReg(rd, rdVal);

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111100000000000) == 0b0010000000000000) // MOV (1)
                {
                    LineDebug("MOV (1)");

                    uint rd = (uint)((ins >> 8) & 0b111);
                    uint immed8 = (uint)(ins & 0xFF);

                    SetReg(rd, immed8);
                    Negative = false;
                    Zero = immed8 == 0;
                }
                else if ((ins & 0b1111111000000000) == 0b0001110000000000) // MOV (2)
                {
                    LineDebug("MOV (2)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rnVal = GetReg((uint)((ins >> 3) & 0b111));

                    SetReg(rd, rnVal);
                    Negative = BitTest(rnVal, 31);
                    Zero = rnVal == 0;
                    Carry = false;
                    Overflow = false;

                }
                else if ((ins & 0b1111111100000000) == 0b0100011000000000) // MOV (3)
                {
                    LineDebug("MOV (3)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    rd += BitTest(ins, 7) ? BIT_3 : 0;
                    rm += BitTest(ins, 6) ? BIT_3 : 0;

                    SetReg(rd, GetReg(rm));
                }
                else if ((ins & 0b1111111111000000) == 0b0100001111000000) // MVN
                {
                    LineDebug("MVN");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    SetReg(rd, ~GetReg(rm));
                    Negative = BitTest(GetReg(rd), 31);
                    Zero = GetReg(rd) == 0;
                }
                else if ((ins & 0b1111111111000000) == 0b0100001100000000) // ORR
                {
                    LineDebug("ORR");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rm = (uint)((ins >> 3) & 0b111);

                    SetReg(rd, GetReg(rd) | GetReg(rm));
                    Negative = BitTest(GetReg(rd), 31);
                    Zero = GetReg(rd) == 0;
                }
                else if ((ins & 0b1111111110000000) == 0b0100011100000000) // BX
                {
                    LineDebug("BX | Optionally switch back to ARM state");

                    uint rm = (uint)((ins >> 3) & 0xF); // High bit is technically an H bit, but can be ignored here
                    uint val = GetReg(rm);
                    LineDebug($"R{rm}");

                    ThumbState = BitTest(val, 0);
                    R15 = val & 0xFFFFFFFE;
                    FlushPipeline();
                }
                else if ((ins & 0b1111111111000000) == 0b0100000111000000) // ROR
                {
                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rs = (uint)((ins >> 3) & 0b111);
                    uint rsVal = GetReg(rs);

                    if ((rsVal & 0xFF) == 0)
                    {
                        // Do nothing
                    }
                    else if ((rsVal & 0b11111) == 0)
                    {
                        Carry = BitTest(rdVal, 31);
                    }
                    else
                    {
                        Carry = BitTest(rdVal, (byte)((rsVal & 0b11111) - 1));
                        rdVal = RotateRight32(rdVal, (byte)(rsVal & 0b11111));
                        SetReg(rd, rdVal);
                    }

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111000000000000) == 0b1000000000000000) // STRH (1) / LDRH (1)
                {
                    bool load = BitTest(ins, 11);

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint rnVal = GetReg(rn);

                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    uint addr = rnVal + (immed5 * 2);

                    if (load)
                    {
                        SetReg(rd, Gba.Mem.Read16(addr));
                    }
                    else
                    {
                        Gba.Mem.Write16(addr, (ushort)GetReg(rd));
                    }
                }
                else if ((ins & 0b1111111111000000) == 0b0100000000000000) // AND
                {
                    LineDebug("AND");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    uint rmVal = GetReg(rm);

                    rdVal = rdVal & rmVal;
                    SetReg(rd, rdVal);

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111111111000000) == 0b0100000001000000) // AND
                {
                    LineDebug("AND");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    uint rmVal = GetReg(rm);

                    rdVal = rdVal | rmVal;
                    SetReg(rd, rdVal);

                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111111111000000) == 0b0100001101000000) // MUL
                {
                    LineDebug("MUL");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rm = (uint)((ins >> 3) & 0b111);
                    uint rmVal = GetReg(rm);

                    rdVal = (rmVal * rdVal);
                    SetReg(rd, rdVal);
                    Negative = BitTest(rdVal, 31);
                    Zero = rdVal == 0;
                }
                else if ((ins & 0b1111000000000000) == 0b0111000000000000) // LDRB / STRB (1)
                {
                    LineDebug("STRB (1)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint rnVal = GetReg(rn);
                    uint immed5 = (uint)((ins >> 6) & 0b11111);

                    uint addr = rnVal + immed5;

                    bool load = BitTest(ins, 11);

                    if (load)
                    {
                        SetReg(rd, Gba.Mem.Read8(addr));
                    }
                    else
                    {
                        Gba.Mem.Write8(addr, (byte)rdVal);
                    }
                }
                else if ((ins & 0b1111011000000000) == 0b0101010000000000) // LDRB / STRB (2)
                {
                    LineDebug("STRB (2)");

                    uint rd = (uint)((ins >> 0) & 0b111);
                    uint rdVal = GetReg(rd);
                    uint rn = (uint)((ins >> 3) & 0b111);
                    uint rnVal = GetReg(rn);
                    uint rm = (uint)((ins >> 6) & 0b111);
                    uint rmVal = GetReg(rm);

                    uint addr = rnVal + rmVal;

                    bool load = BitTest(ins, 11);

                    if (load)
                    {
                        SetReg(rd, Gba.Mem.Read8(addr));
                    }
                    else
                    {
                        Gba.Mem.Write8(addr, (byte)rdVal);
                    }
                }
                else
                {
                    Error("Unknown THUMB instruction");
                }
            }
        }

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
                    Error($"Invalid GetReg: {reg}");
                    return 0;
            }

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                    Error($"Invalid SetReg: {reg}");
                    return;
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
                case 0x0: return R0;
                case 0x1: return R1;
                case 0x2: return R2;
                case 0x3: return R3;
                case 0x4: return R4;
                case 0x5: return R5;
                case 0x6: return R6;
                case 0x7: return R7;
                case 0x8: return R8usr;
                case 0x9: return R9usr;
                case 0xA: return R10usr;
                case 0xB: return R11usr;
                case 0xC: return R12usr;
                case 0xD: return R13usr;
                case 0xE: return R14usr;
                case 0xF: return R15;

                default:
                    Error($"Invalid GetReg: {reg}");
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
                case 0x0: R0 = val; break;
                case 0x1: R1 = val; break;
                case 0x2: R2 = val; break;
                case 0x3: R3 = val; break;
                case 0x4: R4 = val; break;
                case 0x5: R5 = val; break;
                case 0x6: R6 = val; break;
                case 0x7: R7 = val; break;
                case 0x8: R8usr = val; break;
                case 0x9: R9usr = val; break;
                case 0xA: R10usr = val; break;
                case 0xB: R11usr = val; break;
                case 0xC: R12usr = val; break;
                case 0xD: R13usr = val; break;
                case 0xE: R14usr = val; break;
                case 0xF: R15 = val; break;

                default:
                    Error($"Invalid SetReg: {reg}");
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
            uint logical = n >> bits;
            uint mask = BitTest(n, 31) ? 0xFFFFFFFF : 0;

            return logical | (mask << (32 - bits));
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
            // ThumbState = BitTest(val, 5);

            SetMode(val & 0b11111);
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
            return 0;
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

            Error("No SPSR in this mode!");
        }

        public void SetMode(uint mode)
        {
            // Store registers based on current mode
            switch (Mode)
            {
                case ARM7Mode.User:
                case ARM7Mode.OldUser:
                case ARM7Mode.System:
                    R8usr = R8;
                    R9usr = R9;
                    R10usr = R10;
                    R11usr = R11;
                    R12usr = R12;
                    R13usr = R13;
                    R14usr = R14;
                    LineDebug("Saved Registers: User / System");
                    break;

                case ARM7Mode.FIQ:
                case ARM7Mode.OldFIQ:
                    R8fiq = R8;
                    R9fiq = R9;
                    R10fiq = R10;
                    R11fiq = R11;
                    R12fiq = R12;
                    R13fiq = R13;
                    R14fiq = R14;
                    LineDebug("Saved Registers: FIQ");
                    break;

                case ARM7Mode.Supervisor:
                case ARM7Mode.OldSupervisor:
                    R13svc = R13;
                    R14svc = R14;
                    LineDebug("Saved Registers: Supervisor");
                    break;

                case ARM7Mode.Abort:
                    R13abt = R13;
                    R14abt = R14;
                    LineDebug("Saved Registers: Abort");
                    break;

                case ARM7Mode.IRQ:
                case ARM7Mode.OldIRQ:
                    R13irq = R13;
                    R14irq = R14;
                    LineDebug("Saved Registers: IRQ");
                    break;

                case ARM7Mode.Undefined:
                    R13und = R13;
                    R14und = R14;
                    LineDebug("Saved Registers: Undefined");
                    break;
            }

            switch (mode)
            {
                case 0x00:
                    Mode = ARM7Mode.OldUser;
                    R8usr = R8;
                    R9usr = R9;
                    R10usr = R10;
                    R11usr = R11;
                    R12usr = R12;
                    R13usr = R13;
                    R14usr = R14;
                    LineDebug($"Mode Switch: OldUser");
                    break;
                case 0x01:
                    Mode = ARM7Mode.OldFIQ;
                    R8 = R8fiq;
                    R9 = R9fiq;
                    R10 = R10fiq;
                    R11 = R11fiq;
                    R12 = R12fiq;
                    R13 = R13fiq;
                    R14 = R14fiq;
                    LineDebug($"Mode Switch: OldFIQ");
                    break;
                case 0x02:
                    Mode = ARM7Mode.OldIRQ;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13irq;
                    R14 = R14irq;
                    LineDebug($"Mode Switch: OldIRQ");
                    break;
                case 0x03:
                    Mode = ARM7Mode.OldSupervisor;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13svc;
                    R14 = R14svc;
                    LineDebug($"Mode Switch: OldSupervisor");
                    break;

                case 0x10:
                    Mode = ARM7Mode.User;
                    R8usr = R8;
                    R9usr = R9;
                    R10usr = R10;
                    R11usr = R11;
                    R12usr = R12;
                    R13usr = R13;
                    R14usr = R14;
                    LineDebug($"Mode Switch: User");
                    break;
                case 0x11:
                    Mode = ARM7Mode.FIQ;
                    R8 = R8fiq;
                    R9 = R9fiq;
                    R10 = R10fiq;
                    R11 = R11fiq;
                    R12 = R12fiq;
                    R13 = R13fiq;
                    R14 = R14fiq;
                    LineDebug($"Mode Switch: FIQ");
                    break;
                case 0x12:
                    Mode = ARM7Mode.IRQ;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13irq;
                    R14 = R14irq;
                    LineDebug($"Mode Switch: IRQ");
                    break;
                case 0x13:
                    Mode = ARM7Mode.Supervisor;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13svc;
                    R14 = R14svc;
                    LineDebug($"Mode Switch: Supervisor");
                    break;
                case 0x17:
                    Mode = ARM7Mode.Abort;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13abt;
                    R14 = R14abt;
                    LineDebug($"Mode Switch: Abort");
                    break;
                case 0x1B:
                    Mode = ARM7Mode.Undefined;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13und;
                    R14 = R14und;
                    LineDebug($"Mode Switch: Undefined");
                    break;
                case 0x1F:
                    Mode = ARM7Mode.System;
                    R8 = R8usr;
                    R9 = R9usr;
                    R10 = R10usr;
                    R11 = R11usr;
                    R12 = R12usr;
                    R13 = R13usr;
                    R14 = R14usr;
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

        public void ResetDebug()
        {
            Debug = "";
        }

        public void LineDebug(String s)
        {
            Debug += $"{s}\n";
        }

        public void Error(String s)
        {
            LineDebug("ERROR:");
            LineDebug(s);

            Errored = true;
        }

        public static bool MsbDiff(uint val1, uint val2)
        {
            return BitTest(val1, 31) != BitTest(val2, 31);
        }

        public static bool CheckOverflow(uint val1, uint val2, uint result)
        {
            return ((val1 ^ val2) & 0x80000000u) != 0 && ((val1 ^ result) & 0x80000000u) != 0;
        }
    }
}
