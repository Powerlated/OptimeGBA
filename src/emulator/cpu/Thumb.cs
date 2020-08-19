using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class Thumb
    {

        public static void MovImmediate(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 8) & 0b111);
            uint immed8 = ins & 0xFFu;

            arm7.LineDebug("MOV | Move large immediate to register");

            arm7.R[rd] = immed8;

            arm7.Negative = false;
            arm7.Zero = immed8 == 0;
        }

        public static void CmpImmediate(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 8) & 0b111);
            uint immed8 = ins & 0xFFu;

            arm7.LineDebug("CMP (1)");

            uint rnVal = arm7.R[rd];
            uint alu_out = rnVal - immed8;

            arm7.Negative = BitTest(alu_out, 31);
            arm7.Zero = alu_out == 0;
            arm7.Carry = !(immed8 > rnVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rnVal, immed8, alu_out);
        }

        public static void AddImmediate(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 8) & 0b111);
            uint immed8 = ins & 0xFFu;

            arm7.LineDebug("ADD (2)");

            uint rdVal = arm7.R[rd];
            uint final = rdVal + immed8;

            arm7.R[rd] = final;
            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = (long)rdVal + (long)immed8 > 0xFFFFFFFF;
            arm7.Overflow = ARM7.CheckOverflowAdd(rdVal, immed8, final);
        }

        public static void SubImmediate(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 8) & 0b111);
            uint immed8 = ins & 0xFFu;

            arm7.LineDebug("SUB (2)");

            uint rdVal = arm7.R[rd];

            uint final = rdVal - immed8;
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = !(immed8 > rdVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rdVal, immed8, final);
        }


        public static void DataAND(ARM7 arm7, ushort ins) // AND
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("AND");

            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            uint final = rdVal & rmVal;
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
        }

        public static void DataEOR(ARM7 arm7, ushort ins) // EOR
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("EOR");

            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            rdVal = rdVal ^ rmVal;
            arm7.R[rd] = rdVal;

            arm7.Negative = BitTest(rdVal, 31);
            arm7.Zero = rdVal == 0;
        }

        public static void DataLSL(ARM7 arm7, ushort ins) // LSL (2)
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("LSL (2) | Logical Shift Left");

            uint rdValue = arm7.R[(uint)((ins >> 0) & 0b111)];
            uint rsValue = arm7.R[(uint)((ins >> 3) & 0b111)];

            if ((rsValue & 0xFF) == 0)
            {
                // Do nothing
            }
            else if ((rsValue & 0xFF) < 32)
            {
                arm7.Carry = BitTest(rdValue, (byte)(32 - (rsValue & 0xFF)));
                rdValue = ARM7.LogicalShiftLeft32(rdValue, (byte)(rsValue & 0xFF));
            }
            else if ((rsValue & 0xFF) == 32)
            {
                arm7.Carry = BitTest(rdValue, 0);
                rdValue = 0;
            }
            else
            {
                arm7.Carry = false;
                rdValue = 0;
            }

            arm7.R[rd] = rdValue;

            arm7.Negative = BitTest(rdValue, 31);
            arm7.Zero = rdValue == 0;
        }

        public static void DataLSR(ARM7 arm7, ushort ins) // LSR (2)
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("LSR (2)");

            uint rdVal = arm7.R[rd];
            uint rsVal = arm7.R[rs];

            if ((rsVal & 0xFF) == 0)
            {
                // everything unaffected
            }
            else if ((rsVal & 0xFF) < 32)
            {
                arm7.Carry = BitTest(rdVal, (byte)((rsVal & 0xFF) - 1));
                arm7.R[rd] = ARM7.LogicalShiftRight32(rdVal, (byte)(rsVal & 0xFF));
            }
            else if ((rsVal & 0xFF) == 32)
            {
                arm7.Carry = BitTest(rdVal, 31);
                arm7.R[rd] = 0;
            }
            else
            {
                arm7.Carry = false;
                arm7.R[rd] = 0;
            }

            rdVal = arm7.R[rd];

            arm7.Negative = BitTest(rdVal, 31);
            arm7.Zero = rdVal == 0;
        }

        public static void DataASR(ARM7 arm7, ushort ins) // ASR (2)
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("ASR (2)");

            uint rdVal = arm7.R[rd];
            uint rsVal = arm7.R[rs];

            if ((rsVal & 0xFF) == 0)
            {
                // Do nothing
            }
            else if ((rsVal & 0xFF) < 32)
            {
                arm7.Carry = BitTest(rdVal, (byte)((rsVal & 0xFF) - 1));
                rdVal = ARM7.ArithmeticShiftRight32(rdVal, (byte)(rsVal & 0xFF));
            }
            else
            {
                arm7.Carry = BitTest(rdVal, 31);
                if (!arm7.Carry)
                {
                    rdVal = 0;
                }
                else
                {
                    rdVal = 0xFFFFFFFF;
                }
            }

            arm7.R[rd] = rdVal;

            arm7.Negative = BitTest(rdVal, 31);
            arm7.Zero = rdVal == 0;
        }

        public static void DataADC(ARM7 arm7, ushort ins) // ADC
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            uint final = rdVal + rmVal + (arm7.Carry ? 1U : 0);
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = rdVal == 0;
            arm7.Carry = (long)rdVal + (long)rmVal + (arm7.Carry ? 1U : 0) > 0xFFFFFFFF;
            arm7.Overflow = ARM7.CheckOverflowAdd(rdVal, rmVal + (arm7.Carry ? 1U : 0), final);
        }

        public static void DataSBC(ARM7 arm7, ushort ins) // SBC
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("SBC");

            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            uint final = rdVal - rmVal - (!arm7.Carry ? 1U : 0);
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = !((long)rmVal + (!arm7.Carry ? 1U : 0) > rdVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rdVal, rmVal + (!arm7.Carry ? 1U : 0), final);
        }

        public static void DataROR(ARM7 arm7, ushort ins) // ROR
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("ROR");

            uint rdVal = arm7.R[rd];
            uint rsVal = arm7.R[rs];

            if ((rsVal & 0xFF) == 0)
            {
                // Do nothing
            }
            else if ((rsVal & 0b11111) == 0)
            {
                arm7.Carry = BitTest(rdVal, 31);
            }
            else
            {
                arm7.Carry = BitTest(rdVal, (byte)((rsVal & 0b11111) - 1));
                rdVal = ARM7.RotateRight32(rdVal, (byte)(rsVal & 0b11111));
                arm7.R[rd] = rdVal;
            }

            arm7.Negative = BitTest(rdVal, 31);
            arm7.Zero = rdVal == 0;
        }

        public static void DataTST(ARM7 arm7, ushort ins) // TST
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("TST");

            uint rnValue = arm7.R[rn];
            uint rmValue = arm7.R[rm];

            uint final = rnValue & rmValue;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
        }

        public static void DataNEG(ARM7 arm7, ushort ins) // NEG / RSB
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("NEG / RSB");
            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            uint final = 0 - rmVal;

            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = !(rmVal > 0);
            arm7.Overflow = ARM7.CheckOverflowSub(0, rmVal, final);
        }

        public static void DataCMP(ARM7 arm7, ushort ins) // CMP (2)
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("CMP (2)");

            uint rnVal = arm7.R[(uint)((ins >> 0) & 0b111)];
            uint rmVal = arm7.R[(uint)((ins >> 3) & 0b111)];

            uint alu_out = rnVal - rmVal;

            arm7.Negative = BitTest(alu_out, 31);
            arm7.Zero = alu_out == 0;
            arm7.Carry = !(rmVal > rnVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rnVal, rmVal, alu_out);
        }

        public static void DataCMN(ARM7 arm7, ushort ins)  // CMN
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("CMN");

            uint rnVal = arm7.R[(uint)((ins >> 0) & 0b111)];
            uint rmVal = arm7.R[(uint)((ins >> 3) & 0b111)];

            uint alu_out = rnVal + rmVal;

            arm7.Negative = BitTest(alu_out, 31);
            arm7.Zero = alu_out == 0;
            arm7.Carry = (long)rmVal + (long)rnVal > 0xFFFFFFFF;
            arm7.Overflow = ARM7.CheckOverflowAdd(rnVal, rmVal, alu_out);
        }

        public static void DataORR(ARM7 arm7, ushort ins) // ORR
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("ORR");

            arm7.R[rd] = arm7.R[rd] | arm7.R[rm];
            arm7.Negative = BitTest(arm7.R[rd], 31);
            arm7.Zero = arm7.R[rd] == 0;
        }

        public static void DataMUL(ARM7 arm7, ushort ins) // MUL
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("MUL");

            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            rdVal = (rmVal * rdVal);
            arm7.R[rd] = rdVal;

            arm7.Negative = BitTest(rdVal, 31);
            arm7.Zero = rdVal == 0;
        }

        public static void DataBIC(ARM7 arm7, ushort ins) // BIC
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("BIC");

            uint rdValue = arm7.R[rd];
            uint rmValue = arm7.R[rm];

            uint final = rdValue & (~rmValue);
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
        }

        public static void DataMVN(ARM7 arm7, ushort ins) // MVN
        {
            // Rm/Rs and Rd/Rn are the same, just different names for opcodes in this encoding
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = rd;
            uint rm = (uint)((ins >> 3) & 0b111);
            uint rs = rm;

            arm7.LineDebug("MVN");

            arm7.R[rd] = ~arm7.R[rm];
            arm7.Negative = BitTest(arm7.R[rd], 31);
            arm7.Zero = arm7.R[rd] == 0;
        }


        public static void SpecialDataADD(ARM7 arm7, ushort ins) // ADD (4)
        {
            arm7.LineDebug("ADD (4)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rm = (uint)((ins >> 3) & 0b111);
            rd += BitTest(ins, 7) ? BIT_3 : 0;
            rm += BitTest(ins, 6) ? BIT_3 : 0;
            uint rdVal = arm7.R[rd];
            uint rmVal = arm7.R[rm];

            uint final = rdVal + rmVal;
            arm7.R[rd] = final;

            if (rd == 15)
            {
                arm7.FlushPipeline();
            }
        }

        public static void SpecialDataCMP(ARM7 arm7, ushort ins) // CMP (3)
        {
            arm7.LineDebug("CMP (3)");

            uint rn = (uint)((ins >> 0) & 0b111);
            uint rm = (uint)((ins >> 3) & 0b111);

            rn += BitTest(ins, 7) ? BIT_3 : 0;
            rm += BitTest(ins, 6) ? BIT_3 : 0;

            uint rnVal = arm7.R[rn];
            uint rmVal = arm7.R[rm];

            uint alu_out = rnVal - rmVal;

            arm7.Negative = BitTest(alu_out, 31);
            arm7.Zero = alu_out == 0;
            arm7.Carry = !(rmVal > rnVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rnVal, rmVal, alu_out);
        }

        public static void SpecialDataMOV(ARM7 arm7, ushort ins)// MOV (3)
        {
            arm7.LineDebug("MOV (3)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rm = (uint)((ins >> 3) & 0b111);
            rd += BitTest(ins, 7) ? BIT_3 : 0;
            rm += BitTest(ins, 6) ? BIT_3 : 0;

            arm7.R[rd] = arm7.R[rm];

            if (rd == 15)
            {
                arm7.R[15] &= 0xFFFFFFFE;
                arm7.FlushPipeline();
            }
        }

        public static void SpecialDataBX(ARM7 arm7, ushort ins) // BX
        {
            arm7.LineDebug("BX | Optionally switch back to ARM state");

            uint rm = (uint)((ins >> 3) & 0xF); // High bit is technically an H bit, but can be ignored here
            uint val = arm7.R[rm];
            arm7.LineDebug($"R{rm}");

            arm7.ThumbState = BitTest(val, 0);
            arm7.R[15] = val & 0xFFFFFFFE;
            arm7.FlushPipeline();
        }

        public static void LDRLiteralPool(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("LDR (3) | PC Relative, 8-bit Immediate");

            uint rd = (uint)((ins >> 8) & 0b111);
            uint immed8 = (uint)((ins >> 0) & 0xFF);

            uint addr = (arm7.R[15] & 0xFFFFFFFC) + (immed8 * 4);
            if ((addr & 0b11) != 0)
            {
                // Misaligned
                uint readAddr = addr & ~0b11U;
                uint readVal = arm7.Gba.Mem.Read32(readAddr);
                arm7.R[rd] = ARM7.RotateRight32(readVal, (byte)((addr & 0b11) * 8));
            }
            else
            {
                uint readVal = arm7.Gba.Mem.Read32(addr);
                arm7.R[rd] = readVal;
            }
        }

        public static void ImmShiftLSL(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("LSL (1) | Logical Shift Left");

            uint immed5 = (uint)((ins >> 6) & 0b11111);
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rmValue = arm7.R[(uint)((ins >> 3) & 0b111)];

            if (immed5 == 0)
            {
                arm7.R[rd] = rmValue;
            }
            else
            {
                arm7.Carry = BitTest(rmValue, (byte)(32 - immed5));
                arm7.R[rd] = ARM7.LogicalShiftLeft32(rmValue, (byte)immed5);
            }

            arm7.Negative = BitTest(arm7.R[rd], 31);
            arm7.Zero = arm7.R[rd] == 0;
        }

        public static void ImmShiftLSR(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("LSR (1)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rm = (uint)((ins >> 3) & 0b111);
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            uint rmVal = arm7.R[rm];

            uint final;
            if (immed5 == 0)
            {
                arm7.Carry = BitTest(rmVal, 31);
                final = 0;
            }
            else
            {
                arm7.Carry = BitTest(rmVal, (byte)(immed5 - 1));
                final = ARM7.LogicalShiftRight32(rmVal, (byte)immed5);
            }

            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
        }

        public static void ImmShiftASR(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("ASR (1)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rmValue = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            if (immed5 == 0)
            {
                arm7.Carry = BitTest(rmValue, 31);
                if (BitTest(rmValue, 31))
                {
                    arm7.R[rd] = 0xFFFFFFFF;
                }
                else
                {
                    arm7.R[rd] = 0;
                }
            }
            else
            {
                arm7.Carry = BitTest(rmValue, (byte)(immed5 - 1));
                arm7.R[rd] = ARM7.ArithmeticShiftRight32(rmValue, (byte)immed5);
            }

            arm7.Negative = BitTest(arm7.R[rd], 31);
            arm7.Zero = arm7.R[rd] == 0;
        }

        public static void ImmAluADD1(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("ADD (3)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rnVal = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint rmVal = arm7.R[(uint)((ins >> 6) & 0b111)];
            uint final = rnVal + rmVal;

            arm7.R[rd] = final;
            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = (long)rnVal + (long)rmVal > 0xFFFFFFFF;
            arm7.Overflow = ARM7.CheckOverflowAdd(rnVal, rmVal, final);
        }

        public static void ImmAluSUB1(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("SUB (3)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rnValue = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint rmValue = arm7.R[(uint)((ins >> 6) & 0b111)];

            uint final = rnValue - rmValue;
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = !(rmValue > rnValue);
            arm7.Overflow = ARM7.CheckOverflowSub(rnValue, rmValue, final);
        }

        public static void ImmAluADD2(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("ADD (1)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rnVal = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint immed3 = (uint)((ins >> 6) & 0b111);

            uint final = rnVal + immed3;

            arm7.R[rd] = final;
            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = (long)rnVal + (long)immed3 > 0xFFFFFFFF;
            arm7.Overflow = ARM7.CheckOverflowAdd(rnVal, immed3, final);

            if (rd == 15) arm7.FlushPipeline();
        }

        public static void ImmAluSUB2(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("SUB (1)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rn = (uint)((ins >> 3) & 0b111);
            uint immed3 = (uint)((ins >> 6) & 0b111);

            uint rdVal = arm7.R[rd];
            uint rnVal = arm7.R[rn];

            uint final = rnVal - immed3;
            arm7.R[rd] = final;

            arm7.Negative = BitTest(final, 31);
            arm7.Zero = final == 0;
            arm7.Carry = !(immed3 > rnVal);
            arm7.Overflow = ARM7.CheckOverflowSub(rnVal, immed3, final);
        }

        public static void ImmOffsLDR(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("LDR (1) | Base + Immediate");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rnValue = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            uint addr = rnValue + (immed5 * 4);

            // Misaligned
            uint readAddr = addr & ~0b11U;
            uint readVal = arm7.Gba.Mem.Read32(readAddr);
            arm7.R[rd] = ARM7.RotateRight32(readVal, (byte)((addr & 0b11) * 8));

            arm7.LineDebug($"Addr: {Util.HexN(addr, 8)}");
        }

        public static void ImmOffsSTR(ARM7 arm7, ushort ins)
        {
            arm7.LineDebug("STR (1)");

            uint rd = (uint)((ins >> 0) & 0b111);
            uint rnValue = arm7.R[(uint)((ins >> 3) & 0b111)];
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            uint addr = rnValue + (immed5 * 4);
            arm7.LineDebug($"Addr: {Util.HexN(addr, 8)}");

            arm7.Gba.Mem.Write32(addr, arm7.R[rd]);
        }

        public static void ImmOffsSTRB(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rdVal = arm7.R[rd];
            uint rn = (uint)((ins >> 3) & 0b111);
            uint rnVal = arm7.R[rn];
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            uint addr = rnVal + immed5;

            arm7.LineDebug("STRB (1)");
            arm7.Gba.Mem.Write8(addr, (byte)rdVal);
        }

        public static void ImmOffsLDRB(ARM7 arm7, ushort ins)
        {
            uint rd = (uint)((ins >> 0) & 0b111);
            uint rdVal = arm7.R[rd];
            uint rn = (uint)((ins >> 3) & 0b111);
            uint rnVal = arm7.R[rn];
            uint immed5 = (uint)((ins >> 6) & 0b11111);

            uint addr = rnVal + immed5;

            arm7.LineDebug("LDRB (1)");
            arm7.R[rd] = arm7.Gba.Mem.Read8(addr);

        }
    }
}