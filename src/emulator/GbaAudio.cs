namespace OptimeGBA
{
    public class GBAAudio
    {
        GBA Gba;
        public GBAAudio(GBA gba)
        {
            Gba = gba;
        }

        uint BiasLevel = 0x100;
        uint AmplitudeRes;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000088: // SOUNDBIAS B0
                    val |= (byte)(BiasLevel << 1);
                    break;
                case 0x4000089: // SOUNDBIAS B1
                    val |= (byte)(BiasLevel >> 7);
                    val |= (byte)(AmplitudeRes << 6);
                    break;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000088: // SOUNDBIAS B0
                    BiasLevel &= 0b110000000;
                    BiasLevel |= (uint)((val >> 1) & 0b1111111);
                    break;
                case 0x4000089: // SOUNDBIAS B1
                    BiasLevel &= 0b001111111;
                    BiasLevel |= (uint)((val & 0b11) << 7);

                    AmplitudeRes &= 0;
                    AmplitudeRes |= (uint)((val >> 6) & 0b11);
                    break;
            }
        }
    }
}