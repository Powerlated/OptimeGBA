using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public class NullSaveProvider : SaveProvider
    {
        public byte Read8(uint addr)
        {
            return 0;
        }

        public void Write8(uint addr, byte val)
        {

        }
    }
}