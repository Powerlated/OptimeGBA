using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public abstract class HwControl
    {
        public abstract byte ReadHwio8(uint addr);
        public abstract void WriteHwio8(uint addr, byte val);
        public abstract void FlagInterrupt(InterruptGba i);
        public abstract void CheckAndFireInterrupts();

        public bool IME = false;

        public ushort IE;
        public ushort IF;

        public bool AvailableAndEnabled = false;
        public bool Available;
    }
}