using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public class Sram : SaveProvider
    {
        public override byte Read8(uint addr)
        {
            return 0;
        }

        public override void Write8(uint addr, byte val)
        {

        }

        public override byte[] GetSave()
        {
            return new byte[0];
        }

        public override void LoadSave(byte[] save)
        {

        }
    }
}