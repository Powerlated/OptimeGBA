using static OptimeGBA.Bits;
using static Util;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace OptimeGBA
{
    public delegate void ArmExecutor(Arm7 arm7, uint ins);

    public unsafe static class Arm
    {
        public static void SWI(Arm7 arm7, uint ins)
        {
            arm7.SPSR_svc = arm7.GetCPSR();
            arm7.SetMode(Arm7Mode.SVC); // Go into SVC / Supervisor mode
            arm7.R[14] = arm7.R[15] - 4;
            // arm7.ThumbState = false; // Back to ARM state
            arm7.IRQDisable = true;

            arm7.R[15] = arm7.VectorSoftwareInterrupt;
            arm7.FlushPipeline();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _LDMSTM(Arm7 arm7, uint ins, bool L)
        {
            arm7.LineDebug("LDM/STM");

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool S = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            bool loadsPc = BitTest(ins, 15);

            uint oldMode = 0;
            if (S && (!L || !loadsPc))
            {
                oldMode = arm7.GetMode();
                arm7.SetMode(Arm7Mode.USR);
            }

            // if (U && P && W) Error("U & P & W");

            arm7.LineDebug(L ? "Load" : "Store");
            arm7.LineDebug(P ? "No Include Base" : "Include Base");
            arm7.LineDebug(U ? "Upwards" : "Downwards");

            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];

            // String regs = "";

            uint bitsSet = (uint)BitOperations.PopCount(ins & 0xFFFF);
            uint writebackValue;
            if (U)
            {
                if (W)
                {
                    writebackValue = addr + bitsSet * 4;
                }
                else
                {
                    writebackValue = addr;
                }
            }
            else
            {
                if (W)
                {
                    writebackValue = addr - bitsSet * 4;
                }
                else
                {
                    writebackValue = addr;
                }
                if (P)
                {
                    addr = addr - bitsSet * 4 - 4;
                }
                else
                {
                    addr = addr - bitsSet * 4 + 4;
                }
            }

            if (L)
            {
                if (W)
                {
                    arm7.R[rn] = writebackValue;
                }
            }
            else
            {
                arm7.R[15] += 4;
            }

            for (byte r = 0; r < 16; r++)
            {
                if (BitTest(ins, r))
                {
                    if (L)
                    {
                        if (P) addr += 4;

                        if (r != 15)
                        {
                            arm7.R[r] = arm7.Read32(addr & ~3U);
                        }
                        else
                        {
                            arm7.R[15] = arm7.Read32(addr & ~3U) & ~3U;
                            arm7.FlushPipeline();
                        }

                        if (!P) addr += 4;
                    }
                    else
                    {

                        if (P) addr += 4;

                        arm7.Write32(addr & ~3U, arm7.R[r]);

                        if (!P) addr += 4;

                        arm7.R[rn] = writebackValue;
                    }
                }
            }

            bool emptyRlist = (ins & 0xFFFF) == 0;
            if (emptyRlist)
            {
                if (L)
                {
                    arm7.R[15] = arm7.Read32(addr & ~3U);
                    arm7.FlushPipeline();
                    if (U)
                    {
                        arm7.R[rn] += 0x40;
                    }
                    else
                    {
                        arm7.R[rn] -= 0x40;
                    }
                }
                else
                {
                    arm7.LineDebug("Empty Rlist!");
                    if (P)
                    {
                        if (U)
                        {
                            arm7.Write32(arm7.R[rn] + 4, arm7.R[15]);
                            arm7.R[rn] += 0x40;
                        }
                        else
                        {
                            arm7.R[rn] -= 0x40;
                            arm7.Write32(arm7.R[rn], arm7.R[15]);
                        }
                    }
                    else
                    {
                        if (U)
                        {
                            arm7.Write32(arm7.R[rn], arm7.R[15]);
                            arm7.R[rn] += 0x40;
                        }
                        else
                        {
                            arm7.R[rn] -= 0x40;
                            arm7.Write32(arm7.R[rn] + 4, arm7.R[15]);
                        }
                    }
                }
            }

            if (!L)
            {
                arm7.R[15] -= 4;
            }

            if (S)
            {
                if (L && loadsPc)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                }
                else
                {
                    arm7.SetMode((Arm7Mode)oldMode);
                }
            }

            // arm7.LineDebug(regs);

            arm7.ICycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _LDMSTM_V5(Arm7 arm7, uint ins, bool L)
        {
            arm7.LineDebug("LDM/STM ARMv5");

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool S = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            bool loadsPc = BitTest(ins, 15);

            uint oldMode = 0;
            if (S && (!L || !loadsPc))
            {
                oldMode = arm7.GetMode();
                arm7.SetMode(Arm7Mode.USR);
            }

            // if (U && P && W) Error("U & P & W");

            arm7.LineDebug(L ? "Load" : "Store");
            arm7.LineDebug(P ? "No Include Base" : "Include Base");
            arm7.LineDebug(U ? "Upwards" : "Downwards");

            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];

            // String regs = "";

            uint bitsSet = (uint)BitOperations.PopCount(ins & 0xFFFF);
            uint writebackValue;
            if (U)
            {
                if (W)
                {
                    writebackValue = addr + bitsSet * 4;
                }
                else
                {
                    writebackValue = addr;
                }
            }
            else
            {
                if (W)
                {
                    writebackValue = addr - bitsSet * 4;
                }
                else
                {
                    writebackValue = addr;
                }
                if (P)
                {
                    addr = addr - bitsSet * 4 - 4;
                }
                else
                {
                    addr = addr - bitsSet * 4 + 4;
                }
            }

            if (!L)
            {
                arm7.R[15] += 4;
            }

            for (byte r = 0; r < 16; r++)
            {
                if (BitTest(ins, r))
                {
                    if (L)
                    {
                        if (W)
                        {
                            arm7.R[rn] = writebackValue;
                        }

                        if (P) addr += 4;

                        if (r != 15)
                        {
                            arm7.R[r] = arm7.Read32(addr & ~3U);
                        }
                        else
                        {
                            arm7.R[15] = arm7.Read32(addr & ~3U);
                            arm7.ThumbState = BitTest(arm7.R[15], 0);
                            arm7.FlushPipeline();
                        }

                        if (!P) addr += 4;
                    }
                    else
                    {

                        if (P) addr += 4;

                        arm7.Write32(addr & ~3U, arm7.R[r]);

                        if (!P) addr += 4;
                    }
                }
            }

            if (!L)
            {
                arm7.R[15] -= 4;
            }

            // ARMv5: When Rn is in Rlist, writeback happens if Rn is the only register, or not the last
            // I can't figure out the order of operations so I'll just hack the only register case 
            if (!L || bitsSet == 1)
            {
                arm7.R[rn] = writebackValue;
            }

            bool emptyRlist = (ins & 0xFFFF) == 0;
            if (emptyRlist)
            {
                if (U)
                {
                    arm7.R[rn] += 0x40;
                }
                else
                {
                    arm7.R[rn] -= 0x40;
                }
            }

            if (S)
            {
                if (L && loadsPc)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                }
                else
                {
                    arm7.SetMode((Arm7Mode)oldMode);
                }
            }

            // arm7.LineDebug(regs);

            arm7.ICycle();
        }

        public static void LDM(Arm7 arm7, uint ins) { _LDMSTM(arm7, ins, true); }
        public static void STM(Arm7 arm7, uint ins) { _LDMSTM(arm7, ins, false); }
        public static void LDM_V5(Arm7 arm7, uint ins) { _LDMSTM_V5(arm7, ins, true); }
        public static void STM_V5(Arm7 arm7, uint ins) { _LDMSTM_V5(arm7, ins, false); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _B(Arm7 arm7, uint ins, bool link)
        {
            arm7.LineDebug("B | Branch");
            // B
            int offset = (int)(ins & 0b111111111111111111111111) << 2;
            // Signed with Two's Complement
            // Cheap and easy sign-extend
            offset = (offset << 6) >> 6;

            // BL - store return address in R14
            if (link)
            {
                arm7.R[14] = arm7.R[15] - 4;
            }

            // BLX immediate
            if ((ins >> 28) == 0b1111)
            {
                int halfwordOffset = (int)(ins >> 24) & 0b1;
                arm7.ThumbState = true;
                offset += halfwordOffset * 2;

                arm7.R[14] = arm7.R[15] - 4;
            }

            arm7.R[15] = (uint)(arm7.R[15] + offset);
            arm7.FlushPipeline();
        }

        public static void B(Arm7 arm7, uint ins) { _B(arm7, ins, false); }
        public static void BL(Arm7 arm7, uint ins) { _B(arm7, ins, true); }

        public static void BX(Arm7 arm7, uint ins)
        {
            // BX - branch and optional switch to Thumb state
            arm7.LineDebug("BX");

            uint rm = ins & 0xF;
            uint rmValue = arm7.R[rm];

            arm7.ThumbState = BitTest(rmValue, 0);
            if (arm7.ThumbState)
            {
                arm7.StateChange();
                arm7.LineDebug("Switch to THUMB State");
            }
            else
            {
                arm7.LineDebug("Switch to ARM State");
            }

            // BLX register
            uint opcode = (ins >> 4) & 0xF;
            if (opcode == 0b0011)
            {
                arm7.R[14] = arm7.R[15] - 4;
            }

            arm7.R[15] = rmValue & ~1U;
            arm7.FlushPipeline();
        }

        public static void SWP(Arm7 arm7, uint ins)
        {
            uint rm = (ins >> 0) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];
            uint storeValue = arm7.R[rm];

            arm7.LineDebug("SWP");
            uint readVal = RotateRight32(arm7.Read32(addr & ~3u), (byte)((addr & 3u) * 8));
            arm7.Write32(addr & ~3u, storeValue);
            arm7.R[rd] = readVal;

            arm7.ICycle();
        }

        public static void SWPB(Arm7 arm7, uint ins)
        {
            uint rm = (ins >> 0) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];
            uint storeValue = arm7.R[rm];

            arm7.LineDebug("SWPB");
            byte readVal = arm7.Read8(addr);
            arm7.Write8(addr, (byte)storeValue);
            arm7.R[rd] = readVal;

            arm7.ICycle();
        }

        public static void MSR(Arm7 arm7, uint ins)
        {
            arm7.LineDebug("MSR");
            // MSR

            bool useSPSR = BitTest(ins, 22);

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
                operand = arm7.R[ins & 0xF];
            }

            uint byteMask =
                (setControl ? 0x000000FFu : 0) |
                (setExtension ? 0x0000FF00u : 0) |
                (setStatus ? 0x00FF0000u : 0) |
                (setFlags ? 0xFF000000u : 0);

            arm7.LineDebug($"Set Control: {setControl}");
            arm7.LineDebug($"Set Extension: {setExtension}");
            arm7.LineDebug($"Set Status: {setStatus}");
            arm7.LineDebug($"Set Flags: {setFlags}");

            if (!useSPSR)
            {
                // TODO: Fix privileged mode functionality in CPSR MSR
                if (arm7.Mode == Arm7Mode.USR)
                {
                    // Privileged
                    arm7.LineDebug("Privileged");
                    byteMask &= 0xFF000000;
                }
                arm7.SetCPSR((arm7.GetCPSR() & ~byteMask) | (operand & byteMask));
            }
            else
            {
                // TODO: Add SPSR functionality to MSR
                arm7.SetSPSR((arm7.GetSPSR() & ~byteMask) | (operand & byteMask));
            }
        }

        public static void MRS(Arm7 arm7, uint ins)
        {
            arm7.LineDebug("MRS");

            bool useSPSR = BitTest(ins, 22);

            uint rd = (ins >> 12) & 0xF;

            if (useSPSR)
            {
                arm7.LineDebug("Rd from SPSR");
                arm7.R[rd] = arm7.GetSPSR();
            }
            else
            {
                arm7.LineDebug("Rd from CPSR");
                arm7.R[rd] = arm7.GetCPSR();
            }
        }

        public static void MUL(Arm7 arm7, uint ins)
        {
            uint rd = (ins >> 16) & 0xF;
            uint rs = (ins >> 8) & 0xF;
            uint rm = (ins >> 0) & 0xF;
            uint rsValue = arm7.R[rs];
            uint rmValue = arm7.R[rm];

            arm7.LineDebug($"R{rm} * R{rs}");
            arm7.LineDebug($"${Util.HexN(rmValue, 8)} * ${Util.HexN(rsValue, 8)}");

            bool setFlags = BitTest(ins, 20);

            uint final;
            if (BitTest(ins, 21))
            {
                uint rnValue = arm7.R[(ins >> 12) & 0xF];
                arm7.LineDebug("Multiply Accumulate");
                final = (rsValue * rmValue) + rnValue;
            }
            else
            {
                arm7.LineDebug("Multiply Regular");
                final = rsValue * rmValue;
            }
            arm7.R[rd] = final;

            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31);
                arm7.Zero = final == 0;
            }
        }

        public static void MULL(Arm7 arm7, uint ins)
        {
            bool signed = BitTest(ins, 22);
            bool accumulate = BitTest(ins, 21);
            bool setFlags = BitTest(ins, 20);

            uint rdHi = (ins >> 16) & 0xF;
            uint rdLo = (ins >> 12) & 0xF;
            uint rs = (ins >> 8) & 0xF;
            uint rm = (ins >> 0) & 0xF;
            ulong rsVal = arm7.R[rs];
            ulong rmVal = arm7.R[rm];

            arm7.LineDebug("Multiply Long");

            ulong longLo;
            ulong longHi;
            if (accumulate)
            {
                arm7.LineDebug("Accumulate");

                if (signed)
                {
                    // SMLAL
                    long rmValExt = (long)(rmVal << 32) >> 32;
                    long rsValExt = (long)(rsVal << 32) >> 32;

                    longLo = (ulong)(((rsValExt * rmValExt) & 0xFFFFFFFF) + arm7.R[rdLo]);
                    longHi = (ulong)((rsValExt * rmValExt) >> 32) + arm7.R[rdHi] + (longLo > 0xFFFFFFFF ? 1U : 0);
                }
                else
                {
                    // UMLAL
                    longLo = ((rsVal * rmVal) & 0xFFFFFFFF) + arm7.R[rdLo];
                    longHi = ((rsVal * rmVal) >> 32) + arm7.R[rdHi] + (longLo > 0xFFFFFFFF ? 1U : 0);
                }
            }
            else
            {
                arm7.LineDebug("No Accumulate");

                if (signed)
                {
                    // SMULL
                    long rmValExt = (long)(rmVal << 32) >> 32;
                    long rsValExt = (long)(rsVal << 32) >> 32;

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

            arm7.LineDebug($"RdLo: R{rdLo}");
            arm7.LineDebug($"RdHi: R{rdHi}");
            arm7.LineDebug($"Rm: R{rm}");
            arm7.LineDebug($"Rs: R{rs}");

            arm7.R[rdLo] = (uint)longLo;
            arm7.R[rdHi] = (uint)longHi;

            if (setFlags)
            {
                arm7.Negative = BitTest((uint)longHi, 31);
                arm7.Zero = arm7.R[rdLo] == 0 && arm7.R[rdHi] == 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _RegularLDRSTR(Arm7 arm7, uint ins, bool L, bool useRegister)
        {
            // LDR (Load Register)
            arm7.LineDebug("LDR (Load Register)");

            uint rn = (ins >> 16) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rnValue = arm7.R[rn];

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool B = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            uint offset = 0;

            if (useRegister)
            {
                // Register offset
                arm7.LineDebug($"Register Offset");
                uint rmVal = arm7.R[ins & 0xF];

                if ((ins & 0b111111110000) == 0b000000000000)
                {
                    arm7.LineDebug($"Non-scaled");
                    offset = rmVal;
                }
                else
                {
                    arm7.LineDebug($"Scaled");

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
                                // if (BitTest(rmVal, 31))
                                // {
                                //     return 0xFFFFFFFF;
                                // }
                                // else
                                // {
                                //     return 0;
                                // }
                                offset = (uint)((int)rmVal >> 31);
                            }
                            else
                            {
                                offset = ArithmeticShiftRight32(rmVal, shiftBits);
                            }
                            break;
                        default:
                        case 0b11:
                            if (shiftBits == 0)
                            {
                                offset = LogicalShiftLeft32(arm7.Carry ? 1U : 0, 31) | (LogicalShiftRight32(rmVal, 1));
                            }
                            else
                            {
                                offset = RotateRight32(rmVal, shiftBits);
                            }
                            break;
                    }
                }

            }
            else
            {
                // Immediate offset
                arm7.LineDebug($"Immediate Offset");

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

            if (L)
            {
                arm7.LineDebug($"Rn: R{rn}");
                arm7.LineDebug($"Rd: R{rd}");

                uint loadVal = 0;
                if (B)
                {
                    loadVal = arm7.Read8(addr);
                }
                else
                {

                    if ((addr & 0b11) != 0)
                    {

                        // If the address isn't word-aligned
                        uint data = arm7.Read32(addr & ~3U);
                        loadVal = RotateRight32(data, (byte)(8 * (addr & 0b11)));

                        // Error("Misaligned LDR");
                    }
                    else
                    {
                        loadVal = arm7.Read32(addr);
                    }
                }

                arm7.LineDebug($"LDR Addr: {Util.Hex(addr, 8)}");
                arm7.LineDebug($"LDR Value: {Util.Hex(loadVal, 8)}");

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

                    arm7.R[rn] = addr;
                }
                else if (W)
                {
                    arm7.R[rn] = addr;
                }

                // Register loading happens after writeback, so if writeback register and Rd are the same, 
                // the writeback value would be overwritten by Rd.
                arm7.R[rd] = loadVal;

                if (rd == 15)
                {
                    if (arm7.Armv5)
                    {
                        arm7.ThumbState = BitTest(loadVal, 0);
                    }
                    arm7.FlushPipeline();
                }

                arm7.ICycle();
            }
            else
            {
                arm7.R[15] += 4;

                uint storeVal = arm7.R[rd];
                if (B)
                {
                    arm7.Write8(addr, (byte)storeVal);
                }
                else
                {
                    arm7.Write32(addr & 0xFFFFFFFC, storeVal);
                }

                arm7.LineDebug($"STR Addr: {Util.Hex(addr, 8)}");
                arm7.LineDebug($"STR Value: {Util.Hex(storeVal, 8)}");

                arm7.R[15] -= 4;

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

                    arm7.R[rn] = addr;
                }
                else if (W)
                {
                    arm7.R[rn] = addr;
                }
            }
        }

        public static void RegularLDR_Reg(Arm7 arm7, uint ins) { _RegularLDRSTR(arm7, ins, true, true); }
        public static void RegularSTR_Reg(Arm7 arm7, uint ins) { _RegularLDRSTR(arm7, ins, false, true); }
        public static void RegularLDR_Imm(Arm7 arm7, uint ins) { _RegularLDRSTR(arm7, ins, true, false); }
        public static void RegularSTR_Imm(Arm7 arm7, uint ins) { _RegularLDRSTR(arm7, ins, false, false); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _SpecialLDRSTR(Arm7 arm7, uint ins, bool L, bool S, bool H)
        {
            arm7.LineDebug("Halfword, Signed Byte, Doubleword Loads & Stores");
            arm7.LineDebug("LDR|STR H|SH|SB|D");

            bool W = BitTest(ins, 21); // Writeback to base register
            bool immediateOffset = BitTest(ins, 22);
            bool U = BitTest(ins, 23); // Add / Subtract offset
            bool P = BitTest(ins, 24); // Use post-indexed / offset or pre-indexed 

            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            uint baseAddr = arm7.R[rn];

            uint offset;
            if (immediateOffset)
            {
                arm7.LineDebug("Immediate Offset");
                uint immed = (ins & 0xF) | ((ins >> 4) & 0xF0);
                offset = immed;
            }
            else
            {
                arm7.LineDebug("Register Offset");
                uint rm = ins & 0xF;
                offset = arm7.R[rm];
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
                        arm7.LineDebug("Load signed halfword");

                        int readVal;
                        if ((addr & 1) != 0)
                        {
                            // Misaligned, read byte instead.
                            // Sign extend
                            readVal = (sbyte)arm7.Read8(addr);
                        }
                        else
                        {
                            // Sign extend
                            readVal = (short)arm7.Read16(addr);
                        }
                        loadVal = (uint)readVal;
                    }
                    else
                    {
                        arm7.LineDebug("Load signed byte");

                        int val = (sbyte)arm7.Read8(addr);

                        loadVal = (uint)val;
                    }
                }
                else
                {
                    if (H)
                    {
                        arm7.LineDebug("Load unsigned halfword");
                        // Force halfword aligned, and rotate if unaligned
                        loadVal = RotateRight32(arm7.Read16(addr & ~1u), (byte)((addr & 1) * 8));
                    }
                }
            }
            else
            {
                if (S)
                {
                    if (arm7.Armv5)
                    {
                        if (H)
                        {
                            arm7.LineDebug("Store doubleword");
                            arm7.Error($"UNIMPLEMENTED R15:{Hex(arm7.R[15], 8)} OPCODE:{Hex(ins, 8)} STRD");
                        }
                        else
                        {
                            arm7.LineDebug("Load doubleword");
                            arm7.Error($"UNIMPLEMENTED R15:{Hex(arm7.R[15], 8)} OPCODE:{Hex(ins, 8)} LDRD");
                        }
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    if (H)
                    {
                        arm7.LineDebug("Store halfword");
                        arm7.Write16(addr & ~1u, (ushort)arm7.R[rd]);
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
                arm7.R[rn] = addr;
            }

            if (L)
            {
                arm7.R[rd] = loadVal;
                arm7.ICycle();
            }

            arm7.LineDebug($"Writeback: {(W ? "Yes" : "No")}");
            arm7.LineDebug($"Offset / pre-indexed addressing: {(P ? "Yes" : "No")}");
        }

        // In order mentioned in ARM architecture reference manual for ARMv5
        public static void STRH(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, false, false, true); }
        public static void LDRD(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, false, true, false); }
        public static void STRD(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, false, true, true); }
        public static void LDRH(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, true, false, true); }
        public static void LDRSB(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, true, true, false); }
        public static void LDRSH(Arm7 arm7, uint ins) { _SpecialLDRSTR(arm7, ins, true, true, true); }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static (uint shifterOperand, bool shifterCarryOut, uint rnVal, uint rd) DataDecode(Arm7 arm7, uint ins, bool useImmediate32)
        {
            uint rd = (ins >> 12) & 0xF; // Rd, SBZ for CMP

            // ----- When using register as 2nd operand -----
            // Shift by immediate or shift by register
            uint shifterOperand = 0;
            bool shifterCarryOut = false;

            if (useImmediate32)
            {
                uint rn = (ins >> 16) & 0xF; // Rn
                                             // uint rs = (ins >> 8) & 0xF;
                                             // uint rm = ins & 0xF;
                uint rnVal = arm7.R[rn];
                // uint rsVal = R[rs];
                // uint rmVal = R[rm];

                uint rotateBits = ((ins >> 8) & 0xF) * 2;
                uint constant = ins & 0xFF;

                shifterOperand = RotateRight32(constant, (byte)rotateBits);
                if (rotateBits == 0)
                {
                    shifterCarryOut = arm7.Carry;
                }
                else
                {
                    shifterCarryOut = BitTest(shifterOperand, 31);
                }

                arm7.LineDebug($"Immediate32: {Util.Hex(shifterOperand, 8)}");

                return (shifterOperand, shifterCarryOut, rnVal, rd);
            }
            else
            {
                bool regShift = (ins & BIT_4) != 0;

                byte shiftBits;
                uint shiftType = (ins >> 5) & 0b11;

                if (!regShift)
                {
                    // Immediate Shift
                    arm7.LineDebug("Immediate Shift");
                    shiftBits = (byte)((ins >> 7) & 0b11111);

                    uint rn = (ins >> 16) & 0xF; // Rn
                                                 // uint rs = (ins >> 8) & 0xF;
                    uint rm = ins & 0xF;
                    uint rnVal = arm7.R[rn];
                    // uint rsVal = R[rs];
                    uint rmVal = arm7.R[rm];

                    switch (shiftType)
                    {
                        case 0b00: // LSL
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = arm7.Carry;
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
                                shifterOperand = (uint)((int)rmVal >> 31);
                                shifterCarryOut = BitTest(rmVal, 31);
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
                                shifterOperand = LogicalShiftLeft32(arm7.Carry ? 1U : 0, 31) | LogicalShiftRight32(rmVal, 1);
                                shifterCarryOut = BitTest(rmVal, 0);
                            }
                            else
                            {
                                shifterOperand = RotateRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            break;
                    }

                    return (shifterOperand, shifterCarryOut, rnVal, rd);
                }
                else
                {
                    // Register shift
                    arm7.LineDebug("Register Shift");

                    uint rn = (ins >> 16) & 0xF; // Rn
                    uint rs = (ins >> 8) & 0xF;
                    uint rm = ins & 0xF;
                    arm7.LineDebug("RS: " + rs);

                    arm7.ICycle();

                    arm7.R[15] += 4;
                    uint rnVal = arm7.R[rn];
                    uint rsVal = arm7.R[rs];
                    uint rmVal = arm7.R[rm];
                    arm7.R[15] -= 4;

                    shiftBits = (byte)rsVal;

                    switch (shiftType)
                    {
                        case 0b00:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = arm7.Carry;
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
                                shifterCarryOut = arm7.Carry;
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
                                shifterCarryOut = arm7.Carry;
                            }
                            else if (shiftBits < 32)
                            {
                                shifterOperand = ArithmeticShiftRight32(rmVal, shiftBits);
                                shifterCarryOut = BitTest(rmVal, (byte)(shiftBits - 1));
                            }
                            else if (shiftBits >= 32)
                            {
                                shifterOperand = (uint)((int)rmVal >> 31);
                                shifterCarryOut = BitTest(rmVal, 31);
                            }
                            break;
                        case 0b11:
                            if (shiftBits == 0)
                            {
                                shifterOperand = rmVal;
                                shifterCarryOut = arm7.Carry;
                            }
                            else
                            {
                                shifterOperand = RotateRight32(rmVal, (byte)(shiftBits & 0b11111));
                                shifterCarryOut = BitTest(rmVal, (byte)((shiftBits & 0b11111) - 1));
                            }
                            break;
                    }

                    return (shifterOperand, shifterCarryOut, rnVal, rd);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataAND(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("AND");

            uint final = rnValue & shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31);
                arm7.Zero = final == 0;
                arm7.Carry = shifterCarryOut;

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataEOR(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("EOR");

            uint final = rnValue ^ shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31);
                arm7.Zero = final == 0;
                arm7.Carry = shifterCarryOut;

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataSUB(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("SUB");

            uint aluOut = rnValue - shifterOperand;

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = shifterOperand <= rnValue; // C
                arm7.Overflow = Arm7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataRSB(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("RSB");

            uint aluOut = shifterOperand - rnValue;

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = rnValue <= shifterOperand; // C
                arm7.Overflow = Arm7.CheckOverflowSub(shifterOperand, rnValue, aluOut); // V

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataADD(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("ADD");

            uint final = rnValue + shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31); // N
                arm7.Zero = final == 0; // Z
                arm7.Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // C
                arm7.Overflow = Arm7.CheckOverflowAdd(rnValue, shifterOperand, final); // C

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataADC(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("ADC");

            uint final = rnValue + shifterOperand + (arm7.Carry ? 1U : 0);
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31); // N
                arm7.Zero = final == 0; // Z
                arm7.Carry = (long)rnValue + (long)shifterOperand + (arm7.Carry ? 1U : 0) > 0xFFFFFFFFL; // C
                arm7.Overflow = Arm7.CheckOverflowAdd(rnValue, shifterOperand, final); // V

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataSBC(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("SBC");

            uint aluOut = rnValue - shifterOperand - (!arm7.Carry ? 1U : 0U);

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = (long)shifterOperand + (arm7.Carry ? 0 : 1L) <= rnValue; // C
                arm7.Overflow = Arm7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataRSC(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("RSC");

            uint aluOut = shifterOperand - rnValue - (!arm7.Carry ? 1U : 0U);

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = (long)rnValue + (long)(!arm7.Carry ? 1U : 0) <= shifterOperand; // C
                arm7.Overflow = Arm7.CheckOverflowSub(shifterOperand, rnValue, aluOut); // V

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataTST(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("TST");

            uint final = rnValue & shifterOperand;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = shifterCarryOut;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataTEQ(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("TEQ");

            uint aluOut = rnValue ^ shifterOperand;

            arm7.Negative = BitTest(aluOut, 31); // N
            arm7.Zero = aluOut == 0; // Z
            arm7.Carry = shifterCarryOut; // C
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataCMP(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            // SBZ means should be zero, not relevant to the current code, just so you know
            arm7.LineDebug("CMP");

            uint aluOut = rnValue - shifterOperand;

            arm7.Negative = BitTest(aluOut, 31); // N
            arm7.Zero = aluOut == 0; // Z
            arm7.Carry = rnValue >= shifterOperand; // C
            arm7.Overflow = Arm7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataCMN(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("CMN");

            uint aluOut = rnValue + shifterOperand;

            arm7.Negative = BitTest(aluOut, 31); // N
            arm7.Zero = aluOut == 0; // Z
            arm7.Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFF; // C
            arm7.Overflow = Arm7.CheckOverflowAdd(rnValue, shifterOperand, aluOut); // V
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataORR(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("ORR");

            uint final = rnValue | shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31);
                arm7.Zero = final == 0;
                arm7.Carry = shifterCarryOut;

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataMOV(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("MOV");

            arm7.R[rd] /*Rd*/ = shifterOperand;
            if (setFlags)
            {
                arm7.Negative = BitTest(shifterOperand, 31); // N
                arm7.Zero = shifterOperand == 0; // Z
                arm7.Carry = shifterCarryOut; // C

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataBIC(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("BIC");

            uint final = rnValue & ~shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31); // N
                arm7.Zero = final == 0; // Z
                arm7.Carry = shifterCarryOut; // C

                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void _DataMVN(Arm7 arm7, uint ins, bool useImmediate32, bool setFlags)
        {
            (uint shifterOperand, bool shifterCarryOut, uint rnValue, uint rd) = DataDecode(arm7, ins, useImmediate32);

            arm7.LineDebug("MVN");

            arm7.R[rd] /*Rd*/ = ~shifterOperand;
            if (setFlags)
            {
                arm7.Negative = BitTest(~shifterOperand, 31); // N
                arm7.Zero = ~shifterOperand == 0; // Z
                arm7.Carry = shifterCarryOut; ; // C
                if (rd == 15)
                {
                    arm7.SetCPSR(arm7.GetSPSR());
                    arm7.FlushPipeline();
                }
            }
            else
            {
                if (rd == 15) arm7.FlushPipeline();
            }
        }

        // Reducing branches during runtime, the private functions beginning
        // with an underline are marked for inlining
        public static void DataAND_Reg(Arm7 arm7, uint ins) { _DataAND(arm7, ins, false, false); }
        public static void DataEOR_Reg(Arm7 arm7, uint ins) { _DataEOR(arm7, ins, false, false); }
        public static void DataSUB_Reg(Arm7 arm7, uint ins) { _DataSUB(arm7, ins, false, false); }
        public static void DataRSB_Reg(Arm7 arm7, uint ins) { _DataRSB(arm7, ins, false, false); }
        public static void DataADD_Reg(Arm7 arm7, uint ins) { _DataADD(arm7, ins, false, false); }
        public static void DataADC_Reg(Arm7 arm7, uint ins) { _DataADC(arm7, ins, false, false); }
        public static void DataSBC_Reg(Arm7 arm7, uint ins) { _DataSBC(arm7, ins, false, false); }
        public static void DataRSC_Reg(Arm7 arm7, uint ins) { _DataRSC(arm7, ins, false, false); }
        public static void DataTST_Reg(Arm7 arm7, uint ins) { _DataTST(arm7, ins, false, false); }
        public static void DataTEQ_Reg(Arm7 arm7, uint ins) { _DataTEQ(arm7, ins, false, false); }
        public static void DataCMP_Reg(Arm7 arm7, uint ins) { _DataCMP(arm7, ins, false, false); }
        public static void DataCMN_Reg(Arm7 arm7, uint ins) { _DataCMN(arm7, ins, false, false); }
        public static void DataORR_Reg(Arm7 arm7, uint ins) { _DataORR(arm7, ins, false, false); }
        public static void DataMOV_Reg(Arm7 arm7, uint ins) { _DataMOV(arm7, ins, false, false); }
        public static void DataBIC_Reg(Arm7 arm7, uint ins) { _DataBIC(arm7, ins, false, false); }
        public static void DataMVN_Reg(Arm7 arm7, uint ins) { _DataMVN(arm7, ins, false, false); }

        public static void DataAND_Imm(Arm7 arm7, uint ins) { _DataAND(arm7, ins, true, false); }
        public static void DataEOR_Imm(Arm7 arm7, uint ins) { _DataEOR(arm7, ins, true, false); }
        public static void DataSUB_Imm(Arm7 arm7, uint ins) { _DataSUB(arm7, ins, true, false); }
        public static void DataRSB_Imm(Arm7 arm7, uint ins) { _DataRSB(arm7, ins, true, false); }
        public static void DataADD_Imm(Arm7 arm7, uint ins) { _DataADD(arm7, ins, true, false); }
        public static void DataADC_Imm(Arm7 arm7, uint ins) { _DataADC(arm7, ins, true, false); }
        public static void DataSBC_Imm(Arm7 arm7, uint ins) { _DataSBC(arm7, ins, true, false); }
        public static void DataRSC_Imm(Arm7 arm7, uint ins) { _DataRSC(arm7, ins, true, false); }
        public static void DataTST_Imm(Arm7 arm7, uint ins) { _DataTST(arm7, ins, true, false); }
        public static void DataTEQ_Imm(Arm7 arm7, uint ins) { _DataTEQ(arm7, ins, true, false); }
        public static void DataCMP_Imm(Arm7 arm7, uint ins) { _DataCMP(arm7, ins, true, false); }
        public static void DataCMN_Imm(Arm7 arm7, uint ins) { _DataCMN(arm7, ins, true, false); }
        public static void DataORR_Imm(Arm7 arm7, uint ins) { _DataORR(arm7, ins, true, false); }
        public static void DataMOV_Imm(Arm7 arm7, uint ins) { _DataMOV(arm7, ins, true, false); }
        public static void DataBIC_Imm(Arm7 arm7, uint ins) { _DataBIC(arm7, ins, true, false); }
        public static void DataMVN_Imm(Arm7 arm7, uint ins) { _DataMVN(arm7, ins, true, false); }

        public static void DataANDS_Reg(Arm7 arm7, uint ins) { _DataAND(arm7, ins, false, true); }
        public static void DataEORS_Reg(Arm7 arm7, uint ins) { _DataEOR(arm7, ins, false, true); }
        public static void DataSUBS_Reg(Arm7 arm7, uint ins) { _DataSUB(arm7, ins, false, true); }
        public static void DataRSBS_Reg(Arm7 arm7, uint ins) { _DataRSB(arm7, ins, false, true); }
        public static void DataADDS_Reg(Arm7 arm7, uint ins) { _DataADD(arm7, ins, false, true); }
        public static void DataADCS_Reg(Arm7 arm7, uint ins) { _DataADC(arm7, ins, false, true); }
        public static void DataSBCS_Reg(Arm7 arm7, uint ins) { _DataSBC(arm7, ins, false, true); }
        public static void DataRSCS_Reg(Arm7 arm7, uint ins) { _DataRSC(arm7, ins, false, true); }
        public static void DataTSTS_Reg(Arm7 arm7, uint ins) { _DataTST(arm7, ins, false, true); }
        public static void DataTEQS_Reg(Arm7 arm7, uint ins) { _DataTEQ(arm7, ins, false, true); }
        public static void DataCMPS_Reg(Arm7 arm7, uint ins) { _DataCMP(arm7, ins, false, true); }
        public static void DataCMNS_Reg(Arm7 arm7, uint ins) { _DataCMN(arm7, ins, false, true); }
        public static void DataORRS_Reg(Arm7 arm7, uint ins) { _DataORR(arm7, ins, false, true); }
        public static void DataMOVS_Reg(Arm7 arm7, uint ins) { _DataMOV(arm7, ins, false, true); }
        public static void DataBICS_Reg(Arm7 arm7, uint ins) { _DataBIC(arm7, ins, false, true); }
        public static void DataMVNS_Reg(Arm7 arm7, uint ins) { _DataMVN(arm7, ins, false, true); }

        public static void DataANDS_Imm(Arm7 arm7, uint ins) { _DataAND(arm7, ins, true, true); }
        public static void DataEORS_Imm(Arm7 arm7, uint ins) { _DataEOR(arm7, ins, true, true); }
        public static void DataSUBS_Imm(Arm7 arm7, uint ins) { _DataSUB(arm7, ins, true, true); }
        public static void DataRSBS_Imm(Arm7 arm7, uint ins) { _DataRSB(arm7, ins, true, true); }
        public static void DataADDS_Imm(Arm7 arm7, uint ins) { _DataADD(arm7, ins, true, true); }
        public static void DataADCS_Imm(Arm7 arm7, uint ins) { _DataADC(arm7, ins, true, true); }
        public static void DataSBCS_Imm(Arm7 arm7, uint ins) { _DataSBC(arm7, ins, true, true); }
        public static void DataRSCS_Imm(Arm7 arm7, uint ins) { _DataRSC(arm7, ins, true, true); }
        public static void DataTSTS_Imm(Arm7 arm7, uint ins) { _DataTST(arm7, ins, true, true); }
        public static void DataTEQS_Imm(Arm7 arm7, uint ins) { _DataTEQ(arm7, ins, true, true); }
        public static void DataCMPS_Imm(Arm7 arm7, uint ins) { _DataCMP(arm7, ins, true, true); }
        public static void DataCMNS_Imm(Arm7 arm7, uint ins) { _DataCMN(arm7, ins, true, true); }
        public static void DataORRS_Imm(Arm7 arm7, uint ins) { _DataORR(arm7, ins, true, true); }
        public static void DataMOVS_Imm(Arm7 arm7, uint ins) { _DataMOV(arm7, ins, true, true); }
        public static void DataBICS_Imm(Arm7 arm7, uint ins) { _DataBIC(arm7, ins, true, true); }
        public static void DataMVNS_Imm(Arm7 arm7, uint ins) { _DataMVN(arm7, ins, true, true); }

        public static void MCR(Arm7 arm7, uint ins)
        {
            uint opcode1 = (ins >> 21) & 0x7;
            uint cRn = (ins >> 16) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint coproc = (ins >> 8) & 0xF;
            uint opcode2 = (ins >> 5) & 0x7;
            uint cRm = (ins >> 0) & 0xF;

            if (coproc == 15)
            {
                uint rdVal = arm7.R[rd];
                arm7.Cp15.TransferTo(opcode1, rdVal, cRn, cRm, opcode2);
            }
        }

        public static void MRC(Arm7 arm7, uint ins)
        {
            uint opcode1 = (ins >> 21) & 0x7;
            uint cRn = (ins >> 16) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint coproc = (ins >> 8) & 0xF;
            uint opcode2 = (ins >> 5) & 0x7;
            uint cRm = (ins >> 0) & 0xF;

            if (coproc == 15)
            {
                uint data = arm7.Cp15.TransferFrom(opcode1, cRn, cRm, opcode2);
                if (rd == 15)
                {
                    arm7.Negative = BitTest(data, 31);
                    arm7.Zero = BitTest(data, 30);
                    arm7.Carry = BitTest(data, 29);
                    arm7.Overflow = BitTest(data, 28);
                }
                else
                {
                    arm7.R[rd] = data;
                }
            }
        }

        public static void CLZ(Arm7 arm7, uint ins)
        {
            uint rd = (ins >> 12) & 0xF;
            uint rm = (ins >> 0) & 0xF;

            uint rmVal = arm7.R[rm];

            if (rmVal == 0)
            {
                arm7.R[rd] = 32;
            }
            else
            {
                arm7.R[rd] = (uint)BitOperations.LeadingZeroCount(rmVal);
            }
        }

        public static void QADD(Arm7 arm7, uint ins)
        {
            uint rm = (ins >> 0) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            long rmVal = (int)arm7.R[rm];
            long rnVal = (int)arm7.R[rn];

            bool doubling = BitTest(ins, 22);
            if (doubling)
            {
                rnVal *= 2;

                if (rnVal < int.MinValue)
                {
                    rnVal = int.MinValue;
                    arm7.Sticky = true;
                }
                if (rnVal > int.MaxValue)
                {
                    rnVal = int.MaxValue;
                    arm7.Sticky = true;
                }
            }

            long result = rmVal + rnVal;

            if (result < int.MinValue)
            {
                result = int.MinValue;
                arm7.Sticky = true;
            }
            if (result > int.MaxValue)
            {
                result = int.MaxValue;
                arm7.Sticky = true;
            }

            arm7.R[rd] = (uint)result;
        }

        public static void QSUB(Arm7 arm7, uint ins)
        {
            uint rm = (ins >> 0) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            long rmVal = (int)arm7.R[rm];
            long rnVal = (int)arm7.R[rn];

            bool doubling = BitTest(ins, 22);
            if (doubling)
            {
                rnVal *= 2;

                if (rnVal < int.MinValue)
                {
                    rnVal = int.MinValue;
                    arm7.Sticky = true;
                }
                if (rnVal > int.MaxValue)
                {
                    rnVal = int.MaxValue;
                    arm7.Sticky = true;
                }
            }

            long result = rmVal - rnVal;

            if (result < int.MinValue)
            {
                result = int.MinValue;
                arm7.Sticky = true;
            }
            if (result > int.MaxValue)
            {
                result = int.MaxValue;
                arm7.Sticky = true;
            }

            arm7.R[rd] = (uint)result;
        }

        public static void SMLALxy(Arm7 arm7, uint ins)
        {
            bool x = BitTest(ins, 5);
            bool y = BitTest(ins, 6);

            uint rm = (ins >> 0) & 0xFU;
            uint rs = (ins >> 8) & 0xFU;
            uint rmVal = arm7.R[rm];
            uint rsVal = arm7.R[rs];

            uint rdLo = (ins >> 12) & 0xFU;
            uint rdHi = (ins >> 16) & 0xFU;
            uint rdLoVal = arm7.R[rdLo];
            uint rdHiVal = arm7.R[rdHi];

            short op1;
            if (!x)
                op1 = (short)rmVal;
            else
                op1 = (short)(rmVal >> 16);

            short op2;
            if (!y)
                op2 = (short)rsVal;
            else
                op2 = (short)(rsVal >> 16);

            long finalVal = (((long)rdHiVal << 32) | rdLoVal) + op1 * op2;
            arm7.R[rdLo] = (uint)finalVal;
            arm7.R[rdHi] = (uint)(finalVal >> 32);
        }

        public static void SMULxy(Arm7 arm7, uint ins)
        {
            bool x = BitTest(ins, 5);
            bool y = BitTest(ins, 6);

            uint rm = (ins >> 0) & 0xFU;
            uint rs = (ins >> 8) & 0xFU;
            uint rmVal = arm7.R[rm];
            uint rsVal = arm7.R[rs];

            uint rd = (ins >> 16) & 0xFU;

            short op1;
            if (!x)
                op1 = (short)rmVal;
            else
                op1 = (short)(rmVal >> 16);

            short op2;
            if (!y)
                op2 = (short)rsVal;
            else
                op2 = (short)(rsVal >> 16);

            arm7.R[rd] = (uint)(op1 * op2);
        }

        public static void SMLAxy(Arm7 arm7, uint ins)
        {
            bool x = BitTest(ins, 5);
            bool y = BitTest(ins, 6);

            uint rm = (ins >> 0) & 0xFU;
            uint rs = (ins >> 8) & 0xFU;
            uint rn = (ins >> 12) & 0xFU;
            uint rmVal = arm7.R[rm];
            uint rsVal = arm7.R[rs];
            uint rnVal = arm7.R[rn];

            uint rd = (ins >> 16) & 0xFU;

            short op1;
            if (!x)
                op1 = (short)rmVal;
            else
                op1 = (short)(rmVal >> 16);

            short op2;
            if (!y)
                op2 = (short)rsVal;
            else
                op2 = (short)(rsVal >> 16);

            uint mulVal = (uint)(op1 * op2);
            uint finalVal = mulVal + rnVal;
            arm7.R[rd] = finalVal;

            if (Arm7.CheckOverflowAdd(mulVal, rnVal, finalVal)) arm7.Sticky = true;

            // arm7.Error("test");
        }


        public static void Invalid(Arm7 arm7, uint ins)
        {
            arm7.Error($"Invalid ARM Instruction: {Hex(ins, 8)}");
        }
    }
}