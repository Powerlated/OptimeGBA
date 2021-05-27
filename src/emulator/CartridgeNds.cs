namespace OptimeGBA
{
    public class CartridgeNds
    {
        Nds Nds;

        public CartridgeNds(Nds nds)
        {
            Nds = nds;
        }

        public byte ReadHwio8(uint addr)
        {
            switch (addr)
            {
                case 0x40001A4:// ROMCTRL B0
                    break;
                case 0x40001A5:// ROMCTRL B1
                    break;
                case 0x40001A6:// ROMCTRL B2
                    return 0b10000000;
                case 0x40001A7:// ROMCTRL B3
                    break;
            }
            
            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
        }
    }
}