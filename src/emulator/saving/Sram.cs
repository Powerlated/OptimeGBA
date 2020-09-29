using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public class Sram : SaveProvider
    {
        byte[] Memory = new byte[65536];

        public override byte Read8(uint addr)
        {
            addr -= 0xE000000;
            if (addr < Memory.Length) {
                return Memory[addr];
            }
            return 0;
        }

        public override void Write8(uint addr, byte val)
        {
            addr -= 0xE000000;
            if (addr < Memory.Length) {
                Memory[addr] = val;
                Dirty = true;
            }
        }

        public override byte[] GetSave()
        {
            return Memory;
        }

        public override void LoadSave(byte[] save)
        {
            save.CopyTo(Memory, 0);
        }
    }
}