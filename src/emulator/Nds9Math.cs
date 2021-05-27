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

        public byte ReadHwio8(uint addr)
        {
            throw new NotImplementedException("Read from DS math");
        }

        public void WriteHwio8(uint addr, byte val)
        {
            throw new NotImplementedException("Write to DS math");
        }
    }
}