using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public class Flash : SaveProvider
    {
        public byte Read8(uint addr)
        {
            switch (addr) {
                case 0x0E000000: return 0x62;
                case 0x0E000001: return 0x13;
            }

            return 0;
        }

        public void Write8(uint addr, byte val)
        {

        }
    }
}