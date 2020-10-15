using static OptimeGBA.Bits;
using System.Threading;
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

    public sealed unsafe class LCD
    {
        GBA Gba;
        Scheduler Scheduler;
        public LCD(GBA gba, Scheduler scheduler)
        {
            Gba = gba;
            Scheduler = scheduler;

            RenderThread = new Thread(RenderThreadFunction);
            RenderThread.Name = "Emulation Render Thread";
            RenderThread.Start();

            Scheduler.AddEventRelative(SchedulerId.Lcd, 960, EndDrawingToHblank);

            for (uint i = 0; i < ScreenBufferSize; i++)
            {
                ScreenFront[i] = 0xFF;
                ScreenBack[i] = 0xFF;
            }
        }

        public Thread RenderThread;
        public ManualResetEventSlim RenderThreadSync = new ManualResetEventSlim(true);
        public ManualResetEventSlim RenderThreadWait = new ManualResetEventSlim(true);
        public bool RenderingDone = false;

        // BGCNT
        public Background[] Backgrounds = new Background[4] {
            new Background(0),
            new Background(1),
            new Background(2),
            new Background(3),
        };

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

        public bool DebugEnableBg0 = true;
        public bool DebugEnableBg1 = true;
        public bool DebugEnableBg2 = true;
        public bool DebugEnableBg3 = true;
        public bool DebugEnableObj = true;

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

        public const byte WIDTH = 240;
        public const byte HEIGHT = 160;
        public const byte BYTES_PER_PIXEL = 4;

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

        public uint TotalFrames;

        public uint VCount;

        public long ScanlineStartCycles;
        const uint CharBlockBaseSize = 16384;
        const uint MapBlockBaseSize = 2048;

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

            byte r = (byte)((data >> 0) & 0b11111);
            byte g = (byte)((data >> 5) & 0b11111);
            byte b = (byte)((data >> 10) & 0b11111);

            // byuu color correction, customized for my tastes
            double lcdGamma = 4.0, outGamma = 3.0;

            double lb = Math.Pow(b / 31.0, lcdGamma);
            double lg = Math.Pow(g / 31.0, lcdGamma);
            double lr = Math.Pow(r / 31.0, lcdGamma);

            byte fr = (byte)(Math.Pow((0 * lb + 10 * lg + 245 * lr) / 255, 1 / outGamma) * 0xFF);
            byte fg = (byte)(Math.Pow((20 * lb + 230 * lg + 5 * lr) / 255, 1 / outGamma) * 0xFF);
            byte fb = (byte)(Math.Pow((230 * lb + 5 * lg + 20 * lr) / 255, 1 / outGamma) * 0xFF);

            ProcessedPalettes[pal] = (uint)((0xFF << 24) | (fb << 16) | (fg << 8) | (fr << 0));
        }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    val |= (byte)(BgMode & 0b111);
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
                    BgMode = (uint)(val & 0b111);
                    CgbMode = BitTest(val, 3);
                    DisplayFrameSelect = BitTest(val, 4);
                    HBlankIntervalFree = BitTest(val, 5);
                    ObjCharacterVramMapping = BitTest(val, 6);
                    ForcedBlank = BitTest(val, 7);
                    break;
                case 0x4000001: // DISPCNT B1
                    ScreenDisplayBg0 = BitTest(val, 8 - 8);
                    ScreenDisplayBg1 = BitTest(val, 9 - 8);
                    ScreenDisplayBg2 = BitTest(val, 10 - 8);
                    ScreenDisplayBg3 = BitTest(val, 11 - 8);
                    ScreenDisplayObj = BitTest(val, 12 - 8);
                    Window0DisplayFlag = BitTest(val, 13 - 8);
                    Window1DisplayFlag = BitTest(val, 14 - 8);
                    ObjWindowDisplayFlag = BitTest(val, 15 - 8);
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
            }
        }



        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Lcd, 272 - cyclesLate, EndHblank);

            RenderScanline();

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
                        Gba.Dma.RepeatVblank();

                        if (VBlankIrqEnable)
                        {
                            Gba.HwControl.FlagInterrupt(Interrupt.VBlank);
                        }

                        TotalFrames++;
                        SwapBuffers();
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

        public void ActivateRenderThread()
        {
            RenderingDone = false;
            RenderThreadSync.Set();
        }

        public void WaitForRenderingFinish()
        {
            if (!RenderingDone)
            {
                RenderThreadWait.Wait();
                RenderThreadWait.Reset();
            }
        }

        public void RenderThreadFunction()
        {
            while (true)
            {
                RenderThreadSync.Wait();
                RenderThreadSync.Reset();
                RenderScanline();
                RenderThreadWait.Set();
                RenderingDone = true;
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

                for (uint p = 0; p < 240; p++)
                {
                    ScreenBack[screenBase] = 0xFFFFFFFF;
                    screenBase++;
                }
            }
        }

        public readonly static int[] CharHeightShiftTable = { 8, 9, 8, 9 };

        public readonly static uint[] CharWidthTable = { 256, 512, 256, 512 };
        public readonly static uint[] CharHeightTable = { 256, 256, 512, 512 };

        public readonly static uint[] CharWidthMaskTable = { 255, 511, 255, 511 };
        public readonly static uint[] CharHeightMaskTable = { 255, 255, 511, 511 };

        public void DrawBackdropColor()
        {
            uint screenBase = VCount * WIDTH;

            for (uint p = 0; p < 240; p++)
            {
                ScreenBack[screenBase] = ProcessedPalettes[0];
                screenBase++;
            }
        }

        public void RenderCharBackground(Background bg)
        {
            uint charBase = bg.CharBaseBlock * CharBlockBaseSize;
            uint mapBase = bg.MapBaseBlock * MapBlockBaseSize;

            uint screenBase = VCount * WIDTH;

            uint pixelY = bg.VerticalOffset + VCount;
            uint pixelYWrapped = pixelY & 255;

            uint verticalOffsetBlocks = (pixelY & CharHeightMaskTable[bg.ScreenSize]) >> CharHeightShiftTable[bg.ScreenSize];
            uint mapVertOffset = 2048 * verticalOffsetBlocks;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            uint pixelX = bg.HorizontalOffset;
            uint screenPixelX = 0;
            uint tp = pixelX & 7;

            while (true)
            {
                uint pixelXWrapped = pixelX & 255;

                // 2 bytes per tile
                uint tileX = pixelXWrapped >> 3;
                uint horizontalOffsetBlocks = (pixelX & CharWidthMaskTable[bg.ScreenSize]) >> 8;
                uint mapHoriOffset = 2048 * horizontalOffsetBlocks;
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
                    for (; tp < 8; tp++)
                    {
                        uint intraTileX = tp;
                        if (xFlip) intraTileX ^= 7;

                        // 256 color, 64 bytes per tile, 8 bytes per row
                        uint vramAddr = charBase + (tileNumber * 64) + (realIntraTileY * 8) + (intraTileX / 1);
                        uint vramValue = Vram[vramAddr];

                        uint finalColor = vramValue;

                        if (finalColor != 0)
                        {
                            ScreenBack[screenBase] = ProcessedPalettes[finalColor];
                        }

                        screenBase++;

                        pixelX++;
                        screenPixelX++;
                        if (screenPixelX >= WIDTH) return;
                    }
                }
                else
                {
                    for (; tp < 8; tp++)
                    {
                        uint intraTileX = tp;
                        if (xFlip) intraTileX ^= 7;

                        // 16 color, 32 bytes per tile, 4 bytes per row
                        uint vramAddr = charBase + (tileNumber * 32) + (realIntraTileY * 4) + (intraTileX / 2);
                        uint vramValue = Vram[vramAddr];
                        // Lower 4 bits is left pixel, upper 4 bits is right pixel
                        uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;

                        uint finalColor = (palette * 16) + color;
                        if (color != 0)
                        {
                            ScreenBack[screenBase] = ProcessedPalettes[finalColor];
                        }

                        screenBase++;

                        pixelX++;
                        screenPixelX++;
                        if (screenPixelX >= WIDTH) return;
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

            uint charBase = bg.CharBaseBlock * CharBlockBaseSize;
            uint mapBase = bg.MapBaseBlock * MapBlockBaseSize;

            uint screenBase = VCount * WIDTH;

            uint pixelY = (yInteger + VCount) & AffineSizeMask[bg.ScreenSize];
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
                    ScreenBack[screenBase] = ProcessedPalettes[finalColor];
                }

                screenBase++;
            }
        }

        public uint[,] OamPriorityListIds = new uint[4, 128];
        public uint[] OamPriorityListCounts = new uint[4];

        public void ScanOam()
        {
            for (uint i = 0; i < 4; i++)
            {
                OamPriorityListCounts[i] = 0;
            }

            // OAM address for the last sprite
            uint oamBase = 1016;
            for (int s = 127; s >= 0; s--)
            {
                uint attr0 = (uint)(Oam[oamBase + 1] << 8 | Oam[oamBase + 0]);
                uint attr1High = (uint)(Oam[oamBase + 3]);
                uint attr2High = (uint)(Oam[oamBase + 5]);

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
                    if ((VCount >= yPos && VCount < yEnd) || (yEnd < yPos && VCount < yEnd))
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

        public void RenderNoneAffineObjs(uint renderPriority)
        {
            uint count = OamPriorityListCounts[renderPriority];
            for (uint i = 0; i < count; i++)
            {
                uint spriteId = OamPriorityListIds[renderPriority, i];
                uint oamBase = spriteId * 8;

                uint attr0 = (uint)(Oam[oamBase + 1] << 8 | Oam[oamBase + 0]);
                uint attr1 = (uint)(Oam[oamBase + 3] << 8 | Oam[oamBase + 2]);
                uint attr2 = (uint)(Oam[oamBase + 5] << 8 | Oam[oamBase + 4]);

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
                uint screenBase = (VCount * WIDTH);
                uint screenLineBase = xPos;

                // y relative to the object itself
                int objPixelY = ((int)VCount - (int)yPos) & 255;

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
                    if (screenLineBase < WIDTH)
                    {
                        uint objPixelX = x;
                        if (xFlip)
                        {
                            objPixelX = xSize - objPixelX - 1;
                        }

                        uint intraTileX = objPixelX & 7;

                        uint charBase = 0x10000;

                        uint effectiveTileNumber = tileNumber + objPixelX / 8;

                        if (ObjCharacterVramMapping)
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
                                ScreenBack[screenBase + screenLineBase] = ProcessedPalettes[finalColor + 256];
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
                                ScreenBack[screenBase + screenLineBase] = ProcessedPalettes[finalColor + 256];
                            }
                        }
                    }
                    screenLineBase = (screenLineBase + 1) % 512;

                    x &= 511;
                }
            }
        }

        public void RenderMode0()
        {
            ScanOam();
            DrawBackdropColor();
            for (int pri = 3; pri >= 0; pri--)
            {
                if (DebugEnableBg3 && ScreenDisplayBg3 && Backgrounds[3].Priority == pri) RenderCharBackground(Backgrounds[3]);
                if (DebugEnableBg2 && ScreenDisplayBg2 && Backgrounds[2].Priority == pri) RenderCharBackground(Backgrounds[2]);
                if (DebugEnableBg1 && ScreenDisplayBg1 && Backgrounds[1].Priority == pri) RenderCharBackground(Backgrounds[1]);
                if (DebugEnableBg0 && ScreenDisplayBg0 && Backgrounds[0].Priority == pri) RenderCharBackground(Backgrounds[0]);
                if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs((uint)pri);
            }

        }

        public void RenderMode1()
        {
            ScanOam();
            DrawBackdropColor();
            for (int pri = 3; pri >= 0; pri--)
            {
                // BG2 is affine BG
                if (DebugEnableBg2 && ScreenDisplayBg2 && Backgrounds[2].Priority == pri) RenderAffineBackground(Backgrounds[2]);
                if (DebugEnableBg1 && ScreenDisplayBg1 && Backgrounds[1].Priority == pri) RenderCharBackground(Backgrounds[1]);
                if (DebugEnableBg0 && ScreenDisplayBg0 && Backgrounds[0].Priority == pri) RenderCharBackground(Backgrounds[0]);
                if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs((uint)pri);
            }
        }

        public void RenderMode2()
        {
            ScanOam();
            DrawBackdropColor();
            if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs(3);
            if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs(2);
            if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs(1);
            if (DebugEnableObj && ScreenDisplayObj) RenderNoneAffineObjs(0);
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

                byte r = (byte)((data >> 0) & 0b11111);
                byte g = (byte)((data >> 5) & 0b11111);
                byte b = (byte)((data >> 10) & 0b11111);

                byte fr = (byte)(r * (255 / 31));
                byte fg = (byte)(g * (255 / 31));
                byte fb = (byte)(b * (255 / 31));

                ScreenBack[screenBase] = (uint)((0xFF << 24) | (fb << 16) | (fg << 8) | (fr << 0));

                screenBase++;
                vramBase += 2;
            }
        }
    }
}