using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class HWControl
    {
        GBA Gba;

        public HWControl(GBA gba)
        {
            Gba = gba;
        }

        public bool IME;

        public bool IE_VBlank;
        public bool IE_HBlank;
        public bool IE_VCounterMatch;
        public bool IE_Timer0Overflow;
        public bool IE_Timer1Overflow;
        public bool IE_Timer2Overflow;
        public bool IE_Timer3Overflow;
        public bool IE_Serial;
        public bool IE_DMA0;
        public bool IE_DMA1;
        public bool IE_DMA2;
        public bool IE_DMA3;
        public bool IE_Keypad;
        public bool IE_GamePak;

        public bool IF_VBlank;
        public bool IF_HBlank;
        public bool IF_VCounterMatch;
        public bool IF_Timer0Overflow;
        public bool IF_Timer1Overflow;
        public bool IF_Timer2Overflow;
        public bool IF_Timer3Overflow;
        public bool IF_Serial;
        public bool IF_DMA0;
        public bool IF_DMA1;
        public bool IF_DMA2;
        public bool IF_DMA3;
        public bool IF_Keypad;
        public bool IF_GamePak;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000200: // IE B0
                    if (IE_VBlank) val = BitSet(val, 0);
                    if (IE_HBlank) val = BitSet(val, 1);
                    if (IE_VCounterMatch) val = BitSet(val, 2);
                    if (IE_Timer0Overflow) val = BitSet(val, 3);
                    if (IE_Timer1Overflow) val = BitSet(val, 4);
                    if (IE_Timer2Overflow) val = BitSet(val, 5);
                    if (IE_Timer3Overflow) val = BitSet(val, 6);
                    if (IE_Serial) val = BitSet(val, 7);
                    break;
                case 0x4000201: // IE B1
                    if (IE_DMA0) val = BitSet(val, 0);
                    if (IE_DMA1) val = BitSet(val, 1);
                    if (IE_DMA2) val = BitSet(val, 2);
                    if (IE_DMA3) val = BitSet(val, 3);
                    if (IE_Keypad) val = BitSet(val, 4);
                    if (IE_GamePak) val = BitSet(val, 5);
                    break;

                case 0x4000202: // IF B0
                    if (IF_VBlank) val = BitSet(val, 0);
                    if (IF_HBlank) val = BitSet(val, 1);
                    if (IF_VCounterMatch) val = BitSet(val, 2);
                    if (IF_Timer0Overflow) val = BitSet(val, 3);
                    if (IF_Timer1Overflow) val = BitSet(val, 4);
                    if (IF_Timer2Overflow) val = BitSet(val, 5);
                    if (IF_Timer3Overflow) val = BitSet(val, 6);
                    if (IF_Serial) val = BitSet(val, 7);
                    break;
                case 0x4000203: // IF B1
                    if (IF_DMA0) val = BitSet(val, 0);
                    if (IF_DMA1) val = BitSet(val, 1);
                    if (IF_DMA2) val = BitSet(val, 2);
                    if (IF_DMA3) val = BitSet(val, 3);
                    if (IF_Keypad) val = BitSet(val, 4);
                    if (IF_GamePak) val = BitSet(val, 5);
                    break;

                case 0x4000208: // IME
                    if (IME) val = BitSet(val, 0);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000200: // IE B0
                    IE_VBlank = BitTest(val, 0);
                    IE_HBlank = BitTest(val, 1);
                    IE_VCounterMatch = BitTest(val, 2);
                    IE_Timer0Overflow = BitTest(val, 3);
                    IE_Timer1Overflow = BitTest(val, 4);
                    IE_Timer2Overflow = BitTest(val, 5);
                    IE_Timer3Overflow = BitTest(val, 6);
                    IE_Serial = BitTest(val, 7);
                    break;
                case 0x4000201: // IE B1
                    IE_DMA0 = BitTest(val, 0);
                    IE_DMA1 = BitTest(val, 1);
                    IE_DMA2 = BitTest(val, 2);
                    IE_DMA3 = BitTest(val, 3);
                    IE_Keypad = BitTest(val, 4);
                    IE_GamePak = BitTest(val, 5);
                    break;

                case 0x4000202: // IF B0
                    IF_VBlank = BitTest(val, 0);
                    IF_HBlank = BitTest(val, 1);
                    IF_VCounterMatch = BitTest(val, 2);
                    IF_Timer0Overflow = BitTest(val, 3);
                    IF_Timer1Overflow = BitTest(val, 4);
                    IF_Timer2Overflow = BitTest(val, 5);
                    IF_Timer3Overflow = BitTest(val, 6);
                    IF_Serial = BitTest(val, 7);
                    break;
                case 0x4000203: // IF B1
                    IF_DMA0 = BitTest(val, 0);
                    IF_DMA1 = BitTest(val, 1);
                    IF_DMA2 = BitTest(val, 2);
                    IF_DMA3 = BitTest(val, 3);
                    IF_Keypad = BitTest(val, 4);
                    IF_GamePak = BitTest(val, 5);
                    break;

                case 0x4000208: // IME
                    IME = BitTest(val, 0);
                    break;
            }
        }
    }
}