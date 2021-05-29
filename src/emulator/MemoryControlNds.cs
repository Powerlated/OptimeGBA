namespace OptimeGBA
{
    public class MemoryControlNds
    {
        public byte SharedRamControl;

        public byte ReadHwio8Nds9(uint addr)
        {
            switch (addr)
            {
                case 0x4000247:
                    return SharedRamControl;
            }

            return 0;
        }

        public void WriteHwio8Nds9(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000247:
                    SharedRamControl = (byte)(val & 0b11);
                    break;
            }
        }

        public byte ReadHwio8Nds7(uint addr)
        {
            switch (addr)
            {
                case 0x4000241:
                    return SharedRamControl;
            }

            return 0;
        }

        public void WriteHwio8Nds7(uint addr)
        {
            return;
        }
    }
}