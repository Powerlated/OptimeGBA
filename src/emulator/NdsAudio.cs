using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class NdsAudio
    {
        Nds Nds;

        public NdsAudio(Nds nds)
        {
            Nds = nds;
        }

        // SOUNDCNT
        public uint MasterVolume;
        public uint LeftOutputFrom;
        public uint RightOutputFrom;
        public bool Ch1ToMixer;
        public bool Ch3ToMixer;
        public bool MasterEnable;

        // SOUNDBIAS
        ushort SOUNDBIAS;

        // SNDCAPCNT
        byte SNDCAP0CNT;
        byte SNDCAP1CNT;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000500: // SOUNDCNT B0
                    val |= (byte)(MasterVolume & 0x7FU);
                    break;
                case 0x4000501: // SOUNDCNT B1
                    val |= (byte)((LeftOutputFrom & 0b11) << 0);
                    val |= (byte)((RightOutputFrom & 0b11) << 2);
                    if (Ch1ToMixer) val = BitSet(val, 4);
                    if (Ch3ToMixer) val = BitSet(val, 5);
                    if (MasterEnable) val = BitSet(val, 7);
                    break;

                case 0x4000504:
                    return (byte)(SOUNDBIAS >> 0);
                case 0x4000505:
                    return (byte)(SOUNDBIAS >> 8);

                case 0x4000508:
                    return SNDCAP0CNT;
                case 0x4000509:
                    return SNDCAP1CNT;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000500: // SOUNDCNT B0
                    MasterVolume = val & 0x7FU;
                    break;
                case 0x4000501: // SOUNDCNT B1
                    LeftOutputFrom = (byte)((val >> 0) & 0b11);
                    RightOutputFrom = (byte)((val >> 2) & 0b11);
                    Ch1ToMixer = BitTest(val, 4);
                    Ch3ToMixer = BitTest(val, 5);
                    MasterEnable = BitTest(val, 7);
                    break;

                case 0x4000504:
                    SOUNDBIAS &= 0xFF00;
                    SOUNDBIAS |= (ushort)(val << 0);
                    break;
                case 0x4000505:
                    SOUNDBIAS &= 0x00FF;
                    SOUNDBIAS |= (ushort)(val << 8);
                    break;

                case 0x4000508:
                    SNDCAP0CNT = val;
                    break;
                case 0x4000509:
                    SNDCAP1CNT = val;
                    break;
            }
        }
    }
}