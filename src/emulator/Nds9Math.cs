using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public unsafe sealed class Nds9Math
    {
        public Nds9 Nds9;

        public Nds9Math(Nds9 nds9)
        {
            Nds9 = nds9;

        }

        public long DIV_NUMER;
        public long DIV_DENOM;

        public long DIV_RESULT;
        public long DIVREM_RESULT;

        public uint SQRT_RESULT;
        public ulong SQRT_PARAM;

        // DIVCNT
        public uint DivisionMode;
        public bool DividedByZero;
        public bool Busy;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000280: // DIVCNT B0
                    val |= (byte)(DivisionMode & 0b11);
                    break;
                case 0x4000281: // DIVCNT B1
                    if (DividedByZero) val = BitSet(val, 6);
                    if (Busy) val = BitSet(val, 7);
                    break;

                case 0x4000290: // DIV_NUMER B0
                case 0x4000291: // DIV_NUMER B1
                case 0x4000292: // DIV_NUMER B2
                case 0x4000293: // DIV_NUMER B3
                case 0x4000294: // DIV_NUMER B4
                case 0x4000295: // DIV_NUMER B5
                case 0x4000296: // DIV_NUMER B6
                case 0x4000297: // DIV_NUMER B7
                    return (byte)(DIV_NUMER >> (int)((addr & 7) * 8));
                case 0x4000298: // DIV_DENOM B0
                case 0x4000299: // DIV_DENOM B1
                case 0x400029A: // DIV_DENOM B2
                case 0x400029B: // DIV_DENOM B3
                case 0x400029C: // DIV_DENOM B4
                case 0x400029D: // DIV_DENOM B5
                case 0x400029E: // DIV_DENOM B6
                case 0x400029F: // DIV_DENOM B7
                    return (byte)(DIV_DENOM >> (int)((addr & 7) * 8));
                case 0x40002A0: // DIV_RESULT B0
                case 0x40002A1: // DIV_RESULT B1
                case 0x40002A2: // DIV_RESULT B2
                case 0x40002A3: // DIV_RESULT B3
                case 0x40002A4: // DIV_RESULT B4
                case 0x40002A5: // DIV_RESULT B5
                case 0x40002A6: // DIV_RESULT B6
                case 0x40002A7: // DIV_RESULT B7
                    return (byte)(DIV_RESULT >> (int)((addr & 7) * 8));
                case 0x40002A8: // DIVREM_RESULT B0
                case 0x40002A9: // DIVREM_RESULT B1
                case 0x40002AA: // DIVREM_RESULT B2
                case 0x40002AB: // DIVREM_RESULT B3
                case 0x40002AC: // DIVREM_RESULT B4
                case 0x40002AD: // DIVREM_RESULT B5
                case 0x40002AE: // DIVREM_RESULT B6
                case 0x40002AF: // DIVREM_RESULT B7
                    return (byte)(DIVREM_RESULT >> (int)((addr & 7) * 8));


                case 0x40002B4: // SQRT_RESULT B0
                case 0x40002B5: // SQRT_RESULT B1
                case 0x40002B6: // SQRT_RESULT B2
                case 0x40002B7: // SQRT_RESULT B3
                    return (byte)(SQRT_RESULT >> (int)((addr & 3) * 8));
                case 0x40002B8: // SQRT_PARAM B0
                case 0x40002B9: // SQRT_PARAM B1
                case 0x40002BA: // SQRT_PARAM B2
                case 0x40002BB: // SQRT_PARAM B3
                case 0x40002BC: // SQRT_PARAM B4
                case 0x40002BD: // SQRT_PARAM B5
                case 0x40002BE: // SQRT_PARAM B6
                case 0x40002BF: // SQRT_PARAM B7
                    return (byte)(SQRT_PARAM >> (int)((addr & 7) * 8));

                default:
                    throw new NotImplementedException("Read from DS math @ " + Util.Hex(addr, 8));
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000280: // DIVCNT B0
                    DivisionMode = (byte)(val & 0b11);
                    break;
                case 0x4000281: // DIVCNT B1
                    break;

                case 0x4000290: // DIV_NUMER B0
                case 0x4000291: // DIV_NUMER B1
                case 0x4000292: // DIV_NUMER B2
                case 0x4000293: // DIV_NUMER B3
                case 0x4000294: // DIV_NUMER B4
                case 0x4000295: // DIV_NUMER B5
                case 0x4000296: // DIV_NUMER B6
                case 0x4000297: // DIV_NUMER B7
                    DIV_NUMER &= (long)(~(0xFFU << (int)((addr & 7) * 8)));
                    DIV_NUMER |= (long)val << (int)((addr & 7) * 8);
                    Divide();
                    break;
                case 0x4000298: // DIV_DENOM B0
                case 0x4000299: // DIV_DENOM B1
                case 0x400029A: // DIV_DENOM B2
                case 0x400029B: // DIV_DENOM B3
                case 0x400029C: // DIV_DENOM B4
                case 0x400029D: // DIV_DENOM B5
                case 0x400029E: // DIV_DENOM B6
                case 0x400029F: // DIV_DENOM B7
                    DIV_DENOM &= (long)(~(0xFFU << (int)((addr & 7) * 8)));
                    DIV_DENOM |= (long)val << (int)((addr & 7) * 8);
                    Divide();
                    break;

                default:
                    throw new NotImplementedException("Write to DS math @ " + Util.Hex(addr, 8));
            }
        }

        public void Divide()
        {
            long adjDenom = DivisionMode == 2 ? DIV_DENOM : (int)DIV_DENOM;

            if (DIV_DENOM == 0)
            {
                DividedByZero = true;
                // DIV_RESULT = (DIV_RESULT >> 63) << 63;
                // DIVREM_RESULT = DIV_NUMER;
                return;
            }

            switch (DivisionMode)
            {
                case 0: // 32bit / 32bit
                    DIV_RESULT = (int)DIV_NUMER / (int)DIV_DENOM;
                    DIVREM_RESULT = (int)DIV_NUMER % (int)DIV_DENOM;
                    break;
                case 3:
                case 1: // 64bit / 32bit
                    DIV_RESULT = (int)DIV_NUMER / DIV_DENOM;
                    DIVREM_RESULT = (int)DIV_NUMER % DIV_DENOM;
                    break;
                case 2: // 64bit / 64bit
                    DIV_RESULT = DIV_NUMER / DIV_DENOM;
                    DIVREM_RESULT = DIV_NUMER % DIV_DENOM;
                    break;
            }
        }
    }
}