using static OptimeGBA.Bits;
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

    public sealed unsafe partial class Ppu
    {
        Gba Gba;
        Scheduler Scheduler;
        public Ppu(Gba gba, Scheduler scheduler)
        {
            Gba = gba;
            Scheduler = scheduler;

            Scheduler.AddEventRelative(SchedulerId.Ppu, 960, EndDrawingToHblank);

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
        public uint* ProcessedPalettes = Memory.AllocateUnmanagedArray32(512);

        public byte* Palettes = Memory.AllocateUnmanagedArray(1024);
        public byte* Vram = Memory.AllocateUnmanagedArray(98304);
        public byte* Oam = Memory.AllocateUnmanagedArray(1024);

        public byte*[] BackgroundBuffers = {
            Memory.AllocateUnmanagedArray(WIDTH + 8),
            Memory.AllocateUnmanagedArray(WIDTH + 8),
            Memory.AllocateUnmanagedArray(WIDTH + 8),
            Memory.AllocateUnmanagedArray(WIDTH + 8),
        };

        ~Ppu()
        {
            Memory.FreeUnmanagedArray(ScreenFront);
            Memory.FreeUnmanagedArray(ScreenBack);
            Memory.FreeUnmanagedArray(ProcessedPalettes);

            Memory.FreeUnmanagedArray(Palettes);
            Memory.FreeUnmanagedArray(Vram);
            Memory.FreeUnmanagedArray(Oam);

            Memory.FreeUnmanagedArray(BackgroundBuffers[0]);
            Memory.FreeUnmanagedArray(BackgroundBuffers[1]);
            Memory.FreeUnmanagedArray(BackgroundBuffers[2]);
            Memory.FreeUnmanagedArray(BackgroundBuffers[3]);
        }
#else
        public byte[] Palettes = Memory.AllocateManagedArray(1024);
        public byte[] Vram = Memory.AllocateManagedArray(98304);
        public byte[] Oam = Memory.AllocateManagedArray(1024);

        public uint[] ScreenFront = Memory.AllocateManagedArray32(ScreenBufferSize);
        public uint[] ScreenBack = Memory.AllocateManagedArray32(ScreenBufferSize);
        public uint[] ProcessedPalettes = Memory.AllocateManagedArray32(512);

        public byte[][] BackgroundBuffers = {
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
            Memory.AllocateManagedArray(WIDTH + 8),
        };
#endif

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
                double ppuGamma = 4.0, outGamma = 3.0;

                double lb = Math.Pow(b / 31.0, ppuGamma);
                double lg = Math.Pow(g / 31.0, ppuGamma);
                double lr = Math.Pow(r / 31.0, ppuGamma);

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

                    BackgroundSettingsDirty = true;
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

                    BackgroundSettingsDirty = true;
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
                    BackgroundSettingsDirty = true;
                    break;
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    Backgrounds[1].WriteBGCNT(addr - 0x400000A, val);
                    BackgroundSettingsDirty = true;
                    break;
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    Backgrounds[2].WriteBGCNT(addr - 0x400000C, val);
                    BackgroundSettingsDirty = true;
                    break;
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    Backgrounds[3].WriteBGCNT(addr - 0x400000E, val);
                    BackgroundSettingsDirty = true;
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
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (DebugEnableRendering) RenderScanline();

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(Interrupt.HBlank);
            }

            Gba.Dma.RepeatHblank();
        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

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
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndVblankToHblank);

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
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);
                }
            }
            else
            {
                VCount = 0;
                VCounterMatch = VCount == VCountSetting;
                if (VCounterMatch && VCounterIrqEnable)
                {
                    Gba.HwControl.FlagInterrupt(Interrupt.VCounterMatch);
                }
                Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);

                // Pre-render sprites for line zero
                if (DebugEnableObj && ScreenDisplayObj) RenderObjs(0);
            }
        }
    }
}