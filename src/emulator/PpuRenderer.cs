using static OptimeGBA.CoreUtil;
using static OptimeGBA.Bits;
using System.Runtime.CompilerServices;
using System;

namespace OptimeGBA
{
    public sealed unsafe class PpuRenderer
    {
        public int Width;
        public int Height;
        public PpuRenderer(bool nds, int width, int height)
        {
            Width = width;
            Height = height;

            Array.Fill(DebugEnableBg, true);

            int ScreenBufferSize = Width * Height;
#if UNSAFE
            ScreenFront = MemoryUtil.AllocateUnmanagedArray32(ScreenBufferSize);
            ScreenBack = MemoryUtil.AllocateUnmanagedArray32(ScreenBufferSize);

            WinMasks = MemoryUtil.AllocateUnmanagedArray(Width);
            BgLoColor = MemoryUtil.AllocateUnmanagedArray(width);
            BgHiColor = MemoryUtil.AllocateUnmanagedArray(width);
            BgHiPrio = MemoryUtil.AllocateUnmanagedArray(width);
            BgLoPrio = MemoryUtil.AllocateUnmanagedArray(width);
            BgHiFlags = MemoryUtil.AllocateUnmanagedArray(width);
            BgLoFlags = MemoryUtil.AllocateUnmanagedArray(width);
#else 
            ScreenFront = MemoryUtil.AllocateManagedArray32(ScreenBufferSize);
            ScreenBack = MemoryUtil.AllocateManagedArray32(ScreenBufferSize);

            WinMasks = MemoryUtil.AllocateManagedArray(Width);
            BgLoColor = MemoryUtil.AllocateManagedArray(width);
            BgHiColor = MemoryUtil.AllocateManagedArray(width);
            BgHiPrio = MemoryUtil.AllocateManagedArray(width);
            BgLoPrio = MemoryUtil.AllocateManagedArray(width);
            BgHiFlags = MemoryUtil.AllocateManagedArray(width);
            BgLoFlags = MemoryUtil.AllocateManagedArray(width);
#endif
            ObjBuffer = new ObjPixel[Width];
            ObjWindowBuffer = new byte[Width];

            for (uint i = 0; i < ScreenBufferSize; i++)
            {
                ScreenFront[i] = 0xFFFFFFFF;
                ScreenBack[i] = 0xFFFFFFFF;
            }

            RefreshPalettes();

            if (!nds)
            {
                DisplayMode = 1;
            }
        }

        // Internal State
        public uint VCount;
        public const int BYTES_PER_PIXEL = 4;

        public bool RenderingDone = false;

        // RGB, 24-bit
#if UNSAFE
        public uint* ScreenFront;
        public uint* ScreenBack;
        public uint* ProcessedPalettes = MemoryUtil.AllocateUnmanagedArray32(1024);

        public byte* WinMasks;

        public byte* BgLoColor;
        public byte* BgHiColor;
        public byte* BgHiPrio;
        public byte* BgLoPrio;
        public byte* BgHiFlags;
        public byte* BgLoFlags;

        ~PpuRenderer()
        {
            MemoryUtil.FreeUnmanagedArray(ScreenFront);
            MemoryUtil.FreeUnmanagedArray(ScreenBack);
            MemoryUtil.FreeUnmanagedArray(ProcessedPalettes);
            
            MemoryUtil.FreeUnmanagedArray(WinMasks);
        }
#else
        public uint[] ScreenFront;
        public uint[] ScreenBack;
        public uint[] ProcessedPalettes = MemoryUtil.AllocateManagedArray32(1024);

        public byte[] WinMasks;

        public byte[] BgLoColor;
        public byte[] BgHiColor;
        public byte[] BgHiPrio;
        public byte[] BgLoPrio;
        public byte[] BgHiFlags;
        public byte[] BgLoFlags;
#endif

        public byte[] Palettes = MemoryUtil.AllocateManagedArray(2048); // 1024 for GBA, 2048 for NDS A and B
        public byte[] Oam = MemoryUtil.AllocateManagedArray(1024);

        public ObjPixel[] ObjBuffer;
        public byte[] ObjWindowBuffer;

        public uint TotalFrames;

        const uint CharBlockSize = 16384;
        const uint MapBlockSize = 2048;

        public bool ColorCorrection = true;

        public uint White = Rgb555to888(0xFFFF, true);

        public bool[] DebugEnableBg = new bool[4];
        public bool DebugEnableObj = true;
        public bool DebugEnableRendering = true;

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
        public bool[] ScreenDisplayBg = new bool[4];
        public bool ScreenDisplayObj;
        public bool Window0DisplayFlag;
        public bool Window1DisplayFlag;
        public bool ObjWindowDisplayFlag;
        public bool AnyWindowEnabled = false;

        // DISPCNT - NDS Exclusive
        public bool Bg0Is3D;
        public bool BitmapObjShape;
        public bool BitmapObjMapping;
        public uint DisplayMode;
        public uint LcdcVramBlock;
        public uint TileObj1DBoundary;
        public bool BitmapObj1DBoundary;
        public uint CharBaseBlockCoarse;
        public uint MapBaseBlockCoarse;
        public bool BgExtendedPalettes;
        public bool ObjExtendedPalettes;

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
        public byte Win0InEnable;
        public byte Win1InEnable;

        // WINOUT
        public byte WinOutEnable;
        public byte WinObjEnable;

        // BLDCNT
        public BlendEffect BlendEffect = 0;
        public uint Target1Flags;
        public uint Target2Flags;

        // BLDALPHA
        public uint BlendACoeff;
        public uint BlendBCoeff;

        // BLDY
        public uint BlendBrightness;

        public void RenderScanline(byte[] vram)
        {
            if (!ForcedBlank && DisplayMode != 0)
            {
                if (BgMode <= 2)
                {
                    PrepareBackgroundAndWindow();
                }

                switch (DisplayMode)
                {
                    case 1: // Regular rendering
                        switch (BgMode)
                        {
                            case 0:
                                RenderMode0(vram);
                                break;
                            case 1:
                                RenderMode1(vram);
                                break;
                            case 2:
                                RenderMode2(vram);
                                break;
                            case 3:
                                RenderMode3(vram);
                                break;
                            case 4:
                                RenderMode4(vram);
                                break;
                        }

                        if (BgMode <= 2)
                        {
                            Composite();
                            if (DebugEnableObj && ScreenDisplayObj && VCount != 159) RenderObjs(vram, VCount + 1);
                        }
                        break;
                    case 2: // LCDC Mode
                        RenderMode3(vram);
                        break;
                }
            }
            else
            {
                // Render white
                uint screenBase = (uint)(VCount * Width);

                for (uint p = 0; p < Width; p++)
                {
                    ScreenBack[screenBase] = White;
                    screenBase++;
                }
            }
        }

        public void SwapBuffers()
        {
            var temp = ScreenBack;
            ScreenBack = ScreenFront;
            ScreenFront = temp;
        }

        public bool BackgroundSettingsDirty = true;

        public void PrepareBackgroundAndWindow()
        {
            bool win0InsideY = (byte)(VCount - Win0VTop) < (byte)(Win0VBottom - Win0VTop) && Window0DisplayFlag;
            bool win1InsideY = (byte)(VCount - Win1VTop) < (byte)(Win1VBottom - Win1VTop) && Window1DisplayFlag;

            byte win0ThresholdX = (byte)(Win0HRight - Win0HLeft);
            byte win1ThresholdX = (byte)(Win1HRight - Win1HLeft);

            if (AnyWindowEnabled)
            {
                for (uint i = 0; i < Width; i++)
                {
                    byte val = WinOutEnable;

                    if (win0InsideY && ((byte)(i - Win0HLeft)) < win0ThresholdX)
                    {
                        val = Win0InEnable;
                    }
                    else if (win1InsideY && ((byte)(i - Win1HLeft)) < win1ThresholdX)
                    {
                        val = Win1InEnable;
                    }
                    else if (ObjWindowBuffer[i] != 0)
                    {
                        val = WinObjEnable;
                    }

                    WinMasks[i] = val;

                    // Also prepare backgrounds arrays in this loop
                    BgHiColor[i] = 0;
                    BgHiPrio[i] = 4;
                    BgHiFlags[i] = (byte)BlendFlag.Backdrop;
                }
            }
            else
            {
                for (uint i = 0; i < Width; i++)
                {
                    WinMasks[i] = 0b111111;

                    BgHiColor[i] = 0;
                    BgHiPrio[i] = 4;
                    BgHiFlags[i] = (byte)BlendFlag.Backdrop;
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

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public void RenderCharBackground(byte[] vram, Background bg)
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
            int tp = (int)(pixelX & 7);

            byte flag = (byte)(1 << bg.Id);

            for (uint tile = 0; tile < Width / 8 + 1; tile++)
            {
                uint pixelXWrapped = pixelX & 255;

                // 2 bytes per tile
                uint tileX = pixelXWrapped >> 3;
                uint horizontalOffsetBlocks = CharBlockWidthTable[screenSizeBase + ((pixelX & 511) >> 8)];
                uint mapHoriOffset = MapBlockSize * horizontalOffsetBlocks;
                uint mapEntryIndex = mapBase + mapVertOffset + mapHoriOffset + (tileY * 64) + (tileX * 2);
                uint mapEntry = (uint)(vram[mapEntryIndex + 1] << 8 | vram[mapEntryIndex]);

                uint tileNumber = mapEntry & 1023; // 10 bits
                bool xFlip = BitTest(mapEntry, 10);
                bool yFlip = BitTest(mapEntry, 11);

                uint effectiveIntraTileY = intraTileY;
                if (yFlip) effectiveIntraTileY ^= 7;

                // Pre-calculate loop parameters as a desperate measure to ensure performance
                int exit;
                int add;
                if (xFlip)
                {
                    exit = -1;
                    add = -1;
                    tp = 7 - tp;
                }
                else
                {
                    exit = 8;
                    add = 1;
                }

                if (bg.Use8BitColor)
                {
                    uint vramAddrTile = charBase + (tileNumber * 64) + (effectiveIntraTileY * 8);

                    for (; tp != exit; tp += add)
                    {
                        // 256 color, 64 bytes per tile, 8 bytes per row
                        uint vramAddr = (uint)(vramAddrTile + (tp / 1));
                        byte vramValue = vram[vramAddr];

                        byte finalColor = vramValue;
                        PlaceBgPixel(lineIndex, finalColor, bg.Priority, flag);

                        pixelX++;
                        lineIndex++;
                    }
                }
                else
                {
                    uint vramTileAddr = charBase + (tileNumber * 32) + (effectiveIntraTileY * 4);
                    // Irrelevant in 4-bit color mode
                    uint palette = (mapEntry >> 12) & 15; // 4 bits
                    uint palettebase = (palette * 16);

                    for (; tp != exit; tp += add)
                    {
                        uint vramAddr = (uint)(vramTileAddr + (tp / 2));
                        // 16 color, 32 bytes per tile, 4 bytes per row
                        uint vramValue = vram[vramAddr];
                        // Lower 4 bits is left pixel, upper 4 bits is right pixel
                        uint color = (vramValue >> (int)((tp & 1) * 4)) & 0xF;

                        byte finalColor = (byte)(palettebase + color);
                        if (color == 0) finalColor = 0;
                        PlaceBgPixel(lineIndex, finalColor, bg.Priority, flag);

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

        public void RenderAffineBackground(byte[] vram, Background bg)
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

            byte flag = (byte)(1 << bg.Id);

            for (uint p = 0; p < Width; p++)
            {
                uint pixelX = (xInteger + p) & AffineSizeMask[bg.ScreenSize];
                uint pixelXWrapped = pixelX & 255;

                uint tileX = pixelXWrapped >> 3;
                uint intraTileX = pixelXWrapped & 7;

                // 1 byte per tile
                uint mapEntryIndex = mapBase + (tileY * AffineTileSizeTable[bg.ScreenSize]) + (tileX * 1);
                uint tileNumber = vram[mapEntryIndex];

                uint realIntraTileY = intraTileY;

                // Always 256color
                // 256 color, 64 bytes per tile, 8 bytes per row
                uint vramAddr = charBase + (tileNumber * 64) + (realIntraTileY * 8) + (intraTileX / 1);
                byte vramValue = vram[vramAddr];

                byte finalColor = vramValue;
                PlaceBgPixel(lineIndex, finalColor, bg.Priority, flag);

                lineIndex++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaceBgPixel(uint lineIndex, byte color, byte priority, byte flag)
        {
            if (color != 0 && (WinMasks[lineIndex] & flag) != 0)
            {
                // if (priority < BgLoColor[lineIndex])
                // {
                BgLoPrio[lineIndex] = BgHiPrio[lineIndex];
                BgLoColor[lineIndex] = BgHiColor[lineIndex];
                BgLoFlags[lineIndex] = BgHiFlags[lineIndex];
                // }

                BgHiPrio[lineIndex] = priority;
                BgHiColor[lineIndex] = color;
                BgHiFlags[lineIndex] = flag;
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

        public void RenderObjs(byte[] vram, uint vcount)
        {
            // OAM address for the last sprite
            uint oamBase = 0;
            for (int s = 127; s >= 0; s--, oamBase += 8)
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
                    if ((vcount >= yPos && vcount < yEnd) || (yEnd < yPos && vcount < yEnd))
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

                    if ((vcount >= yPos && vcount < yEnd) || (yEnd < yPos && vcount < yEnd))
                    {
                        render = true;
                    }
                }

                if ((byte)mode == 3) render = false;
                if ((byte)shape == 3) render = false;

                if (!render) continue;

                // y relative to the object itself
                int objPixelY = (int)(vcount - yPos) & 255;

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
                        if (screenLineBase < Width)
                        {
                            int objPixelX = (int)x;

                            if (xFlip)
                            {
                                objPixelX = (int)(xSize - objPixelX - 1);
                            }

                            PlaceObjPixel(vram, objPixelX, objPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
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

                    short pA = (short)MemoryUtil.GetUshort(Oam, pBase + 6);
                    short pB = (short)MemoryUtil.GetUshort(Oam, pBase + 14);
                    short pC = (short)MemoryUtil.GetUshort(Oam, pBase + 22);
                    short pD = (short)MemoryUtil.GetUshort(Oam, pBase + 30);

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
                        if (screenLineBase < Width)
                        {
                            uint lerpedObjPixelX = (uint)(objPixelXEdge0 >> 8);
                            uint lerpedObjPixelY = (uint)(objPixelYEdge0 >> 8);

                            if (lerpedObjPixelX < xSize && lerpedObjPixelY < ySize)
                            {
                                PlaceObjPixel(vram, (int)lerpedObjPixelX, (int)lerpedObjPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
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
        public void PlaceObjPixel(byte[] vram, int objX, int objY, uint tile, uint width, bool use8BitColor, uint x, uint palette, byte priority, ObjMode mode)
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
                uint vramValue = vram[vramAddr];

                byte finalColor = (byte)vramValue;

                if (finalColor != 0)
                {
                    switch (mode)
                    {
                        case ObjMode.Normal:
                            if (priority < ObjBuffer[x].Priority)
                            {
                                ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                            }
                            break;
                        case ObjMode.Translucent:
                            if (priority < ObjBuffer[x].Priority)
                            {
                                ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                            }
                            ObjBuffer[x].Priority = priority;
                            break;
                        default:
                            if (ObjWindowDisplayFlag)
                            {
                                ObjWindowBuffer[x] = 1;
                            }
                            break;
                    }
                }
            }
            else
            {
                // 16 color, 32 bytes per tile, 4 bytes per row
                uint vramAddr = charBase + (effectiveTileNumber * 32) + (intraTileY * 4) + (intraTileX / 2);
                uint vramValue = vram[vramAddr];
                // Lower 4 bits is left pixel, upper 4 bits is right pixel
                uint color = (vramValue >> (int)((intraTileX & 1) * 4)) & 0xF;
                byte finalColor = (byte)(palette * 16 + color);

                if (color != 0)
                {
                    switch (mode)
                    {
                        case ObjMode.Normal:
                            if (priority < ObjBuffer[x].Priority)
                            {
                                ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                            }
                            break;
                        case ObjMode.Translucent:
                            if (priority < ObjBuffer[x].Priority)
                            {
                                ObjBuffer[x] = new ObjPixel(finalColor, priority, mode);
                            }
                            ObjBuffer[x].Priority = priority;
                            break;
                        default:
                            if (ObjWindowDisplayFlag)
                            {
                                ObjWindowBuffer[x] = 1;
                            }
                            break;
                    }
                }
            }
        }

        public void Composite()
        {
            uint screenBase = (uint)(VCount * Width);

            for (int i = 0; i < Width; i++)
            {
                uint winMask = WinMasks[i];
                var objPixel = ObjBuffer[i];

                uint hiPaletteIndex = BgHiColor[i];
                uint loPaletteIndex = BgLoColor[i];
                // Make sure sprites always draw over backdrop
                uint hiPrio = BgHiPrio[i];
                uint loPrio = BgLoPrio[i];
                BlendFlag hiPixelFlag = (BlendFlag)BgHiFlags[i];
                BlendFlag loPixelFlag = (BlendFlag)BgLoFlags[i];
                uint objPaletteIndex = objPixel.Color + 256U;

                uint effectiveTarget1Flags = Target1Flags;
                BlendEffect effectiveBlendEffect = BlendEffect;

                if (objPaletteIndex != 256 && (winMask & (uint)WindowFlag.Obj) != 0)
                {
                    if (objPixel.Priority <= hiPrio)
                    {
                        loPaletteIndex = hiPaletteIndex;
                        loPixelFlag = hiPixelFlag;

                        hiPaletteIndex = objPaletteIndex;
                        hiPixelFlag = BlendFlag.Obj;
                    }
                    else if (objPixel.Priority <= loPrio)
                    {
                        loPaletteIndex = objPaletteIndex;
                        loPixelFlag = BlendFlag.Obj;
                    }

                    if (objPixel.Mode == ObjMode.Translucent)
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
                            fr = (byte)(r1 + (((255 - r1) * BlendBrightness) >> 4));
                            fg = (byte)(g1 + (((255 - g1) * BlendBrightness) >> 4));
                            fb = (byte)(b1 + (((255 - b1) * BlendBrightness) >> 4));
                            break;
                        case BlendEffect.Darken:
                            fr = (byte)(r1 - ((r1 * BlendBrightness) >> 4));
                            fg = (byte)(g1 - ((g1 * BlendBrightness) >> 4));
                            fb = (byte)(b1 - ((b1 * BlendBrightness) >> 4));
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
                ObjBuffer[i].Color = 0;
                ObjBuffer[i].Priority = 4;
                ObjWindowBuffer[i] = 0;
            }
        }

        public bool BgIsEnabled(int id)
        {
            return ScreenDisplayBg[id] && DebugEnableBg[id];
        }

        public void RenderMode0(byte[] vram)
        {
            for (int pri = 3; pri >= 0; pri--)
            {
                if (BgIsEnabled(3) && Backgrounds[3].Priority == pri) RenderCharBackground(vram, Backgrounds[3]);
                if (BgIsEnabled(2) && Backgrounds[2].Priority == pri) RenderCharBackground(vram, Backgrounds[2]);
                if (BgIsEnabled(1) && Backgrounds[1].Priority == pri) RenderCharBackground(vram, Backgrounds[1]);
                if (BgIsEnabled(0) && Backgrounds[0].Priority == pri) RenderCharBackground(vram, Backgrounds[0]);
            }
        }

        public void RenderMode1(byte[] vram)
        {
            for (int pri = 3; pri >= 0; pri--)
            {
                if (BgIsEnabled(2) && Backgrounds[2].Priority == pri) RenderAffineBackground(vram, Backgrounds[2]);
                if (BgIsEnabled(1) && Backgrounds[1].Priority == pri) RenderCharBackground(vram, Backgrounds[1]);
                if (BgIsEnabled(0) && Backgrounds[0].Priority == pri) RenderCharBackground(vram, Backgrounds[0]);
            }
        }

        public void RenderMode2(byte[] vram)
        {
            for (int pri = 3; pri >= 0; pri--)
            {
                if (BgIsEnabled(3) && Backgrounds[3].Priority == pri) RenderAffineBackground(vram, Backgrounds[3]);
                if (BgIsEnabled(2) && Backgrounds[2].Priority == pri) RenderAffineBackground(vram, Backgrounds[2]);
            }
        }

        public void RenderMode4(byte[] vram)
        {
            uint screenBase = (uint)(VCount * Width);
            uint vramBase = (uint)(0x0 + VCount * Width);

            for (uint p = 0; p < Width; p++)
            {
                uint vramVal = vram[vramBase];

                ScreenBack[screenBase] = ProcessedPalettes[vramVal];

                vramBase++;
                screenBase++;
            }
        }

        public void RenderMode3(byte[] vram)
        {
            uint screenBase = (uint)(VCount * Width);
            uint vramBase = (uint)(VCount * Width * 2);

            for (uint p = 0; p < Width; p++)
            {
                byte b0 = vram[vramBase + 0];
                byte b1 = vram[vramBase + 1];

                ushort data = (ushort)((b1 << 8) | b0);

                ScreenBack[screenBase] = Rgb555to888(data, ColorCorrection);

                screenBase++;
                vramBase += 2;
            }
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

        public void UpdatePalette(uint pal)
        {
            byte b0 = Palettes[(pal * 2) + 0];
            byte b1 = Palettes[(pal * 2) + 1];

            ushort data = (ushort)((b1 << 8) | b0);

            ProcessedPalettes[pal] = Rgb555to888(data, ColorCorrection);
        }

        public void RefreshPalettes()
        {
            for (uint i = 0; i < 512; i++)
            {
                UpdatePalette(i);
            }

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

    }
}