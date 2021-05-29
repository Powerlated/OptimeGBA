using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds9Math
    {
        public Nds9 Nds9;

        public Nds9Math(Nds9 nds9)
        {
            Nds9 = nds9;

        }

        public uint SqrtResult;
        public ulong SqrtParam;

        public byte ReadHwio8(uint addr)
        {
            throw new NotImplementedException("Read from DS math");
            switch (addr)
            {
                case 0x40002B4: // SQRT_RESULT B0
                case 0x40002B5: // SQRT_RESULT B1
                case 0x40002B6: // SQRT_RESULT B2
                case 0x40002B7: // SQRT_RESULT B3
                    return (byte)(SqrtResult >> (int)((addr & 3) * 8));
                case 0x40002B8: // SQRT_PARAM B0
                case 0x40002B9: // SQRT_PARAM B1
                case 0x40002BA: // SQRT_PARAM B2
                case 0x40002BB: // SQRT_PARAM B3
                case 0x40002BC: // SQRT_PARAM B4
                case 0x40002BD: // SQRT_PARAM B5
                case 0x40002BE: // SQRT_PARAM B6
                case 0x40002BF: // SQRT_PARAM B7
                    return (byte)(SqrtParam >> (int)((addr & 7) * 8));


            }
            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            throw new NotImplementedException("Read from DS math");

        }
    }
}