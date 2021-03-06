using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public sealed class NullSaveProvider : SaveProvider
    {
        public override byte Read8(uint addr)
        {
            return 0; // Consider throwing NotImplementedException?
        }

        public override void Write8(uint addr, byte val)
        {
            // Consider throwing NotImplementedException?
        }

        public override byte[] GetSave()
        {
            return new byte[0]; // Consider throwing NotImplementedException?
        }

        public override void LoadSave(byte[] save)
        {
            // Consider throwing NotImplementedException?
        }
    }
}