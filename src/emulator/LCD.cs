using static OptimeGBA.Bits;
using System.Threading;
using System;

namespace OptimeGBA
{
    public class Background
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

    public class LCD
    {
        GBA Gba;
        public LCD(GBA gba)
        {
            Gba = gba;

            RenderThread = new Thread(RenderThreadFunction);
            RenderThread.Name = "Emulation Render Thread";
            RenderThread.Start();
        }

        public enum LCDEnum
        {
            Drawing, HBlank, VBlank
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

        // DISPSTAT
        public bool VBlank;
        public bool HBlank;
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;

        // RGB, 24-bit
        public byte[] ScreenFront = new byte[240 * 160 * 3];
        public byte[] ScreenBack = new byte[240 * 160 * 3];
        const uint WIDTH = 240;
        const uint HEIGHT = 160;
        const uint BYTES_PER_PIXEL = 3;

        public byte[] Palettes = new byte[1024];
        public byte[,] ProcessedPalettes = new byte[512, 3];
        public byte[] Vram = new byte[98304];
        public byte[] Oam = new byte[1024];

        public uint TotalFrames;

        public uint VCount;

        public uint CycleCount;
        public LCDEnum lcdEnum;

        const uint CharBlockBaseSize = 16384;
        const uint MapBlockBaseSize = 2048;



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
                            RenderScanline();

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

                            // if (VCount < 160)
                            // {
                            //     WaitForRenderingFinish();
                            // }

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
                                        SwapBuffers();
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

        public readonly static uint[] CharWidthTable = { 256, 512, 256, 512 };
        public readonly static uint[] CharHeightTable = { 256, 256, 512, 512 };

        public readonly static uint[] CharWidthMaskTable = { 255, 511, 255, 511 };
        public readonly static uint[] CharHeightMaskTable = { 255, 255, 511, 511 };

        public void DrawBackdropColor()
        {
            uint screenBase = VCount * WIDTH * BYTES_PER_PIXEL;

            for (uint p = 0; p < 240; p++)
            {
                ScreenBack[screenBase++] = ProcessedPalettes[0, 0];
                ScreenBack[screenBase++] = ProcessedPalettes[0, 1];
                ScreenBack[screenBase++] = ProcessedPalettes[0, 2];
            }
        }

        public void RenderCharBackground(Background bg)
        {
            uint charBase = bg.CharBaseBlock * CharBlockBaseSize;
            uint mapBase = bg.MapBaseBlock * MapBlockBaseSize;

            uint screenBase = VCount * WIDTH * BYTES_PER_PIXEL;

            uint pixelY = (bg.VerticalOffset + VCount) & CharHeightMaskTable[bg.ScreenSize];
            uint tileY = pixelY / 8;
            uint intraTileY = pixelY % 8;

            for (uint p = 0; p < 240; p++)
            {
                uint pixelX = (bg.HorizontalOffset + p) & CharWidthMaskTable[bg.ScreenSize];

                uint tileX = pixelX / 8;
                uint intraTileX = pixelX % 8;

                // 2 bytes per tile
                uint mapEntryIndex = (tileY * 64) + (tileX * 2);
                uint mapEntry = (uint)(Vram[mapBase + mapEntryIndex + 1] << 8 | Vram[mapBase + mapEntryIndex]);

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

        public readonly static uint[] SquareSizeTable = { 8, 16, 32, 64 };
        public readonly static uint[] RectangularSide0SizeTable = { 16, 32, 32, 64 };
        public readonly static uint[] RectangularSide1SizeTable = { 8, 8, 16, 32 };

        public void RenderObjs(uint renderPriority)
        {
            // OAM address for the last sprite
            uint oamBase = 1016;
            for (uint s = 0; s < 128; s++)
            {
                uint attr2 = (uint)(Oam[oamBase + 5] << 8 | Oam[oamBase + 4]);

                uint priority = (attr2 >> 10) & 0b11;

                if (priority == renderPriority)
                {
                    uint attr0 = (uint)(Oam[oamBase + 1] << 8 | Oam[oamBase + 0]);
                    uint attr1 = (uint)(Oam[oamBase + 3] << 8 | Oam[oamBase + 2]);

                    uint yPos = attr0 & 255;
                    bool affine = BitTest(attr0, 8);
                    bool disabled = BitTest(attr0, 9);
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

                    if (!disabled && !affine)
                    {
                        int yEnd = ((int)yPos + (int)ySize) & 255;
                        uint screenBase = (VCount * WIDTH) * BYTES_PER_PIXEL;
                        uint screenLineBase = xPos * BYTES_PER_PIXEL;
                        if ((VCount >= yPos && VCount < yEnd) || (yEnd < yPos && VCount < yEnd))
                        {
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
                }

                oamBase -= 8;
            }
        }

        public void RenderMode0()
        {
            DrawBackdropColor();
            for (int pri = 3; pri >= 0; pri--)
            {
                if (ScreenDisplayBg3 && Backgrounds[3].Priority == pri) RenderCharBackground(Backgrounds[3]);
                if (ScreenDisplayBg2 && Backgrounds[2].Priority == pri) RenderCharBackground(Backgrounds[2]);
                if (ScreenDisplayBg1 && Backgrounds[1].Priority == pri) RenderCharBackground(Backgrounds[1]);
                if (ScreenDisplayBg0 && Backgrounds[0].Priority == pri) RenderCharBackground(Backgrounds[0]);
                if (ScreenDisplayObj) RenderObjs((uint)pri);
            }

        }

        public void RenderMode1()
        {
            DrawBackdropColor();
            for (int pri = 3; pri >= 0; pri--)
            {
                // BG3 is affine BG
                // if (ScreenDisplayBg2 && Backgrounds[2].Priority == pri) RenderCharBackground(Backgrounds[2]);
                if (ScreenDisplayBg1 && Backgrounds[1].Priority == pri) RenderCharBackground(Backgrounds[1]);
                if (ScreenDisplayBg0 && Backgrounds[0].Priority == pri) RenderCharBackground(Backgrounds[0]);
                if (ScreenDisplayObj) RenderObjs((uint)pri);
            }
        }

        public void RenderMode2()
        {
            DrawBackdropColor();
            if (ScreenDisplayObj) RenderObjs(3);
            if (ScreenDisplayObj) RenderObjs(2);
            if (ScreenDisplayObj) RenderObjs(1);
            if (ScreenDisplayObj) RenderObjs(0);
        }

        public void RenderMode4()
        {
            uint screenBase = VCount * WIDTH * BYTES_PER_PIXEL;
            uint vramBase = 0x0 + (VCount * WIDTH);

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

        public void RenderMode3()
        {
            uint screenBase = VCount * WIDTH * BYTES_PER_PIXEL;
            uint vramBase = 0x0 + (VCount * WIDTH * 2);

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