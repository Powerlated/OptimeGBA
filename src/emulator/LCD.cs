using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public class LCD
    {
        GBA Gba;
        public LCD(GBA gba)
        {
            Gba = gba;
        }

        uint Mode;
        bool CgbMode;
        bool DisplayFrameSelect;
        bool HBlankIntervalFree;
        bool ObjCharacterVramMapping;
        bool ForcedBlank;
        bool ScreenDisplayBg0;
        bool ScreenDisplayBg1;
        bool ScreenDisplayBg2;
        bool ScreenDisplayBg3;
        bool ScreenDisplayObj;
        bool Window0DisplayFlag;
        bool Window1DisplayFlag;
        bool ObjWindowDisplayFlag;




        public byte Read8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    val |= (byte)(Mode & 0b111);
                    if (CgbMode) val = BitSet(val, 3);
                    if (DisplayFrameSelect) val = BitSet(val, 4);
                    if (HBlankIntervalFree) val = BitSet(val, 5);
                    if (ObjCharacterVramMapping) val = BitSet(val, 6);
                    if (ForcedBlank) val = BitSet(val, 7);
                    break;
                case 0x4000001: // DISPCNT B1
                    if (ScreenDisplayBg0) val = BitSet(val, 8 - 8);
                    if (ScreenDisplayBg1) val = BitSet(val, 9 - 8);
                    if (ScreenDisplayBg2) val = BitSet(val, 10 - 8);
                    if (ScreenDisplayBg3) val = BitSet(val, 11 - 8);
                    if (ScreenDisplayObj) val = BitSet(val, 12 - 8);
                    if (Window0DisplayFlag) val = BitSet(val, 13 - 8);
                    if (Window1DisplayFlag) val = BitSet(val, 14 - 8);
                    if (ObjWindowDisplayFlag) val = BitSet(val, 15 - 8);
                    break;
            }

            return val;
        }

        public void Write8(uint addr, byte preConv)
        {
            uint val = preConv;
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    val <<= 0;
                    Mode = val & 0b111;
                    CgbMode = BitTest(val, 3);
                    DisplayFrameSelect = BitTest(val, 4);
                    HBlankIntervalFree = BitTest(val, 5);
                    ObjCharacterVramMapping = BitTest(val, 6);
                    ForcedBlank = BitTest(val, 7);
                    break;
                case 0x4000001: // DISPCNT B1
                    val <<= 8;
                    ScreenDisplayBg0 = BitTest(val, 8);
                    ScreenDisplayBg1 = BitTest(val, 9);
                    ScreenDisplayBg2 = BitTest(val, 10);
                    ScreenDisplayBg3 = BitTest(val, 11);
                    ScreenDisplayObj = BitTest(val, 12);
                    Window0DisplayFlag = BitTest(val, 13);
                    Window1DisplayFlag = BitTest(val, 14);
                    ObjWindowDisplayFlag = BitTest(val, 15);
                    break;
            }
        }
    }
}