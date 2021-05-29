namespace OptimeGBA
{
    public class NdsAudio
    {
        Nds Nds;

        public NdsAudio(Nds nds) {
            Nds = nds;
        }
        
        // SOUNDBIAS
        ushort SOUNDBIAS;

        public byte ReadHwio8(uint addr)
        {
            switch (addr)
            {
                case 0x4000504:
                    return (byte)(SOUNDBIAS >> 0);
                case 0x4000505:
                    return (byte)(SOUNDBIAS >> 8);
            }

            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000504:
                    SOUNDBIAS &= 0xFF00;
                    SOUNDBIAS |= (ushort)(val << 0);
                    break;
                case 0x4000505:
                    SOUNDBIAS &= 0x00FF;
                    SOUNDBIAS |= (ushort)(val << 8);
                    break;
            }
        }
    }
}