using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public sealed class Keypad
    {
        public bool A;
        public bool B;
        public bool Start;
        public bool Select;
        public bool Right;
        public bool Left;
        public bool Up;
        public bool Down;
        public bool R;
        public bool L;

        // DS Exclusive
        public bool X;
        public bool Y;
        public bool DebugButton;
        public bool Touch;
        public bool ScreensOpen = true; // DS folded

        public byte ReadHwio8(uint addr)
        {
            byte val = 0xFF;
            switch (addr)
            {
                case 0x4000130: // KEYINPUT B0
                    if (A) val = BitClear(val, 0);
                    if (B) val = BitClear(val, 1);
                    if (Select) val = BitClear(val, 2);
                    if (Start) val = BitClear(val, 3);
                    if (Right) val = BitClear(val, 4);
                    if (Left) val = BitClear(val, 5);
                    if (Up) val = BitClear(val, 6);
                    if (Down) val = BitClear(val, 7);
                    break;
                case 0x4000131: // KEYINPUT B1
                    if (R) val = BitClear(val, 8 - 8);
                    if (L) val = BitClear(val, 9 - 8);
                    break;

                case 0x4000136: // EXTKEYIN - ARM7 only
                    if (X) val = BitClear(val, 0);
                    if (Y) val = BitClear(val, 1);
                    if (DebugButton) val = BitClear(val, 3);
                    if (Touch) val = BitClear(val, 6);
                    if (ScreensOpen) val = BitClear(val, 7);
                    break;
            }

            return val;
        }
    }
}