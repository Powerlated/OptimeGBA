using static OptimeGBA.Bits;
using System.Threading;
using System;

namespace OptimeGBA
{
    public struct Background
    {
        public uint Priority;
        public uint CharBaseBlock;
        public bool EnableMosaic;
        public bool Use8BitColor;
        public uint MapBaseBlock;
        public bool OverflowWrap;
        public uint ScreenSize;

        byte[] BGCNTValue;

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

            Priority = 0;
            CharBaseBlock = 0;
            EnableMosaic = false;
            Use8BitColor = false;
            MapBaseBlock = 0;
            OverflowWrap = false;
            ScreenSize = 0;

            BGCNTValue = new byte[2];

            HorizontalOffset = 0;
            VerticalOffset = 0;

            Id = 0;

            RefPointX = 0;
            RefPointY = 0;

            AffineA = 0;
            AffineB = 0;
            AffineC = 0;
            AffineD = 0;
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

    public struct RenderSettings
    {
        // BGCNT
        public Background Background0;
        public Background Background1;
        public Background Background2;
        public Background Background3;

        // DISPCNT
        public uint BgMode;
        public bool CgbMode;
        public bool DisplayFrameSelect;
        public bool HBlankIntervalFree;
        public bool ObjCharacterVramMapping;
        public bool ForcedBlank;
        public bool ScreenDisplayBg0;
        public bool ScreenDisplayBg1;
        public bool ScreenDisplayBg2;
        public bool ScreenDisplayBg3;
        public bool ScreenDisplayObj;
        public bool Window0DisplayFlag;
        public bool Window1DisplayFlag;
        public bool ObjWindowDisplayFlag;

        public byte[] Oam;

        public RenderSettings(uint structHack = 0)
        {
            Background0 = new Background(0);
            Background1 = new Background(1);
            Background2 = new Background(2);
            Background3 = new Background(3);

            BgMode = 0;
            CgbMode = false;
            DisplayFrameSelect = false;
            HBlankIntervalFree = false;
            ObjCharacterVramMapping = false;
            ForcedBlank = false;
            ScreenDisplayBg0 = false;
            ScreenDisplayBg1 = false;
            ScreenDisplayBg2 = false;
            ScreenDisplayBg3 = false;
            ScreenDisplayObj = false;
            Window0DisplayFlag = false;
            Window1DisplayFlag = false;
            ObjWindowDisplayFlag = false;

            Oam = Memory.AllocateManagedArray(1024);
        }

        public RenderSettings(ref RenderSettings copy)
        {
            Background0 = copy.Background0;
            Background1 = copy.Background1;
            Background2 = copy.Background2;
            Background3 = copy.Background3;

            BgMode = copy.BgMode;
            CgbMode = copy.CgbMode;
            DisplayFrameSelect = copy.DisplayFrameSelect;
            HBlankIntervalFree = copy.HBlankIntervalFree;
            ObjCharacterVramMapping = copy.ObjCharacterVramMapping;
            ForcedBlank = copy.ForcedBlank;
            ScreenDisplayBg0 = copy.ScreenDisplayBg0;
            ScreenDisplayBg1 = copy.ScreenDisplayBg1;
            ScreenDisplayBg2 = copy.ScreenDisplayBg2;
            ScreenDisplayBg3 = copy.ScreenDisplayBg3;
            ScreenDisplayObj = copy.ScreenDisplayObj;
            Window0DisplayFlag = copy.Window0DisplayFlag;
            Window1DisplayFlag = copy.Window1DisplayFlag;
            ObjWindowDisplayFlag = copy.ObjWindowDisplayFlag;

            Oam = new byte[1024];
            copy.Oam.CopyTo(Oam, 0);
        }
    }

    public enum ObjShape
    {
        Square = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    public enum ObjMode
    {
        Square = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    public unsafe class LCD
    {
        GBA Gba;
        public LCD(GBA gba)
        {
            Gba = gba;

            ThreadSettings = new RenderSettings[HEIGHT];
            for (int i = 0; i < HEIGHT; i++)
            {
                ThreadSettings[i] = new RenderSettings(0);
            }
            ThreadLcd = this;
        }

        static LCD()
        {
            RenderThread = new Thread(RenderThreadFunction);
            RenderThread.Name = "Emulation Render Thread";
            RenderThread.Start();
        }

        public enum LCDEnum
        {
            Drawing, HBlank, VBlank
        }

        public bool UseThreadedRenderer = true;
        public static Thread RenderThread;
        public static ManualResetEventSlim RenderThreadSync = new ManualResetEventSlim(true);
        public static RenderSettings[] ThreadSettings;
        public static uint ThreadVCount;
        public static uint ThreadVCountShouldBeAt;
        public static LCD ThreadLcd;

        // RGB, 24-bit
        public byte[] ScreenFront = new byte[WIDTH * HEIGHT * BYTES_PER_PIXEL];
        public byte[] ScreenBack = new byte[WIDTH * HEIGHT * BYTES_PER_PIXEL];
        const uint WIDTH = 240;
        const uint HEIGHT = 160;
        const uint BYTES_PER_PIXEL = 3;

        public byte[,] ProcessedPalettes = new byte[512, 3];
#if DEBUG
        public byte[] Palettes = Memory.AllocateManagedArray(1024);
        public byte[] Vram = Memory.AllocateManagedArray(98304);
#else
        public byte* Palettes = Memory.AllocateUnmanagedArray(1024);
        public byte* Vram = Memory.AllocateUnmanagedArray(98304);
#endif

        public RenderSettings Settings = new RenderSettings(0);

        public uint TotalFrames;

        public uint VCount;

        public uint CycleCount;
        public LCDEnum lcdEnum;

        const uint CharBlockBaseSize = 16384;
        const uint MapBlockBaseSize = 2048;

        public bool VBlank;
        public bool HBlank;
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;

        public bool DebugEnableBg0 = true;
        public bool DebugEnableBg1 = true;
        public bool DebugEnableBg2 = true;
        public bool DebugEnableBg3 = true;
        public bool DebugEnableObj = true;

        public void SwapBuffers()
        {
            var temp = ScreenBack;
            ScreenBack = ScreenFront;
            ScreenFront = temp;

            if (UseThreadedRenderer)
            {

            }
        }

        public void UpdatePalette(uint pal)
        {
            byte b0 = Palettes[(pal * 2) + 0];
            byte b1 = Palettes[(pal * 2) + 1];

            ushort data = (ushort)((b1 << 8) | b0);

            byte r = (byte)((data >> 0) & 0b11111);
            byte g = (byte)((data >> 5) & 0b11111);
            byte b = (byte)((data >> 10) & 0b11111);

            ProcessedPalettes[pal, 0] = (byte)(r * (255 / 31));
            ProcessedPalettes[pal, 1] = (byte)(g * (255 / 31));
            ProcessedPalettes[pal, 2] = (byte)(b * (255 / 31));
        }
        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    val |= (byte)(Settings.BgMode & 0b111);
                    if (Settings.CgbMode) val = BitSet(val, 3);
                    if (Settings.DisplayFrameSelect) val = BitSet(val, 4);
                    if (Settings.HBlankIntervalFree) val = BitSet(val, 5);
                    if (Settings.ObjCharacterVramMapping) val = BitSet(val, 6);
                    if (Settings.ForcedBlank) val = BitSet(val, 7);
                    break;
                case 0x4000001: // DISPCNT B1
                    if (Settings.ScreenDisplayBg0) val = BitSet(val, 8 - 8);
                    if (Settings.ScreenDisplayBg1) val = BitSet(val, 9 - 8);
                    if (Settings.ScreenDisplayBg2) val = BitSet(val, 10 - 8);
                    if (Settings.ScreenDisplayBg3) val = BitSet(val, 11 - 8);
                    if (Settings.ScreenDisplayObj) val = BitSet(val, 12 - 8);
                    if (Settings.Window0DisplayFlag) val = BitSet(val, 13 - 8);
                    if (Settings.Window1DisplayFlag) val = BitSet(val, 14 - 8);
                    if (Settings.ObjWindowDisplayFlag) val = BitSet(val, 15 - 8);
                    break;

                case 0x4000004: // DISPSTAT B0
                    if (VBlank) val = BitSet(val, 0);
                    if (HBlank) val = BitSet(val, 1);
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
                    return Settings.Background0.ReadBGCNT(addr - 0x4000008);
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    return Settings.Background1.ReadBGCNT(addr - 0x400000A);
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    return Settings.Background2.ReadBGCNT(addr - 0x400000C);
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    return Settings.Background3.ReadBGCNT(addr - 0x400000E);

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    return Settings.Background0.ReadBGOFS(addr - 0x4000010);
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    return Settings.Background1.ReadBGOFS(addr - 0x4000014);
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    return Settings.Background2.ReadBGOFS(addr - 0x4000018);
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    return Settings.Background3.ReadBGOFS(addr - 0x400001C);

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    return Settings.Background2.ReadBGXY(addr - 0x04000028);

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    return Settings.Background3.ReadBGXY(addr - 0x04000038);

                case 0x4000050: // BLDCNT B0
                case 0x4000051: // BLDCNT B1
                    return 0;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    Settings.BgMode = (uint)(val & 0b111);
                    Settings.CgbMode = BitTest(val, 3);
                    Settings.DisplayFrameSelect = BitTest(val, 4);
                    Settings.HBlankIntervalFree = BitTest(val, 5);
                    Settings.ObjCharacterVramMapping = BitTest(val, 6);
                    Settings.ForcedBlank = BitTest(val, 7);
                    break;
                case 0x4000001: // DISPCNT B1
                    Settings.ScreenDisplayBg0 = BitTest(val, 8 - 8);
                    Settings.ScreenDisplayBg1 = BitTest(val, 9 - 8);
                    Settings.ScreenDisplayBg2 = BitTest(val, 10 - 8);
                    Settings.ScreenDisplayBg3 = BitTest(val, 11 - 8);
                    Settings.ScreenDisplayObj = BitTest(val, 12 - 8);
                    Settings.Window0DisplayFlag = BitTest(val, 13 - 8);
                    Settings.Window1DisplayFlag = BitTest(val, 14 - 8);
                    Settings.ObjWindowDisplayFlag = BitTest(val, 15 - 8);
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
                    Settings.Background0.WriteBGCNT(addr - 0x4000008, val);
                    break;
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    Settings.Background1.WriteBGCNT(addr - 0x400000A, val);
                    break;
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    Settings.Background2.WriteBGCNT(addr - 0x400000C, val);
                    break;
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    Settings.Background3.WriteBGCNT(addr - 0x400000E, val);
                    break;

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    Settings.Background0.WriteBGOFS(addr - 0x4000010, val);
                    break;
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    Settings.Background1.WriteBGOFS(addr - 0x4000014, val);
                    break;
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    Settings.Background2.WriteBGOFS(addr - 0x4000018, val);
                    break;
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    Settings.Background3.WriteBGOFS(addr - 0x400001C, val);
                    break;

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    Settings.Background2.WriteBGXY(addr - 0x04000028, val);
                    break;

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    Settings.Background3.WriteBGXY(addr - 0x04000038, val);
                    break;
            }
        }

        public void Tick(uint cycles)
        {
            // This is called every 16 cycles
            CycleCount += cycles;
            switch (lcdEnum)
            {
                case LCDEnum.Drawing:
                    {
                        if (CycleCount >= 960)
                        {
                            lcdEnum = LCDEnum.HBlank;
                            HBlank = true;

                            if (!UseThreadedRenderer)
                            {
                                RenderScanline(VCount, ref Settings);
                            }
                            else
                            {
                                // Copy data for offthread rendering
                                ThreadSettings[VCount] = new RenderSettings(ref Settings);
                                ThreadVCountShouldBeAt = VCount;
                                ActivateRenderThread();
                            }

                            if (HBlankIrqEnable)
                            {
                                Gba.HwControl.FlagInterrupt(Interrupt.HBlank);
                            }
                            Gba.Dma.RepeatHblank();
                        }
                    }
                    break;
                case LCDEnum.HBlank:
                    {
                        if (CycleCount >= 1232)
                        {
                            CycleCount -= 1232;

                            HBlank = false;

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
                                    lcdEnum = LCDEnum.VBlank;
                                    VBlank = true;

                                    if (VCount == 160)
                                    {
                                        Gba.Dma.RepeatVblank();

                                        if (VBlankIrqEnable)
                                        {
                                            Gba.HwControl.FlagInterrupt(Interrupt.VBlank);
                                        }

                                        TotalFrames++;


                                        if (!UseThreadedRenderer)
                                        {
                                            SwapBuffers();
                                        }
                                    }
                                }
                                else
                                {
                                    lcdEnum = LCDEnum.Drawing;
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
                                lcdEnum = LCDEnum.Drawing;
                                VBlank = false;

                                ThreadVCount = 0;
                                ThreadVCountShouldBeAt = 0;
                            }
                        }
                    }
                    break;
                case LCDEnum.VBlank:
                    {
                        if (CycleCount >= 960)
                        {
                            HBlank = true;
                            lcdEnum = LCDEnum.HBlank;

                            if (HBlankIrqEnable)
                            {
                                Gba.HwControl.FlagInterrupt(Interrupt.HBlank);
                            }
                        }
                    }
                    break;

            }
        }

        public void ActivateRenderThread()
        {
            RenderThreadSync.Set();
        }

        public static void RenderThreadFunction()
        {
            while (true)
            {
                RenderThreadSync.Reset();
                RenderThreadSync.Wait();

                while (ThreadVCount < ThreadVCountShouldBeAt)
                {
                    ThreadLcd.RenderScanline(ThreadVCount, ref ThreadSettings[ThreadVCount]);
                    ThreadVCount++;

                    if (ThreadVCount == HEIGHT - 1)
                    {
                        ThreadLcd.SwapBuffers();
                    }
                }

            }
        }

        public void RenderScanline(uint vCount, ref RenderSettings settings)
        {
            switch (settings.BgMode)
            {
                case 0:
                    RenderMode0(vCount, ref settings);
                    return;
                case 1:
                    RenderMode1(vCount, ref settings);
                    return;
                case 2:
                    RenderMode2(vCount, ref settings);
                    return;
                case 3:
                    RenderMode3(vCount, ref settings);
                    return;
                case 4:
                    RenderMode4(vCount, ref settings);
                    return;
            }
        }

        public readonly static int[] CharHeightShiftTable = { 8, 9, 8, 9 };

        public readonly static uint[] CharWidthTable = { 256, 512, 256, 512 };
        public readonly static uint[] CharHeightTable = { 256, 256, 512, 512 };

        public readonly static uint[] CharWidthMaskTable = { 255, 511, 255, 511 };
        public readonly static uint[] CharHeightMaskTable = { 255, 255, 511, 511 };

        public void DrawBackdropColor(uint vCount)
        {
            uint screenBase = vCount * WIDTH * BYTES_PER_PIXEL;

            for (uint p = 0; p < 240; p++)
            {
                ScreenBack[screenBase++] = ProcessedPalettes[0, 0];
                ScreenBack[screenBase++] = ProcessedPalettes[0, 1];
                ScreenBack[screenBase++] = ProcessedPalettes[0, 2];
            }
        }

        public void RenderCharBackground(uint vCount, ref Background bg)
        {
            uint charBase = bg.CharBaseBlock * CharBlockBaseSize;
            uint mapBase = bg.MapBaseBlock * MapBlockBaseSize;

            uint screenBase = vCount * WIDTH * BYTES_PER_PIXEL;

            uint pixelY = bg.VerticalOffset + vCount;
            uint pixelYWrapped = pixelY & 255;

            uint verticalOffsetBlocks = (pixelY & CharHeightMaskTable[bg.ScreenSize]) >> CharHeightShiftTable[bg.ScreenSize];
            uint mapVertOffset = 2048 * verticalOffsetBlocks;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            for (uint p = 0; p < 240; p++)
            {
                uint pixelX = bg.HorizontalOffset + p;
                uint pixelXWrapped = pixelX & 255;

                uint horizontalOffsetBlocks = (pixelX & CharWidthMaskTable[bg.ScreenSize]) >> 8;
                uint mapHoriOffset = 2048 * horizontalOffsetBlocks;

                uint tileX = pixelXWrapped >> 3;
                uint intraTileX = pixelXWrapped & 7;

                // 2 bytes per tile
                uint mapEntryIndex = mapBase + mapVertOffset + mapHoriOffset + (tileY * 64) + (tileX * 2);
                uint mapEntry = (uint)(Vram[mapEntryIndex + 1] << 8 | Vram[mapEntryIndex]);

                uint tileNumber = mapEntry & 1023; // 10 bits
                bool xFlip = BitTest(mapEntry, 10);
                bool yFlip = BitTest(mapEntry, 11);
                // Irrelevant in 4-bit color mode
                uint palette = (mapEntry >> 12) & 15; // 4 bits

                uint realIntraTileY = intraTileY;

                if (xFlip) intraTileX ^= 7;
                if (yFlip) realIntraTileY ^= 7;

                if (bg.Use8BitColor)
                {
                    // 256 color, 64 bytes per tile, 8 bytes per row
                    uint vramAddr = charBase + (tileNumber * 64) + (realIntraTileY * 8) + (intraTileX / 1);
                    uint vramValue = Vram[vramAddr];

                    uint finalColor = vramValue;

                    if (finalColor != 0)
                    {
                        ScreenBack[screenBase + 0] = ProcessedPalettes[finalColor, 0];
                        ScreenBack[screenBase + 1] = ProcessedPalettes[finalColor, 1];
                        ScreenBack[screenBase + 2] = ProcessedPalettes[finalColor, 2];
                    }
                }
                else
                {
                    // 16 color, 32 bytes per tile, 4 bytes per row
                    uint vramAddr = charBase + (tileNumber * 32) + (realIntraTileY * 4) + (intraTileX / 2);
                    uint vramValue = Vram[vramAddr];
                    // Lower 4 bits is left pixel, upper 4 bits is right pixel
                    uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;

                    uint finalColor = (palette * 16) + color;
                    if (color != 0)
                    {
                        ScreenBack[screenBase + 0] = ProcessedPalettes[finalColor, 0];
                        ScreenBack[screenBase + 1] = ProcessedPalettes[finalColor, 1];
                        ScreenBack[screenBase + 2] = ProcessedPalettes[finalColor, 2];
                    }
                }

                screenBase += 3;
            }
        }

        public readonly static int[] AffineSizeShiftTable = { 7, 8, 9, 10 };
        public readonly static uint[] AffineSizeTable = { 128, 256, 512, 1024 };
        public readonly static uint[] AffineTileSizeTable = { 16, 32, 64, 128 };
        public readonly static uint[] AffineSizeMask = { 127, 255, 511, 1023 };

        public void RenderAffineBackground(uint vCount, ref Background bg)
        {
            uint xInteger = (bg.RefPointX >> 8) & 0x7FFFF;
            uint yInteger = (bg.RefPointY >> 8) & 0x7FFFF;

            uint charBase = bg.CharBaseBlock * CharBlockBaseSize;
            uint mapBase = bg.MapBaseBlock * MapBlockBaseSize;

            uint screenBase = vCount * WIDTH * BYTES_PER_PIXEL;

            uint pixelY = (yInteger + vCount) & AffineSizeMask[bg.ScreenSize];
            uint pixelYWrapped = pixelY & 255;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            for (uint p = 0; p < 240; p++)
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
                uint vramValue = Vram[vramAddr];

                uint finalColor = vramValue;

                if (finalColor != 0)
                {
                    ScreenBack[screenBase + 0] = ProcessedPalettes[finalColor, 0];
                    ScreenBack[screenBase + 1] = ProcessedPalettes[finalColor, 1];
                    ScreenBack[screenBase + 2] = ProcessedPalettes[finalColor, 2];
                }

                screenBase += 3;
            }
        }

        public uint[,] OamPriorityListIds = new uint[4, 128];
        public uint[] OamPriorityListCounts = new uint[4];

        public void ScanOam(uint vCount, ref RenderSettings settings)
        {
            for (uint i = 0; i < 4; i++)
            {
                OamPriorityListCounts[i] = 0;
            }

            // OAM address for the last sprite
            uint oamBase = 1016;
            for (int s = 127; s >= 0; s--)
            {
                uint attr0 = (uint)(settings.Oam[oamBase + 1] << 8 | settings.Oam[oamBase + 0]);
                uint attr1High = (uint)(settings.Oam[oamBase + 3]);
                uint attr2High = (uint)(settings.Oam[oamBase + 5]);

                uint yPos = attr0 & 255;
                bool affine = BitTest(attr0, 8);
                bool disabled = BitTest(attr0, 9);
                ObjShape shape = (ObjShape)((attr0 >> 14) & 0b11);

                uint objSize = (attr1High >> 6) & 0b11;
                uint priority = (attr2High >> 2) & 0b11;

                uint ySize = 0;

                switch (shape)
                {
                    case ObjShape.Square:
                        ySize = SquareSizeTable[objSize];
                        break;
                    case ObjShape.Horizontal:
                        ySize = RectangularSide1SizeTable[objSize];
                        break;
                    case ObjShape.Vertical:
                        ySize = RectangularSide0SizeTable[objSize];
                        break;
                }

                if (!disabled || affine)
                {
                    int yEnd = ((int)yPos + (int)ySize) & 255;
                    if ((vCount >= yPos && vCount < yEnd) || (yEnd < yPos && vCount < yEnd))
                    {
                        OamPriorityListIds[priority, OamPriorityListCounts[priority]] = (uint)s;
                        OamPriorityListCounts[priority]++;
                    }
                }

                oamBase -= 8;
            }
        }

        public readonly static uint[] SquareSizeTable = { 8, 16, 32, 64 };
        public readonly static uint[] RectangularSide0SizeTable = { 16, 32, 32, 64 };
        public readonly static uint[] RectangularSide1SizeTable = { 8, 8, 16, 32 };

        public void RenderNoneAffineObjs(uint vCount, ref RenderSettings settings, uint renderPriority)
        {
            uint count = OamPriorityListCounts[renderPriority];
            for (uint i = 0; i < count; i++)
            {
                uint spriteId = OamPriorityListIds[renderPriority, i];
                uint oamBase = spriteId * 8;

                uint attr0 = (uint)(settings.Oam[oamBase + 1] << 8 | settings.Oam[oamBase + 0]);
                uint attr1 = (uint)(settings.Oam[oamBase + 3] << 8 | settings.Oam[oamBase + 2]);
                uint attr2 = (uint)(settings.Oam[oamBase + 5] << 8 | settings.Oam[oamBase + 4]);

                uint yPos = attr0 & 255;
                // bool affine = BitTest(attr0, 8);
                ObjMode mode = (ObjMode)((attr0 >> 10) & 0b11);
                bool mosaic = BitTest(attr0, 12);
                bool use8BitColor = BitTest(attr0, 13);
                ObjShape shape = (ObjShape)((attr0 >> 14) & 0b11);

                uint xPos = attr1 & 511;
                bool xFlip = BitTest(attr1, 12);
                bool yFlip = BitTest(attr1, 13);
                uint objSize = (attr1 >> 14) & 0b11;

                uint tileNumber = attr2 & 1023;
                uint palette = (attr2 >> 12) & 15;

                uint xSize = 0;
                uint ySize = 0;

                switch (shape)
                {
                    case ObjShape.Square:
                        xSize = SquareSizeTable[objSize];
                        ySize = SquareSizeTable[objSize];
                        break;
                    case ObjShape.Horizontal:
                        xSize = RectangularSide0SizeTable[objSize];
                        ySize = RectangularSide1SizeTable[objSize];
                        break;
                    case ObjShape.Vertical:
                        xSize = RectangularSide1SizeTable[objSize];
                        ySize = RectangularSide0SizeTable[objSize];
                        break;
                }

                int yEnd = ((int)yPos + (int)ySize) & 255;
                uint screenBase = (vCount * WIDTH) * BYTES_PER_PIXEL;
                uint screenLineBase = xPos * BYTES_PER_PIXEL;

                // y relative to the object itself
                int objPixelY = ((int)vCount - (int)yPos) & 255;

                if (yFlip)
                {
                    objPixelY = (int)(ySize - objPixelY - 1);
                }

                uint intraTileY = (uint)(objPixelY & 7);
                uint tileY = (uint)(objPixelY / 8);

                // Tile numbers are halved in 256-color mode
                if (use8BitColor) tileNumber >>= 1;

                for (uint x = 0; x < xSize; x++)
                {
                    if (screenLineBase < WIDTH * BYTES_PER_PIXEL)
                    {
                        uint objPixelX = x;
                        if (xFlip)
                        {
                            objPixelX = xSize - objPixelX - 1;
                        }

                        uint intraTileX = objPixelX & 7;

                        uint charBase = 0x10000;

                        uint effectiveTileNumber = tileNumber + objPixelX / 8;

                        if (settings.ObjCharacterVramMapping)
                        {
                            effectiveTileNumber += tileY * (xSize / 8);
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

                            uint finalColor = vramValue;

                            if (finalColor != 0)
                            {
                                ScreenBack[screenBase + screenLineBase + 0] = ProcessedPalettes[finalColor + 256, 0];
                                ScreenBack[screenBase + screenLineBase + 1] = ProcessedPalettes[finalColor + 256, 1];
                                ScreenBack[screenBase + screenLineBase + 2] = ProcessedPalettes[finalColor + 256, 2];
                            }
                        }
                        else
                        {
                            // 16 color, 32 bytes per tile, 4 bytes per row
                            uint vramAddr = charBase + (effectiveTileNumber * 32) + (intraTileY * 4) + (intraTileX / 2);
                            uint vramValue = Vram[vramAddr];
                            // Lower 4 bits is left pixel, upper 4 bits is right pixel
                            uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;

                            uint finalColor = (palette * 16) + color;
                            if (color != 0)
                            {
                                ScreenBack[screenBase + screenLineBase + 0] = ProcessedPalettes[finalColor + 256, 0];
                                ScreenBack[screenBase + screenLineBase + 1] = ProcessedPalettes[finalColor + 256, 1];
                                ScreenBack[screenBase + screenLineBase + 2] = ProcessedPalettes[finalColor + 256, 2];
                            }
                        }
                    }
                    screenLineBase = (screenLineBase + 3) % (512 * BYTES_PER_PIXEL);

                    x &= 511;
                }
            }
        }

        public void RenderMode0(uint vCount, ref RenderSettings settings)
        {
            ScanOam(vCount, ref settings);
            DrawBackdropColor(vCount);
            for (int pri = 3; pri >= 0; pri--)
            {
                if (DebugEnableBg3 && settings.ScreenDisplayBg3 && settings.Background3.Priority == pri) RenderCharBackground(vCount, ref settings.Background3);
                if (DebugEnableBg2 && settings.ScreenDisplayBg2 && settings.Background2.Priority == pri) RenderCharBackground(vCount, ref settings.Background2);
                if (DebugEnableBg1 && settings.ScreenDisplayBg1 && settings.Background1.Priority == pri) RenderCharBackground(vCount, ref settings.Background1);
                if (DebugEnableBg0 && settings.ScreenDisplayBg0 && settings.Background0.Priority == pri) RenderCharBackground(vCount, ref settings.Background0);
                if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, (uint)pri);
            }

        }

        public void RenderMode1(uint vCount, ref RenderSettings settings)
        {
            ScanOam(vCount, ref settings);
            DrawBackdropColor(vCount);
            for (int pri = 3; pri >= 0; pri--)
            {
                // BG2 is affine BG
                if (DebugEnableBg2 && settings.ScreenDisplayBg2 && settings.Background2.Priority == pri) RenderAffineBackground(vCount, ref settings.Background2);
                if (DebugEnableBg1 && settings.ScreenDisplayBg1 && settings.Background1.Priority == pri) RenderCharBackground(vCount, ref settings.Background1);
                if (DebugEnableBg0 && settings.ScreenDisplayBg0 && settings.Background0.Priority == pri) RenderCharBackground(vCount, ref settings.Background0);
                if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, (uint)pri);
            }
        }

        public void RenderMode2(uint vCount, ref RenderSettings settings)
        {
            ScanOam(vCount, ref settings);
            DrawBackdropColor(vCount);
            if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, 3);
            if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, 2);
            if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, 1);
            if (DebugEnableObj && settings.ScreenDisplayObj) RenderNoneAffineObjs(vCount, ref settings, 0);
        }

        public void RenderMode4(uint vCount, ref RenderSettings settings)
        {
            uint screenBase = vCount * WIDTH * BYTES_PER_PIXEL;
            uint vramBase = 0x0 + (vCount * WIDTH);

            for (uint p = 0; p < WIDTH; p++)
            {
                uint vramVal = Vram[vramBase];

                ScreenBack[screenBase + 0] = ProcessedPalettes[vramVal, 0];
                ScreenBack[screenBase + 1] = ProcessedPalettes[vramVal, 1];
                ScreenBack[screenBase + 2] = ProcessedPalettes[vramVal, 2];

                vramBase++;
                screenBase += BYTES_PER_PIXEL;
            }
        }

        public void RenderMode3(uint vCount, ref RenderSettings settings)
        {
            uint screenBase = vCount * WIDTH * BYTES_PER_PIXEL;
            uint vramBase = 0x0 + (vCount * WIDTH * 2);

            for (uint p = 0; p < WIDTH; p++)
            {
                byte b0 = Vram[vramBase + 0];
                byte b1 = Vram[vramBase + 1];

                ushort data = (ushort)((b1 << 8) | b0);

                byte r = (byte)((data >> 0) & 0b11111);
                byte g = (byte)((data >> 5) & 0b11111);
                byte b = (byte)((data >> 10) & 0b11111);

                ScreenBack[screenBase + 0] = (byte)(r * (255 / 31));
                ScreenBack[screenBase + 1] = (byte)(g * (255 / 31));
                ScreenBack[screenBase + 2] = (byte)(b * (255 / 31));

                screenBase += BYTES_PER_PIXEL;
                vramBase += 2;
            }
        }
    }
}