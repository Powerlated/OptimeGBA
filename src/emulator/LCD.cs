using static OptimeGBA.Bits;
using static OptimeGBA.CoreUtil;
using System.Runtime.CompilerServices;
using System;

namespace OptimeGBA
{
    public sealed class Background
    {
        public uint Priority = 0;
        public uint CharBaseBlock = 0;
        public bool EnableMosaic = false;
        public bool Use8BitColor = false;
        public uint MapBaseBlock = 0;
        public bool OverflowWrap = false;
        public uint ScreenSize = 0;

        byte[] BGCNTValue = new byte[2];

        public uint HorizontalOffset;
        public uint VerticalOffset;

        public uint Id;

        public uint RefPointX;
        public uint RefPointY;

        public uint AffineA;
        public uint AffineB;
        public uint AffineC;
        public uint AffineD;

        public Background(uint id)
        {
            Id = id;
        }

        public byte ReadBGCNT(uint addr)
        {
            switch (addr)
            {
                case 0x00: // BGCNT B0
                    return BGCNTValue[0];
                case 0x01: // BGCNT B1
                    return BGCNTValue[1];
            }
            return 0;
        }

        public void WriteBGCNT(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x00: // BGCNT B0
                    Priority = (uint)(val >> 0) & 0b11;
                    CharBaseBlock = (uint)(val >> 2) & 0b11;
                    EnableMosaic = BitTest(val, 6);
                    Use8BitColor = BitTest(val, 7);

                    BGCNTValue[0] = val;
                    break;
                case 0x01: // BGCNT B1
                    MapBaseBlock = (uint)(val >> 0) & 0b11111;
                    OverflowWrap = BitTest(val, 5);
                    ScreenSize = (uint)(val >> 6) & 0b11;

                    BGCNTValue[1] = val;
                    break;
            }
        }

        public byte ReadBGOFS(uint addr)
        {
            switch (addr)
            {
                case 0x0: // BGHOFS B0
                    return (byte)((HorizontalOffset & 0x0FF) >> 0);
                case 0x1: // BGHOFS B1
                    return (byte)((HorizontalOffset & 0x100) >> 8);

                case 0x2: // BGVOFS B0
                    return (byte)((VerticalOffset & 0x0FF) >> 0);
                case 0x3: // BGVOFS B1
                    return (byte)((VerticalOffset & 0x100) >> 8);
            }

            return 0;
        }

        public void WriteBGOFS(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x0: // BGHOFS B0
                    HorizontalOffset &= ~0x0FFu;
                    HorizontalOffset |= (uint)((val << 0) & 0x0FFu);
                    break;
                case 0x1: // BGHOFS B1
                    HorizontalOffset &= ~0x100u;
                    HorizontalOffset |= (uint)((val << 8) & 0x100u);
                    break;

                case 0x2: // BGVOFS B0
                    VerticalOffset &= ~0x0FFu;
                    VerticalOffset |= (uint)((val << 0) & 0x0FFu);
                    break;
                case 0x3: // BGVOFS B1
                    VerticalOffset &= ~0x100u;
                    VerticalOffset |= (uint)((val << 8) & 0x100u);
                    break;
            }
        }

        public byte ReadBGXY(uint addr)
        {
            byte offset = (byte)((addr & 3) << 8);
            switch (addr)
            {
                case 0x0: // BGX_L
                case 0x1: // BGX_L
                case 0x2: // BGX_H
                case 0x3: // BGX_H
                    return (byte)(RefPointX >> offset);

                case 0x4: // BGY_L
                case 0x5: // BGY_L
                case 0x6: // BGY_H
                case 0x7: // BGY_H
                    return (byte)(RefPointY >> offset);
            }

            return 0;
        }

        public void WriteBGXY(uint addr, byte val)
        {
            byte offset = (byte)((addr & 3) * 8);
            switch (addr)
            {
                case 0x0: // BGX_L
                case 0x1: // BGX_L
                case 0x2: // BGX_H
                case 0x3: // BGX_H
                    RefPointX &= ~(0xFFu << offset);
                    RefPointX |= (uint)(val << offset);
                    break;

                case 0x4: // BGY_L
                case 0x5: // BGY_L
                case 0x6: // BGY_H
                case 0x7: // BGY_H
                    RefPointY &= ~(0xFFu << offset);
                    RefPointY |= (uint)(val << offset);
                    break;
            }
        }
    }

    public struct ObjPixel
    {
        public byte Color;
        public byte Priority;
        public ObjMode Mode;

        public ObjPixel(byte color, byte priority, ObjMode transparent)
        {
            Color = color;
            Priority = priority;
            Mode = transparent;
        }
    }

    public enum ObjShape
    {
        Square = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    public enum ObjMode : byte
    {
        Normal = 0,
        Translucent = 1,
        ObjWindow = 2,
    }

    public enum BlendEffect
    {
        None = 0,
        Blend = 1,
        Lighten = 2,
        Darken = 3,
    }

    public enum BlendFlag
    {
        Bg0 = 1 << 0,
        Bg1 = 1 << 1,
        Bg2 = 1 << 2,
        Bg3 = 1 << 3,
        Obj = 1 << 4,
        Backdrop = 1 << 5,
    }

    public enum WindowFlag
    {
        Bg0 = 1 << 0,
        Bg1 = 1 << 1,
        Bg2 = 1 << 2,
        Bg3 = 1 << 3,
        Obj = 1 << 4,
        ColorMath = 1 << 5,
    }

    public sealed unsafe class Lcd
    {
        Gba Gba;
        Scheduler Scheduler;
        public Lcd(Gba gba, Scheduler scheduler)
        {
            Gba = gba;
            Scheduler = scheduler;

            Scheduler.AddEventRelative(SchedulerId.Lcd, 960, EndDrawingToHblank);

            for (uint i = 0; i < ScreenBufferSize; i++)
            {
                ScreenFront[i] = 0xFFFFFFFF;
                ScreenBack[i] = 0xFFFFFFFF;
            }

            Array.Fill(DebugEnableBg, true);
        }

#if DS_RESOLUTION
        public const int WIDTH = 256;
        public const int HEIGHT = 192;
#else
        public const int WIDTH = 240;
        public const int HEIGHT = 160;
#endif
        public const int BYTES_PER_PIXEL = 4;

        public bool RenderingDone = false;

        // BGCNT
        public Background[] Backgrounds = new Background[4] {
            new Background(0),
            new Background(1),
            new Background(2),
            new Background(3),
        };

        // DISPCNT
        public ushort DISPCNTValue;

        public uint BgMode;
        public bool CgbMode;
        public bool DisplayFrameSelect;
        public bool HBlankIntervalFree;
        public bool ObjCharacterVramMapping;
        public bool ForcedBlank;
        public bool[] ScreenDisplayBg = new bool[4];
        public bool ScreenDisplayObj;
        public bool Window0DisplayFlag;
        public bool Window1DisplayFlag;
        public bool ObjWindowDisplayFlag;

        public bool[] DebugEnableBg = new bool[4];
        public bool DebugEnableObj = true;
        public bool DebugEnableRendering = true;

        // WIN0H
        public byte Win0HRight;
        public byte Win0HLeft;
        // WIN1H
        public byte Win1HRight;
        public byte Win1HLeft;

        // WIN0V
        public byte Win0VBottom;
        public byte Win0VTop;

        // WIN1V
        public byte Win1VBottom;
        public byte Win1VTop;


        // WININ
        public ushort WININValue;

        public uint Win0InEnable;
        public uint Win1InEnable;

        // WINOUT
        public ushort WINOUTValue;

        public uint WinOutEnable;
        public uint WinObjEnable;

        // BLDCNT
        public ushort BLDCNTValue;

        public BlendEffect BlendEffect = 0;
        public uint Target1Flags;
        public uint Target2Flags;

        // BLDALPHA
        public uint BLDALPHAValue;

        public uint BlendACoeff;
        public uint BlendBCoeff;

        // BLDY
        public uint BlendBrightness;

        // DISPSTAT
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;

        // RGB, 24-bit
        public const int ScreenBufferSize = WIDTH * HEIGHT;
#if UNSAFE
        public uint* ScreenFront = Memory.AllocateUnmanagedArray32(ScreenBufferSize);
        public uint* ScreenBack = Memory.AllocateUnmanagedArray32(ScreenBufferSize);
#else   
        public uint[] ScreenFront = Memory.AllocateManagedArray32(ScreenBufferSize);
        public uint[] ScreenBack = Memory.AllocateManagedArray32(ScreenBufferSize);
#endif

        public uint[] ProcessedPalettes = new uint[512];
#if UNSAFE
        public byte* Palettes = Memory.AllocateUnmanagedArray(1024);
        public byte* Vram = Memory.AllocateUnmanagedArray(98304);
        public byte* Oam = Memory.AllocateUnmanagedArray(1024);
#else
        public byte[] Palettes = Memory.AllocateManagedArray(1024);
        public byte[] Vram = Memory.AllocateManagedArray(98304);
        public byte[] Oam = Memory.AllocateManagedArray(1024);
#endif

        public byte[][] BackgroundBuffers = {
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
        };

        public ObjPixel[] ObjBuffer = new ObjPixel[WIDTH];
        public byte[] ObjWindowBuffer = new byte[WIDTH];

        public uint TotalFrames;

        public uint VCount;

        public long ScanlineStartCycles;
        const uint CharBlockSize = 16384;
        const uint MapBlockSize = 2048;

        public bool ColorCorrection = true;

        // Black and white used for blending
        public uint Black = Rgb555to888(0, true);
        public byte BlackR = (byte)(Rgb555to888(0, true) >> 0);
        public byte BlackG = (byte)(Rgb555to888(0, true) >> 8);
        public byte BlackB = (byte)(Rgb555to888(0, true) >> 16);
        public uint White = Rgb555to888(0xFFFF, true);
        public byte WhiteR = (byte)(Rgb555to888(0xFFFF, true) >> 0);
        public byte WhiteG = (byte)(Rgb555to888(0xFFFF, true) >> 8);
        public byte WhiteB = (byte)(Rgb555to888(0xFFFF, true) >> 16);

        public long GetScanlineCycles()
        {
            return Scheduler.CurrentTicks - ScanlineStartCycles;
        }

        public void SwapBuffers()
        {
            var temp = ScreenBack;
            ScreenBack = ScreenFront;
            ScreenFront = temp;
        }

        public void UpdatePalette(uint pal)
        {
            byte b0 = Palettes[(pal * 2) + 0];
            byte b1 = Palettes[(pal * 2) + 1];

            ushort data = (ushort)((b1 << 8) | b0);

            ProcessedPalettes[pal] = Rgb555to888(data, ColorCorrection);
        }

        public static uint Rgb555to888(uint data, bool colorCorrection)
        {
            byte r = (byte)((data >> 0) & 0b11111);
            byte g = (byte)((data >> 5) & 0b11111);
            byte b = (byte)((data >> 10) & 0b11111);

            if (colorCorrection)
            {
                // byuu color correction, customized for my tastes
                double lcdGamma = 4.0, outGamma = 3.0;

                double lb = Math.Pow(b / 31.0, lcdGamma);
                double lg = Math.Pow(g / 31.0, lcdGamma);
                double lr = Math.Pow(r / 31.0, lcdGamma);

                byte fr = (byte)(Math.Pow((0 * lb + 10 * lg + 245 * lr) / 255, 1 / outGamma) * 0xFF);
                byte fg = (byte)(Math.Pow((20 * lb + 230 * lg + 5 * lr) / 255, 1 / outGamma) * 0xFF);
                byte fb = (byte)(Math.Pow((230 * lb + 5 * lg + 20 * lr) / 255, 1 / outGamma) * 0xFF);

                return (uint)((0xFF << 24) | (fb << 16) | (fg << 8) | (fr << 0));
            }
            else
            {
                byte fr = (byte)((255 / 31) * r);
                byte fg = (byte)((255 / 31) * g);
                byte fb = (byte)((255 / 31) * b);

                return (uint)((0xFF << 24) | (fb << 16) | (fg << 8) | (fr << 0));
            }
        }

        public void RefreshPalettes()
        {
            for (uint i = 0; i < 512; i++)
            {
                UpdatePalette(i);
            }

            Black = Rgb555to888(0, ColorCorrection);
            White = Rgb555to888(0xFFFF, ColorCorrection);
        }

        public void EnableColorCorrection()
        {
            ColorCorrection = true;
            RefreshPalettes();
        }

        public void DisableColorCorrection()
        {
            ColorCorrection = false;
            RefreshPalettes();
        }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    return (byte)(DISPCNTValue >> 0);
                case 0x4000001: // DISPCNT B1
                    return (byte)(DISPCNTValue >> 8);

                case 0x4000004: // DISPSTAT B0
                    // Vblank flag is set in scanlines 160-226, not including 227 for some reason
                    if (VCount >= 160 && VCount <= 226) val = BitSet(val, 0);
                    // Hblank flag is set at cycle 1006, not cycle 960
                    if (GetScanlineCycles() >= 1006) val = BitSet(val, 1);
                    if (VCounterMatch) val = BitSet(val, 2);
                    if (VBlankIrqEnable) val = BitSet(val, 3);
                    if (HBlankIrqEnable) val = BitSet(val, 4);
                    if (VCounterIrqEnable) val = BitSet(val, 5);
                    break;
                case 0x4000005: // DISPSTAT B1
                    val |= VCountSetting;
                    break;

                case 0x4000006: // VCOUNT B0 - B1 only exists for Nintendo DS
                    val |= (byte)VCount;
                    break;
                case 0x4000007:
                    return 0;

                case 0x4000008: // BG0CNT B0
                case 0x4000009: // BG0CNT B1
                    return Backgrounds[0].ReadBGCNT(addr - 0x4000008);
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    return Backgrounds[1].ReadBGCNT(addr - 0x400000A);
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    return Backgrounds[2].ReadBGCNT(addr - 0x400000C);
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    return Backgrounds[3].ReadBGCNT(addr - 0x400000E);

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    return Backgrounds[0].ReadBGOFS(addr - 0x4000010);
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    return Backgrounds[1].ReadBGOFS(addr - 0x4000014);
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    return Backgrounds[2].ReadBGOFS(addr - 0x4000018);
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    return Backgrounds[3].ReadBGOFS(addr - 0x400001C);

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    return Backgrounds[2].ReadBGXY(addr - 0x04000028);

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    return Backgrounds[3].ReadBGXY(addr - 0x04000038);

                case 0x4000040: // WIN0H B0
                    return Win0HRight;
                case 0x4000041: // WIN0H B1
                    return Win0HLeft;
                case 0x4000042: // WIN1H B0
                    return Win1HRight;
                case 0x4000043: // WIN1H B1
                    return Win1HLeft;

                case 0x4000044: // WIN0V B0
                    return Win0VBottom;
                case 0x4000045: // WIN0V B1
                    return Win0VTop;
                case 0x4000046: // WIN1V B0
                    return Win1VBottom;
                case 0x4000047: // WIN1V B1
                    return Win1VTop;

                case 0x4000048: // WININ B0
                    return (byte)((WININValue >> 0) & 0x3F);
                case 0x4000049: // WININ B1
                    return (byte)((WININValue >> 8) & 0x3F);

                case 0x400004A: // WINOUT B0
                    return (byte)((WINOUTValue >> 0) & 0x3F);
                case 0x400004B: // WINOUT B1
                    return (byte)((WINOUTValue >> 8) & 0x3F);

                case 0x4000050: // BLDCNT B0
                    return (byte)((BLDCNTValue >> 0) & 0xFF);
                case 0x4000051: // BLDCNT B1
                    return (byte)((BLDCNTValue >> 8) & 0x3F);

                case 0x4000052: // BLDALPHA B0
                    return (byte)(BLDALPHAValue >> 0);
                case 0x4000053: // BLDALPHA B1
                    return (byte)(BLDALPHAValue >> 8);

                case 0x4000054: // BLDY
                    return (byte)BlendBrightness;

            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    BgMode = (uint)(val & 0b111);
                    CgbMode = BitTest(val, 3);
                    DisplayFrameSelect = BitTest(val, 4);
                    HBlankIntervalFree = BitTest(val, 5);
                    ObjCharacterVramMapping = BitTest(val, 6);
                    ForcedBlank = BitTest(val, 7);

                    DISPCNTValue &= 0xFF00;
                    DISPCNTValue |= (ushort)(val << 0);
                    break;
                case 0x4000001: // DISPCNT B1
                    ScreenDisplayBg[0] = BitTest(val, 8 - 8);
                    ScreenDisplayBg[1] = BitTest(val, 9 - 8);
                    ScreenDisplayBg[2] = BitTest(val, 10 - 8);
                    ScreenDisplayBg[3] = BitTest(val, 11 - 8);
                    ScreenDisplayObj = BitTest(val, 12 - 8);
                    Window0DisplayFlag = BitTest(val, 13 - 8);
                    Window1DisplayFlag = BitTest(val, 14 - 8);
                    ObjWindowDisplayFlag = BitTest(val, 15 - 8);

                    DISPCNTValue &= 0x00FF;
                    DISPCNTValue |= (ushort)(val << 8);
                    break;

                case 0x4000004: // DISPSTAT B0
                    VBlankIrqEnable = BitTest(val, 3);
                    HBlankIrqEnable = BitTest(val, 4);
                    VCounterIrqEnable = BitTest(val, 5);
                    break;
                case 0x4000005: // DISPSTAT B1
                    VCountSetting = val;
                    break;

                case 0x4000008: // BG0CNT B0
                case 0x4000009: // BG0CNT B1
                    Backgrounds[0].WriteBGCNT(addr - 0x4000008, val);
                    break;
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    Backgrounds[1].WriteBGCNT(addr - 0x400000A, val);
                    break;
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    Backgrounds[2].WriteBGCNT(addr - 0x400000C, val);
                    break;
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    Backgrounds[3].WriteBGCNT(addr - 0x400000E, val);
                    break;

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    Backgrounds[0].WriteBGOFS(addr - 0x4000010, val);
                    break;
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    Backgrounds[1].WriteBGOFS(addr - 0x4000014, val);
                    break;
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    Backgrounds[2].WriteBGOFS(addr - 0x4000018, val);
                    break;
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    Backgrounds[3].WriteBGOFS(addr - 0x400001C, val);
                    break;

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    Backgrounds[2].WriteBGXY(addr - 0x04000028, val);
                    break;

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    Backgrounds[3].WriteBGXY(addr - 0x04000038, val);
                    break;

                case 0x4000040: // WIN0H B0
                    Win0HRight = val;
                    break;
                case 0x4000041: // WIN0H B1
                    Win0HLeft = val;
                    break;
                case 0x4000042: // WIN1H B0
                    Win1HRight = val;
                    break;
                case 0x4000043: // WIN1H B1
                    Win1HLeft = val;
                    break;

                case 0x4000044: // WIN0V B0
                    Win0VBottom = val;
                    break;
                case 0x4000045: // WIN0V B1
                    Win0VTop = val;
                    break;
                case 0x4000046: // WIN1V B0
                    Win1VBottom = val;
                    break;
                case 0x4000047: // WIN1V B1
                    Win1VTop = val;
                    break;

                case 0x4000048: // WININ B0
                    Win0InEnable = val & 0b111111U;

                    WININValue &= 0x7F00;
                    WININValue |= (ushort)(val << 0);
                    break;
                case 0x4000049: // WININ B1
                    Win1InEnable = val & 0b111111U;

                    WININValue &= 0x007F;
                    WININValue |= (ushort)(val << 8);
                    break;

                case 0x400004A: // WINOUT B0
                    WinOutEnable = val & 0b111111U;

                    WINOUTValue &= 0x7F00;
                    WINOUTValue |= (ushort)(val << 0);
                    break;
                case 0x400004B: // WINOUT B1
                    WinObjEnable = val & 0b111111U;

                    WINOUTValue &= 0x007F;
                    WINOUTValue |= (ushort)(val << 8);
                    break;

                case 0x4000050: // BLDCNT B0
                    Target1Flags = val & 0b111111U;

                    BlendEffect = (BlendEffect)((val >> 6) & 0b11U);

                    BLDCNTValue &= 0x7F00;
                    BLDCNTValue |= (ushort)(val << 0);
                    break;
                case 0x4000051: // BLDCNT B1
                    Target2Flags = val & 0b111111U;

                    BLDCNTValue &= 0x00FF;
                    BLDCNTValue |= (ushort)(val << 8);
                    break;

                case 0x4000052: // BLDALPHA B0
                    BlendACoeff = val & 0b11111U;

                    BLDALPHAValue &= 0x7F00;
                    BLDALPHAValue |= (ushort)(val << 0);
                    break;
                case 0x4000053: // BLDALPHA B1
                    BlendBCoeff = val & 0b11111U;

                    BLDALPHAValue &= 0x00FF;
                    BLDALPHAValue |= (ushort)(val << 8);
                    break;

                case 0x4000054: // BLDY
                    BlendBrightness = (byte)(val & 0b11111);
                    break;
            }
        }

        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Lcd, 272 - cyclesLate, EndHblank);

            if (DebugEnableRendering) RenderScanline();

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(Interrupt.HBlank);
            }

            Gba.Dma.RepeatHblank();
        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Lcd, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(Interrupt.HBlank);
            }
        }

        public void EndHblank(long cyclesLate)
        {
            ScanlineStartCycles = Scheduler.CurrentTicks;

            if (VCount != 227)
            {
                VCount++;
                VCounterMatch = VCount == VCountSetting;

                if (VCounterMatch && VCounterIrqEnable)
                {
                    Gba.HwControl.FlagInterrupt(Interrupt.VCounterMatch);
                }
                if (VCount > 159)
                {
                    Scheduler.AddEventRelative(SchedulerId.Lcd, 960 - cyclesLate, EndVblankToHblank);

                    if (VCount == 160)
                    {
#if DS_RESOLUTION
                        while (VCount < HEIGHT) {
                            RenderScanline();
                            VCount++;
                        }
                        VCount = 160;
#endif

                        Gba.Dma.RepeatVblank();

                        if (VBlankIrqEnable)
                        {
                            Gba.HwControl.FlagInterrupt(Interrupt.VBlank);
                        }

                        TotalFrames++;
                        SwapBuffers();

                        RenderingDone = true;
                    }
                }
                else
                {
                    Scheduler.AddEventRelative(SchedulerId.Lcd, 960 - cyclesLate, EndDrawingToHblank);
                }
            }
            else
            {
                VCount = 0;
                VCounterMatch = VCount == VCountSetting;
                if (VCounterMatch && VCounterIrqEnable)
                {
                    // Gba.HwControl.FlagInterrupt(Interrupt.VCounterMatch);
                }
                Scheduler.AddEventRelative(SchedulerId.Lcd, 960 - cyclesLate, EndDrawingToHblank);
            }
        }

        public void RenderScanline()
        {
            if (!ForcedBlank)
            {
                switch (BgMode)
                {
                    case 0:
                        RenderMode0();
                        return;
                    case 1:
                        RenderMode1();
                        return;
                    case 2:
                        RenderMode2();
                        return;
                    case 3:
                        RenderMode3();
                        return;
                    case 4:
                        RenderMode4();
                        return;
                }
            }
            else
            {
                // Render white
                uint screenBase = VCount * WIDTH;

                for (uint p = 0; p < WIDTH; p++)
                {
                    ScreenBack[screenBase] = White;
                    screenBase++;
                }
            }
        }

        public readonly static uint[] CharBlockHeightTable = {
            0, 0, // Size 0 - 256x256
            0, 0, // Size 1 - 512x256
            0, 1, // Size 2 - 256x512
            0, 2, // Size 3 - 512x512
        };
        public readonly static uint[] CharBlockWidthTable = {
            0, 0, // Size 0 - 256x256
            0, 1, // Size 1 - 512x256
            0, 0, // Size 2 - 256x512
            0, 1, // Size 3 - 512x512
        };

        public readonly static uint[] CharWidthTable = { 256, 512, 256, 512 };
        public readonly static uint[] CharHeightTable = { 256, 256, 512, 512 };


        public void RenderCharBackground(Background bg)
        {
            uint charBase = bg.CharBaseBlock * CharBlockSize;
            uint mapBase = bg.MapBaseBlock * MapBlockSize;

            uint pixelY = bg.VerticalOffset + VCount;
            uint pixelYWrapped = pixelY & 255;

            uint screenSizeBase = bg.ScreenSize * 2;
            uint verticalOffsetBlocks = CharBlockHeightTable[screenSizeBase + ((pixelY & 511) >> 8)];
            uint mapVertOffset = MapBlockSize * verticalOffsetBlocks;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            uint pixelX = bg.HorizontalOffset;
            uint lineIndex = 0;
            uint tp = pixelX & 7;

            byte[] bgBuffer = BackgroundBuffers[bg.Id];

            for (uint tile = 0; tile < WIDTH / 8 + 1; tile++)
            {
                uint pixelXWrapped = pixelX & 255;

                // 2 bytes per tile
                uint tileX = pixelXWrapped >> 3;
                uint horizontalOffsetBlocks = CharBlockWidthTable[screenSizeBase + ((pixelX & 511) >> 8)];
                uint mapHoriOffset = MapBlockSize * horizontalOffsetBlocks;
                uint mapEntryIndex = mapBase + mapVertOffset + mapHoriOffset + (tileY * 64) + (tileX * 2);
                uint mapEntry = (uint)(Vram[mapEntryIndex + 1] << 8 | Vram[mapEntryIndex]);

                uint tileNumber = mapEntry & 1023; // 10 bits
                bool xFlip = BitTest(mapEntry, 10);
                bool yFlip = BitTest(mapEntry, 11);
                // Irrelevant in 4-bit color mode
                uint palette = (mapEntry >> 12) & 15; // 4 bits

                uint realIntraTileY = intraTileY;
                if (yFlip) realIntraTileY ^= 7;

                if (bg.Use8BitColor)
                {
                    uint vramAddrTile = charBase + (tileNumber * 64) + (realIntraTileY * 8);
                    for (; tp < 8; tp++)
                    {
                        uint intraTileX = tp;
                        if (xFlip) intraTileX ^= 7;

                        // 256 color, 64 bytes per tile, 8 bytes per row
                        uint vramAddr = vramAddrTile + (intraTileX / 1);
                        byte vramValue = Vram[vramAddr];

                        byte finalColor = vramValue;
                        bgBuffer[lineIndex] = finalColor;

                        pixelX++;
                        lineIndex++;
                    }
                }
                else
                {
                    uint vramTileAddr = charBase + (tileNumber * 32) + (realIntraTileY * 4);
                    uint palettebase = (palette * 16);
                    for (; tp < 8; tp++)
                    {
                        uint intraTileX = tp;
                        if (xFlip) intraTileX ^= 7;

                        uint vramAddr = vramTileAddr + (intraTileX / 2);
                        // 16 color, 32 bytes per tile, 4 bytes per row
                        uint vramValue = Vram[vramAddr];
                        // Lower 4 bits is left pixel, upper 4 bits is right pixel
                        uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;

                        byte finalColor = (byte)(palettebase + color);
                        if (color == 0) finalColor = 0;
                        bgBuffer[lineIndex] = finalColor;

                        pixelX++;
                        lineIndex++;
                    }
                }

                tp = 0;
            }
        }

        public readonly static int[] AffineSizeShiftTable = { 7, 8, 9, 10 };
        public readonly static uint[] AffineSizeTable = { 128, 256, 512, 1024 };
        public readonly static uint[] AffineTileSizeTable = { 16, 32, 64, 128 };
        public readonly static uint[] AffineSizeMask = { 127, 255, 511, 1023 };

        public void RenderAffineBackground(Background bg)
        {
            uint xInteger = (bg.RefPointX >> 8) & 0x7FFFF;
            uint yInteger = (bg.RefPointY >> 8) & 0x7FFFF;

            uint charBase = bg.CharBaseBlock * CharBlockSize;
            uint mapBase = bg.MapBaseBlock * MapBlockSize;

            uint lineIndex = 0;

            uint pixelY = (yInteger + VCount) & AffineSizeMask[bg.ScreenSize];
            uint pixelYWrapped = pixelY & 255;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            byte[] bgBuffer = BackgroundBuffers[bg.Id];

            for (uint p = 0; p < WIDTH; p++)
            {
                uint pixelX = (xInteger + p) & AffineSizeMask[bg.ScreenSize];
                uint pixelXWrapped = pixelX & 255;

                uint tileX = pixelXWrapped >> 3;
                uint intraTileX = pixelXWrapped & 7;

                // 1 byte per tile
                uint mapEntryIndex = mapBase + (tileY * AffineTileSizeTable[bg.ScreenSize]) + (tileX * 1);
                uint tileNumber = Vram[mapEntryIndex];

                uint realIntraTileY = intraTileY;

                // Always 256color
                // 256 color, 64 bytes per tile, 8 bytes per row
                uint vramAddr = charBase + (tileNumber * 64) + (realIntraTileY * 8) + (intraTileX / 1);
                byte vramValue = Vram[vramAddr];

                byte finalColor = vramValue;
                bgBuffer[lineIndex] = finalColor;

                lineIndex++;
            }
        }

        public readonly static uint[] ObjSizeTable = {
            // Square
            8,  16, 32, 64,
            8,  16, 32, 64,

            // Rectangular 1
            16, 32, 32, 64,
            8,  8,  16, 32,

            // Rectangular 2
            8,  8,  16, 32,
            16, 32, 32, 64,

            // Invalid
            0,  0,  0,  0,
            0,  0,  0,  0,
        };

        public void RenderObjs()
        {
            // OAM address for the last sprite
            uint oamBase = 1016;
            for (int s = 127; s >= 0; s--, oamBase -= 8)
            {
                uint attr0 = (uint)(Oam[oamBase + 1] << 8 | Oam[oamBase + 0]);
                uint attr1 = (uint)(Oam[oamBase + 3] << 8 | Oam[oamBase + 2]);
                uint attr2 = (uint)(Oam[oamBase + 5] << 8 | Oam[oamBase + 4]);

                uint yPos = attr0 & 255;
                bool affine = BitTest(attr0, 8);
                ObjMode mode = (ObjMode)((attr0 >> 10) & 0b11);
                bool mosaic = BitTest(attr0, 12);
                bool use8BitColor = BitTest(attr0, 13);
                ObjShape shape = (ObjShape)((attr0 >> 14) & 0b11);

                uint xPos = attr1 & 511;
                bool xFlip = BitTest(attr1, 12) && !affine;
                bool yFlip = BitTest(attr1, 13) && !affine;

                uint objSize = (attr1 >> 14) & 0b11;

                uint tileNumber = attr2 & 1023;
                uint palette = (attr2 >> 12) & 15;

                uint xSize = ObjSizeTable[((int)shape * 8) + 0 + objSize];
                uint ySize = ObjSizeTable[((int)shape * 8) + 4 + objSize];

                int yEnd = ((int)yPos + (int)ySize) & 255;
                uint screenLineBase = xPos;

                bool disabled = BitTest(attr0, 9);

                byte priority = (byte)((attr2 >> 10) & 0b11);

                bool render = false;
                if (!disabled && !affine)
                {
                    if ((VCount >= yPos && VCount < yEnd) || (yEnd < yPos && VCount < yEnd))
                    {
                        render = true;
                    }
                }
                else if (affine)
                {
                    if (disabled)
                    {
                        yEnd += (int)ySize;
                    }

                    if ((VCount >= yPos && VCount < yEnd) || (yEnd < yPos && VCount < yEnd))
                    {
                        render = true;
                    }
                }

                if (!render) continue;

                // y relative to the object itself
                int objPixelY = (int)(VCount - yPos) & 255;

                if (yFlip)
                {
                    objPixelY = (int)ySize - objPixelY - 1;
                }

                // Tile numbers are halved in 256-color mode
                if (use8BitColor) tileNumber >>= 1;

                if (!affine)
                {
                    for (uint x = 0; x < xSize; x++)
                    {
                        if (screenLineBase < WIDTH)
                        {
                            int objPixelX = (int)x;

                            if (xFlip)
                            {
                                objPixelX = (int)(xSize - objPixelX - 1);
                            }

                            PlaceObjPixel(objPixelX, objPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
                        }
                        screenLineBase = (screenLineBase + 1) % 512;
                    }
                }
                else
                {
                    uint renderXSize = xSize;

                    bool doubleSize = BitTest(attr0, 9);
                    if (doubleSize)
                    {
                        renderXSize *= 2;
                    }

                    uint parameterId = (attr1 >> 9) & 0b11111;
                    uint pBase = parameterId * 32;

                    short pA = (short)Memory.GetUshort(Oam, pBase + 6);
                    short pB = (short)Memory.GetUshort(Oam, pBase + 14);
                    short pC = (short)Memory.GetUshort(Oam, pBase + 22);
                    short pD = (short)Memory.GetUshort(Oam, pBase + 30);

                    uint xofs;
                    uint yofs;

                    int xfofs;
                    int yfofs;

                    if (!doubleSize)
                    {
                        xofs = xSize / 2;
                        yofs = ySize / 2;

                        xfofs = 0;
                        yfofs = 0;
                    }
                    else
                    {
                        xofs = xSize;
                        yofs = ySize;

                        xfofs = -(int)xofs / 2;
                        yfofs = -(int)yofs / 2;
                    }

                    // Left edge
                    int origXEdge0 = (int)(0 - xofs);
                    int origY = (int)(objPixelY - yofs);

                    // Precalculate parameters for left and right matrix multiplications
                    int shiftedXOfs = (int)(xofs + xfofs << 8);
                    int shiftedYOfs = (int)(yofs + yfofs << 8);
                    int pBYOffset = pB * origY + shiftedXOfs;
                    int pDYOffset = pD * origY + shiftedYOfs;

                    int objPixelXEdge0 = (int)(pA * origXEdge0 + pBYOffset);
                    int objPixelYEdge0 = (int)(pC * origXEdge0 + pDYOffset);

                    // Right edge
                    int origXEdge1 = (int)(1 - xofs);
                    int objPixelXEdge1 = (int)(pA * origXEdge1 + pBYOffset);
                    int objPixelYEdge1 = (int)(pC * origXEdge1 + pDYOffset);

                    int xPerPixel = objPixelXEdge1 - objPixelXEdge0;
                    int yPerPixel = objPixelYEdge1 - objPixelYEdge0;

                    for (int x = 0; x < renderXSize; x++)
                    {
                        if (screenLineBase < WIDTH)
                        {
                            uint lerpedObjPixelX = (uint)(objPixelXEdge0 >> 8);
                            uint lerpedObjPixelY = (uint)(objPixelYEdge0 >> 8);

                            if (lerpedObjPixelX < xSize && lerpedObjPixelY < ySize)
                            {
                                PlaceObjPixel((int)lerpedObjPixelX, (int)lerpedObjPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
                            }
                        }
                        objPixelXEdge0 += xPerPixel;
                        objPixelYEdge0 += yPerPixel;

                        screenLineBase = (screenLineBase + 1) % 512;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaceObjPixel(int objX, int objY, uint tile, uint width, bool use8BitColor, uint x, uint palette, byte priority, ObjMode mode)
        {
            uint intraTileX = (uint)(objX & 7);
            uint intraTileY = (uint)(objY & 7);

            uint tileY = (uint)(objY / 8);

            const uint charBase = 0x10000;

            uint effectiveTileNumber = (uint)(tile + objX / 8);

            if (ObjCharacterVramMapping)
            {
                effectiveTileNumber += tileY * (width / 8);
            }
            else
            {
                if (use8BitColor)
                {
                    effectiveTileNumber += 16 * tileY;
                }
                else
                {
                    effectiveTileNumber += 32 * tileY;
                }
            }

            if (use8BitColor)
            {
                // 256 color, 64 bytes per tile, 8 bytes per row
                uint vramAddr = charBase + (effectiveTileNumber * 64) + (intraTileY * 8) + (intraTileX / 1);
                uint vramValue = Vram[vramAddr];

                byte finalColor = (byte)vramValue;

                if (finalColor != 0)
                {
                    if (mode != ObjMode.ObjWindow)
                    {
                        ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                    }
                    else
                    {
                        ObjWindowBuffer[x] = 1;
                    }
                }
            }
            else
            {
                // 16 color, 32 bytes per tile, 4 bytes per row
                uint vramAddr = charBase + (effectiveTileNumber * 32) + (intraTileY * 4) + (intraTileX / 2);
                uint vramValue = Vram[vramAddr];
                // Lower 4 bits is left pixel, upper 4 bits is right pixel
                uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;

                if (color != 0)
                {
                    if (mode != ObjMode.ObjWindow)
                    {
                        byte finalColor = (byte)(palette * 16 + color);
                        ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                    }
                    else
                    {
                        ObjWindowBuffer[x] = 1;
                    }
                }
            }
        }

        public void Composite()
        {
            uint screenBase = VCount * WIDTH;

            Span<int> bgList = stackalloc int[4];

            int bgCount = 0;
            for (int bg = 0; bg < 4; bg++)
            {
                // -1 means disabled
                bgList[bg] = -1;
                bgList[bgCount] = bg;
                if (ScreenDisplayBg[bg] && DebugEnableBg[bg])
                {
                    bgCount++;
                }
            }

            // Insertion sort backgrounds according to priority
            int key;
            int j;
            for (int i = 1; i < bgCount; i++)
            {
                key = (int)Backgrounds[bgList[i]].Priority;
                j = i - 1;

                while (j >= 0 && Backgrounds[bgList[j]].Priority > key)
                {
                    Swap(ref bgList[j + 1], ref bgList[j]);
                    j--;
                }
            }

            // Look up priorities for each background
            Span<uint> bgPrioList = stackalloc uint[4];
            for (int i = 0; i < bgCount; i++)
            {
                bgPrioList[i] = Backgrounds[bgList[i]].Priority;
            }

            bool win0InsideY = VCount - Win0VTop < Win0VBottom - Win0VTop;
            bool win1InsideY = VCount - Win1VTop < Win1VBottom - Win1VTop;

            uint pixel = 0;
            for (uint i = 0; i < WIDTH; i++)
            {
                uint winMask = 0b111111;

                if (ObjWindowDisplayFlag && ObjWindowBuffer[i] != 0)
                {
                    winMask = WinObjEnable;
                }
                else if (Window0DisplayFlag && win0InsideY && i - Win0HLeft < Win0HRight - Win0HLeft)
                {
                    winMask = Win0InEnable;
                }
                else if (Window1DisplayFlag && win1InsideY && i - Win1HLeft < Win1HRight - Win1HLeft)
                {
                    winMask = Win1InEnable;
                }
                else if ((DISPCNTValue & 0b1110000000000000) != 0)
                {
                    winMask = WinOutEnable;
                }

                uint hiPaletteIndex = 0;
                uint loPaletteIndex = 0;
                // Make sure sprites always draw over backdrop
                uint hiPrio = 4;
                uint loPrio = 4;
                BlendFlag hiPixelFlag = BlendFlag.Backdrop;
                BlendFlag loPixelFlag = BlendFlag.Backdrop;
                uint objPaletteIndex = ObjBuffer[i].Color + 256U;

                for (int bg = 0; bg < bgCount; bg++)
                {
                    uint color = BackgroundBuffers[bgList[bg]][i];

                    if (color != 0 && (winMask & (uint)WindowFlag.Bg0 << bgList[bg]) != 0)
                    {
                        hiPrio = loPrio;
                        loPrio = bgPrioList[bg];

                        hiPaletteIndex = loPaletteIndex;
                        loPaletteIndex = color;

                        hiPixelFlag = loPixelFlag;
                        loPixelFlag = (BlendFlag)(1 << bgList[bg]);

                        if (hiPaletteIndex != 0)
                        {
                            break;
                        }
                    }

                    if (bg == bgCount - 1)
                    {
                        hiPaletteIndex = loPaletteIndex;
                        hiPrio = loPrio;
                        hiPixelFlag = loPixelFlag;
                    }
                }

                uint effectiveTarget1Flags = Target1Flags;
                BlendEffect effectiveBlendEffect = BlendEffect;

                if (objPaletteIndex != 256 && (winMask & (uint)WindowFlag.Obj) != 0)
                {
                    if (ObjBuffer[i].Priority <= hiPrio)
                    {
                        loPaletteIndex = hiPaletteIndex;
                        hiPaletteIndex = objPaletteIndex;

                        loPixelFlag = hiPixelFlag;
                        hiPixelFlag = BlendFlag.Obj;
                    }
                    else
                    {
                        loPaletteIndex = objPaletteIndex;
                        loPixelFlag = BlendFlag.Obj;
                    }

                    if (ObjBuffer[i].Mode == ObjMode.Translucent)
                    {
                        effectiveTarget1Flags |= (uint)BlendFlag.Obj;
                        effectiveBlendEffect = BlendEffect.Blend;
                        winMask |= (uint)WindowFlag.ColorMath;
                    }
                }

                uint colorOut;
                if (
                    effectiveBlendEffect != BlendEffect.None &&
                    (effectiveTarget1Flags & (uint)hiPixelFlag) != 0 &&
                    (winMask & (uint)WindowFlag.ColorMath) != 0
                )
                {
                    uint color1 = ProcessedPalettes[hiPaletteIndex];
                    byte r1 = (byte)(color1 >> 0);
                    byte g1 = (byte)(color1 >> 8);
                    byte b1 = (byte)(color1 >> 16);

                    byte fr = r1;
                    byte fg = g1;
                    byte fb = b1;
                    switch (BlendEffect)
                    {
                        case BlendEffect.Blend:
                            if ((Target2Flags & (uint)loPixelFlag) != 0)
                            {
                                uint color2 = ProcessedPalettes[loPaletteIndex];
                                byte r2 = (byte)(color2 >> 0);
                                byte g2 = (byte)(color2 >> 8);
                                byte b2 = (byte)(color2 >> 16);

                                fr = (byte)(Math.Min(4095U, r1 * BlendACoeff + r2 * BlendBCoeff) >> 4);
                                fg = (byte)(Math.Min(4095U, g1 * BlendACoeff + g2 * BlendBCoeff) >> 4);
                                fb = (byte)(Math.Min(4095U, b1 * BlendACoeff + b2 * BlendBCoeff) >> 4);
                            }
                            break;
                        case BlendEffect.Lighten:
                            fr = (byte)(Math.Min(4095U, (r1 * 16) + WhiteR * BlendBrightness) >> 4);
                            fg = (byte)(Math.Min(4095U, (g1 * 16) + WhiteG * BlendBrightness) >> 4);
                            fb = (byte)(Math.Min(4095U, (b1 * 16) + WhiteB * BlendBrightness) >> 4);
                            break;
                        case BlendEffect.Darken:
                            fr = (byte)(Math.Max(0U, (r1 * 16) - WhiteR * BlendBrightness) >> 4);
                            fg = (byte)(Math.Max(0U, (g1 * 16) - WhiteG * BlendBrightness) >> 4);
                            fb = (byte)(Math.Max(0U, (b1 * 16) - WhiteB * BlendBrightness) >> 4);
                            break;
                    }

                    colorOut = (uint)((0xFF << 24) | (fb << 16) | (fg << 8) | (fr << 0));
                }
                else
                {
                    colorOut = ProcessedPalettes[hiPaletteIndex];
                }

                ScreenBack[screenBase++] = colorOut;

                // Use this loop as an opportunity to clear the sprite buffer
                ObjBuffer[pixel].Color = 0;
                ObjWindowBuffer[pixel++] = 0;
            }
        }

        public void RenderMode0()
        {
            RenderCharBackground(Backgrounds[3]);
            RenderCharBackground(Backgrounds[2]);
            RenderCharBackground(Backgrounds[1]);
            RenderCharBackground(Backgrounds[0]);

            if (DebugEnableObj && ScreenDisplayObj) RenderObjs();

            Composite();
        }

        public void RenderMode1()
        {
            RenderAffineBackground(Backgrounds[2]);
            RenderCharBackground(Backgrounds[1]);
            RenderCharBackground(Backgrounds[0]);

            if (DebugEnableObj && ScreenDisplayObj) RenderObjs();

            Composite();
        }

        public void RenderMode2()
        {
            RenderAffineBackground(Backgrounds[2]);
            RenderAffineBackground(Backgrounds[3]);

            if (DebugEnableObj && ScreenDisplayObj) RenderObjs();

            Composite();
        }

        public void RenderMode4()
        {
            uint screenBase = VCount * WIDTH;
            uint vramBase = 0x0 + (VCount * WIDTH);

            for (uint p = 0; p < WIDTH; p++)
            {
                uint vramVal = Vram[vramBase];

                ScreenBack[screenBase] = ProcessedPalettes[vramVal];

                vramBase++;
                screenBase++;
            }
        }

        public void RenderMode3()
        {
            uint screenBase = VCount * WIDTH;
            uint vramBase = 0x0 + (VCount * WIDTH * 2);

            for (uint p = 0; p < WIDTH; p++)
            {
                byte b0 = Vram[vramBase + 0];
                byte b1 = Vram[vramBase + 1];

                ushort data = (ushort)((b1 << 8) | b0);

                ScreenBack[screenBase] = Rgb555to888(data, ColorCorrection);

                screenBase++;
                vramBase += 2;
            }
        }
    }
}