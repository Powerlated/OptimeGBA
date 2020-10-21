using static OptimeGBA.Bits;
using static Util;
using System;

namespace OptimeGBA
{
    public delegate void ArmExecutor(ARM7 arm7, uint ins);

    public unsafe static class Arm
    {
        public static void SWI(ARM7 arm7, uint ins)
        {
            arm7.R14svc = arm7.R[15] - 4;
            arm7.SPSR_svc = arm7.GetCPSR();

            arm7.SetMode((uint)ARM7.ARM7Mode.Supervisor); // Go into SVC / Supervisor mode
            // arm7.ThumbState = false; // Back to ARM state
            arm7.IRQDisable = true;

            arm7.R[15] = ARM7.VectorSoftwareInterrupt;
            arm7.FlushPipeline();
        }

        public static void LDM(ARM7 arm7, uint ins)
        {
            arm7.LineDebug("LDM");

            const bool L = true;

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool S = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            bool loadsPc = BitTest(ins, 15);
            bool useUserModeRegs = S && (!L || !loadsPc) && (arm7.Mode != ARM7.ARM7Mode.User && arm7.Mode != ARM7.ARM7Mode.OldUser);

            if (S)
            {
                if (L && loadsPc)
                {
                    arm7.LineDebug("Load CPSR from SPSR");
                    arm7.SetCPSR(arm7.GetSPSR());
                }
            }

            // if (U && P && W) Error("U & P & W");

            arm7.LineDebug(L ? "Load" : "Store");
            arm7.LineDebug(P ? "No Include Base" : "Include Base");
            arm7.LineDebug(U ? "Upwards" : "Downwards");

            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];

            // String regs = "";

            uint bitsSet = (uint)System.Numerics.BitOperations.PopCount(ins & 0xFFFF);
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

            if (W)
            {
                arm7.R[rn] = writebackValue;
            }

            for (byte r = 0; r < 16; r++)
            {
                if (BitTest(ins, r))
                {
                    if (P) addr += 4;

                    if (!useUserModeRegs)
                    {
                        if (r != 15)
                        {
                            arm7.R[r] = arm7.Read32(addr & 0xFFFFFFFC);
                        }
                        else
                        {
                            arm7.R[15] = arm7.Read32(addr & 0xFFFFFFFC) & 0xFFFFFFFC;
                            arm7.FlushPipeline();
                        }
                    }
                    else
                    {
                        if (r != 15)
                        {
                            arm7.SetUserReg(r, arm7.Read32(addr & 0xFFFFFFFC));
                        }
                        else
                        {
                            arm7.R[15] = arm7.Read32(addr & 0xFFFFFFFC) & 0xFFFFFFFC;
                            arm7.FlushPipeline();
                        }
                    }

                    if (!P) addr += 4;
                }
            }

            bool emptyRlist = (ins & 0xFFFF) == 0;
            if (emptyRlist)
            {
                arm7.R[15] = arm7.Read32(addr & 0xFFFFFFFC);
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

            // arm7.LineDebug(regs);

            arm7.ICycle();
        }

        public static void STM(ARM7 arm7, uint ins)
        {
            arm7.LineDebug("STM");

            const bool L = false;

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool S = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            bool loadsPc = BitTest(ins, 15);
            bool useUserModeRegs = S && (!L || !loadsPc) && (arm7.Mode != ARM7.ARM7Mode.User && arm7.Mode != ARM7.ARM7Mode.OldUser);

            if (S)
            {
                if (L && loadsPc)
                {
                    arm7.LineDebug("Load CPSR from SPSR");
                    arm7.SetCPSR(arm7.GetSPSR());
                }
            }

            // if (U && P && W) Error("U & P & W");

            arm7.LineDebug(L ? "Load" : "Store");
            arm7.LineDebug(P ? "No Include Base" : "Include Base");
            arm7.LineDebug(U ? "Upwards" : "Downwards");

            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];

            // String regs = "";

            uint bitsSet = (uint)System.Numerics.BitOperations.PopCount(ins & 0xFFFF);
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

            arm7.FetchPipelineArm();

            for (byte r = 0; r < 16; r++)
            {
                if (BitTest(ins, r))
                {
                    // regs += $"R{r} ";

                    if (P) addr += 4;

                    if (!useUserModeRegs)
                    {
                        arm7.Write32(addr & 0xFFFFFFFC, arm7.R[r]);
                    }
                    else
                    {
                        arm7.Write32(addr & 0xFFFFFFFC, arm7.GetUserReg(r));
                    }

                    if (!P) addr += 4;

                    arm7.R[rn] = writebackValue;
                }
            }

            // Empty register list
            if ((ins & 0xFFFF) == 0)
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

            // arm7.LineDebug(regs);
        }

        public static void B(ARM7 arm7, uint ins)
        {
            arm7.LineDebug("B | Branch");
            // B
            int offset = (int)(ins & 0b111111111111111111111111) << 2;
            // Signed with Two's Complement
            // Cheap and easy sign-extend
            offset = (offset << 6) >> 6;

            // Link - store return address in R14
            if ((ins & BIT_24) != 0)
            {
                arm7.R[14] = arm7.R[15] - 4;
            }

            arm7.R[15] = (uint)(arm7.R[15] + offset);
            arm7.FlushPipeline();
        }

        public static void BX(ARM7 arm7, uint ins)
        {
            // BX - branch and optional switch to Thumb state
            arm7.LineDebug("BX");

            uint rm = ins & 0xF;
            uint rmValue = arm7.R[rm];

            arm7.ThumbState = BitTest(rmValue, 0);
            if (arm7.ThumbState)
            {
                arm7.LineDebug("Switch to THUMB State");
            }
            else
            {
                arm7.LineDebug("Switch to ARM State");
            }

            arm7.R[15] = (rmValue & 0xFFFFFFFE);
            arm7.FlushPipeline();
        }

        public static void SWP(ARM7 arm7, uint ins)
        {
            uint rm = (ins >> 0) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rn = (ins >> 16) & 0xF;

            uint addr = arm7.R[rn];
            uint storeValue = arm7.R[rm];

            arm7.LineDebug("SWP");
            uint readVal = ARM7.RotateRight32(arm7.Read32(addr & ~3u), (byte)((addr & 3u) * 8));
            arm7.Write32(addr & ~3u, storeValue);
            arm7.R[rd] = readVal;

            arm7.ICycle();
        }

        public static void SWPB(ARM7 arm7, uint ins)
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

        public static void MSR(ARM7 arm7, uint ins)
        {
            arm7.LineDebug("MSR");
            // MSR

            bool useSPSR = BitTest(ins, 22);

            // uint UnallocMask = 0x0FFFFF00;
            uint UserMask = 0xFFFFFFFF;
            uint PrivMask = 0xFFFFFFFF;
            uint StateMask = 0xFFFFFFFF;

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

                operand = ARM7.RotateRight32(constant, (byte)rotateBits);
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

            uint mask;

            if (!useSPSR)
            {
                // TODO: Fix privileged mode functionality in CPSR MSR
                if (arm7.Mode != ARM7.ARM7Mode.User)
                {
                    // Privileged
                    arm7.LineDebug("Privileged");
                    mask = byteMask & (UserMask | PrivMask);
                }
                else
                {
                    // Unprivileged
                    arm7.LineDebug("Unprivileged");
                    mask = byteMask & UserMask;
                }
                uint set = (arm7.GetCPSR() & ~mask) | (operand & mask);
                arm7.SetCPSR(set);
            }
            else
            {
                // TODO: Add SPSR functionality to MSR
                mask = byteMask & (UserMask | PrivMask | StateMask);
                arm7.SetSPSR((arm7.GetSPSR() & ~mask) | (operand & mask));
            }
        }

        public static void MRS(ARM7 arm7, uint ins)
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

        public static void MUL(ARM7 arm7, uint ins)
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

        public static void MULL(ARM7 arm7, uint ins)
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

        public static void RegularLDR(ARM7 arm7, uint ins)
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

            uint offset = RegularLDRSTRDecode(arm7, ins);

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
                    uint data = arm7.Read32(addr & 0xFFFFFFFC);
                    loadVal = ARM7.RotateRight32(data, (byte)(8 * (addr & 0b11)));

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
            }

            if (W || !P)
            {
                arm7.R[rn] = addr;
            }

            // Register loading happens after writeback, so if writeback register and Rd are the same, 
            // the writeback value would be overwritten by Rd.
            arm7.R[rd] = loadVal;

            if (rd == 15) arm7.FlushPipeline();

            arm7.ICycle();
        }

        public static void RegularSTR(ARM7 arm7, uint ins)
        {
            // STR (Store Register)
            arm7.LineDebug("STR (Store Register)");

            uint rn = (ins >> 16) & 0xF;
            uint rd = (ins >> 12) & 0xF;
            uint rnValue = arm7.R[rn];

            bool P = BitTest(ins, 24); // post-indexed / offset addressing 
            bool U = BitTest(ins, 23); // invert
            bool B = BitTest(ins, 22);
            bool W = BitTest(ins, 21);

            uint offset = RegularLDRSTRDecode(arm7, ins);

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

            arm7.LineDebug($"Rn: R{rn}");
            arm7.LineDebug($"Rd: R{rd}");

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
            }

            if (W || !P)
            {
                arm7.R[rn] = addr;
            }
        }


        public static uint RegularLDRSTRDecode(ARM7 arm7, uint ins)
        {
            bool registerOffset = BitTest(ins, 25);

            if (registerOffset)
            {
                // Register offset
                arm7.LineDebug($"Register Offset");
                uint rmVal = arm7.R[ins & 0xF];

                if ((ins & 0b111111110000) == 0b000000000000)
                {
                    arm7.LineDebug($"Non-scaled");
                    return rmVal;
                }
                else
                {
                    arm7.LineDebug($"Scaled");

                    uint shiftType = (ins >> 5) & 0b11;
                    byte shiftBits = (byte)((ins >> 7) & 0b11111);
                    switch (shiftType)
                    {
                        case 0b00:
                            return ARM7.LogicalShiftLeft32(rmVal, shiftBits);
                        case 0b01:
                            if (shiftBits == 0)
                            {
                                return 0;
                            }
                            else
                            {
                                return ARM7.LogicalShiftRight32(rmVal, shiftBits);
                            }
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
                                return (uint)((int)rmVal >> 31);
                            }
                            else
                            {
                                return ARM7.ArithmeticShiftRight32(rmVal, shiftBits);
                            }
                        default:
                        case 0b11:
                            if (shiftBits == 0)
                            {
                                return ARM7.LogicalShiftLeft32(arm7.Carry ? 1U : 0, 31) | (ARM7.LogicalShiftRight32(rmVal, 1));
                            }
                            else
                            {
                                return ARM7.RotateRight32(rmVal, shiftBits);
                            }
                    }
                }

            }
            else
            {
                // Immediate offset
                arm7.LineDebug($"Immediate Offset");

                // if (L && U && !registerOffset && rd == 0 && (ins & 0b111111111111) == 0) Error("sdfsdf");


                // This IS NOT A SHIFTED 32-BIT IMMEDIATE, IT'S PLAIN 12-BIT!
                return ins & 0b111111111111;
            }

        }

        public static void SpecialLDRSTR(ARM7 arm7, uint ins)
        {
            arm7.LineDebug("Halfword, Signed Byte, Doubleword Loads & Stores");
            arm7.LineDebug("LDR|STR H|SH|SB|D");

            bool L = BitTest(ins, 20);
            bool S = BitTest(ins, 6);
            bool H = BitTest(ins, 5);


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
                        loadVal = ARM7.RotateRight32(arm7.Read16(addr & ~1u), (byte)((addr & 1) * 8));
                    }
                }
            }
            else
            {
                if (S)
                {
                    if (H)
                    {
                        arm7.LineDebug("Store doubleword");
                        arm7.Error("UNIMPLEMENTED");
                    }
                    else
                    {
                        arm7.LineDebug("Load doubleword");
                        arm7.Error("UNIMPLEMENTED");
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

        public static void DataAND(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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

        public static void DataEOR(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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

        public static void DataSUB(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("SUB");

            uint aluOut = rnValue - shifterOperand;

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = !(shifterOperand > rnValue); // C
                arm7.Overflow = ARM7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V

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

        public static void DataRSB(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("RSB");

            uint aluOut = shifterOperand - rnValue;

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = !(rnValue > shifterOperand); // C
                arm7.Overflow = ARM7.CheckOverflowSub(shifterOperand, rnValue, aluOut); // V

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

        public static void DataADD(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("ADD");

            uint final = rnValue + shifterOperand;
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31); // N
                arm7.Zero = final == 0; // Z
                arm7.Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFFL; // C
                arm7.Overflow = ARM7.CheckOverflowAdd(rnValue, shifterOperand, final); // C

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

        public static void DataADC(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("ADC");

            uint final = rnValue + shifterOperand + (arm7.Carry ? 1U : 0);
            arm7.R[rd] = final;
            if (setFlags)
            {
                arm7.Negative = BitTest(final, 31); // N
                arm7.Zero = final == 0; // Z
                arm7.Carry = (long)rnValue + (long)shifterOperand + (arm7.Carry ? 1U : 0) > 0xFFFFFFFFL; // C
                arm7.Overflow = ARM7.CheckOverflowAdd(rnValue, shifterOperand, final); // V

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

        public static void DataSBC(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("SBC");

            uint aluOut = rnValue - shifterOperand - (!arm7.Carry ? 1U : 0U);

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = !((long)shifterOperand + (long)(!arm7.Carry ? 1U : 0) > rnValue); // C
                arm7.Overflow = ARM7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V

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

        public static void DataRSC(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("RSC");

            uint aluOut = shifterOperand - rnValue - (!arm7.Carry ? 1U : 0U);

            arm7.R[rd] = aluOut;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = !((long)rnValue + (long)(!arm7.Carry ? 1U : 0) > shifterOperand); // C
                arm7.Overflow = ARM7.CheckOverflowSub(shifterOperand, rnValue, aluOut); // V

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

        public static void DataTST(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("TST");

            uint final = rnValue & shifterOperand;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = shifterCarryOut;
        }

        public static void DataTEQ(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("TEQ");

            uint aluOut = rnValue ^ shifterOperand;
            if (setFlags)
            {
                arm7.Negative = BitTest(aluOut, 31); // N
                arm7.Zero = aluOut == 0; // Z
                arm7.Carry = shifterCarryOut; // C
            }
        }

        public static void DataCMP(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            // SBZ means should be zero, not relevant to the current code, just so you know
            arm7.LineDebug("CMP");

            uint aluOut = rnValue - shifterOperand;

            arm7.Negative = BitTest(aluOut, 31); // N
            arm7.Zero = aluOut == 0; // Z
            arm7.Carry = rnValue >= shifterOperand; // C
            arm7.Overflow = ARM7.CheckOverflowSub(rnValue, shifterOperand, aluOut); // V
        }

        public static void DataCMN(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

            arm7.LineDebug("CMN");

            uint aluOut = rnValue + shifterOperand;

            arm7.Negative = BitTest(aluOut, 31); // N
            arm7.Zero = aluOut == 0; // Z
            arm7.Carry = (long)rnValue + (long)shifterOperand > 0xFFFFFFFF; // C
            arm7.Overflow = ARM7.CheckOverflowAdd(rnValue, shifterOperand, aluOut); // V
        }

        public static void DataORR(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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

        public static void DataMOV(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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
                }
            }

            if (rd == 15)
            {
                arm7.FlushPipeline();
            }
        }

        public static void DataBIC(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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

        public static void DataMVN(ARM7 arm7, uint ins)
        {
            (uint rd, bool setFlags) = ARM7.ArmDataOperandDecode(ins);
            (uint shifterOperand, bool shifterCarryOut, uint rnValue) = arm7.ArmDataShiftAndApplyFlags(ins);

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

        public static void Invalid(ARM7 arm7, uint ins)
        {
            arm7.Error($"Invalid ARM Instruction: {Hex(ins, 8)}");
        }
    }
}