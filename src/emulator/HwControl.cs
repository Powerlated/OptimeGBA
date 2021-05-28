namespace OptimeGBA
{
    public abstract class HwControl
    {
        public bool IME;

        public uint IE;
        public uint IF;

        public bool Available;

        public abstract void FlagInterrupt(uint interruptFlag);
    }
}