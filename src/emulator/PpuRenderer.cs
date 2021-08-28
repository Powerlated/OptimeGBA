using static OptimeGBA.CoreUtil;
using static OptimeGBA.Bits;
using System.Runtime.CompilerServices;
using System;
using static OptimeGBA.MemoryUtil;
using System.IO;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
namespace OptimeGBA
{
    public sealed unsafe class PpuRenderer
    {
        public int Width;
        public int Height;
        public Nds Nds;
        public PpuRenderer(Nds nds, int width, int height)
        {
            Width = width;
            Height = height;
            Nds = nds;

            Backgrounds = new Background[4] {
                new Background(Nds != null, 0),
                new Background(Nds != null, 1),
                new Background(Nds != null, 2),
                new Background(Nds != null, 3),
            };

            Array.Fill(DebugEnableBg, true);

            int ScreenBufferSize = Width * Height;
#if UNSAFE
            ScreenFront = MemoryUtil.AllocateUnmanagedArray16(ScreenBufferSize);
            ScreenBack = MemoryUtil.AllocateUnmanagedArray16(ScreenBufferSize);

            // 16 wide to allow tiles to poke out on each side for efficiency
            WinMasks = MemoryUtil.AllocateUnmanagedArray(Width + 16);
            BgLo = MemoryUtil.AllocateUnmanagedArray32(width + 16);
            BgHi = MemoryUtil.AllocateUnmanagedArray32(width + 16);
#else 
            ScreenFront = MemoryUtil.AllocateManagedArray16(ScreenBufferSize);
            ScreenBack = MemoryUtil.AllocateManagedArray16(ScreenBufferSize);

            WinMasks = MemoryUtil.AllocateManagedArray(width + 16);
            BgLo = MemoryUtil.AllocateManagedArray32(width + 16);
            BgHi = MemoryUtil.AllocateManagedArray32(width + 16);
#endif
            ObjBuffer = new ObjPixel[Width];
            ObjWindowBuffer = new byte[Width];

            for (uint i = 0; i < ScreenBufferSize; i++)
            {
                ScreenFront[i] = 0x7FFF;
                ScreenBack[i] = 0x7FFF;
            }

            if (nds == null)
            {
                DisplayMode = 1;
            }

            // Load 3D placeholder
            // Why do I waste time on useless crap like this
            Stream img = typeof(PpuRenderer).Assembly.GetManifestResourceStream("OptimeGBA-OpenTK.resources.3d-placeholder.raw");
            if (img == null)
            {
                img = typeof(PpuRenderer).Assembly.GetManifestResourceStream("OptimeGBA-SDL.resources.3d-placeholder.raw");
            }
            PlaceholderFor3D = new ushort[img.Length / 2];
            int val = 0;
            int index = 0;
            while (val != -1)
            {
                val = img.ReadByte();
                byte r = (byte)val;
                val = img.ReadByte();
                byte g = (byte)val;
                val = img.ReadByte();
                byte b = (byte)val;
                val = img.ReadByte();
                byte a = (byte)val;

                // Crush it to RGB555
                r >>= 3;
                g >>= 3;
                b >>= 3;

                PlaceholderFor3D[index++] = (ushort)((b << 10) | (g << 5) | r);
            }
        }

        // RGB555
        public static ushort[] PlaceholderFor3D;

        // Internal State
        public const int BYTES_PER_PIXEL = 4;

        public bool RenderingDone = false;

        // RGB, 24-bit
#if UNSAFE
        public ushort* ScreenFront;
        public ushort* ScreenBack;

        public byte* WinMasks;

        public uint* BgLo;
        public uint* BgHi;

        ~PpuRenderer()
        {
            MemoryUtil.FreeUnmanagedArray(ScreenFront);
            MemoryUtil.FreeUnmanagedArray(ScreenBack);
            
            MemoryUtil.FreeUnmanagedArray(WinMasks);
            MemoryUtil.FreeUnmanagedArray(BgLo);
            MemoryUtil.FreeUnmanagedArray(BgHi);
        }
#else
        public ushort[] ScreenFront;
        public ushort[] ScreenBack;

        public byte[] WinMasks;

        // Bytes 0-1: Color
        // Byte 2: Flag
        // Byte 3: Priority
        public uint[] BgLo;
        public uint[] BgHi;
#endif

        public byte[] Palettes = MemoryUtil.AllocateManagedArray(1024);
        public byte[] Oam = MemoryUtil.AllocateManagedArray(1024);

        public ObjPixel[] ObjBuffer;
        public byte[] ObjWindowBuffer;

        public uint TotalFrames;

        const uint CoarseBlockSize = 65536;
        const uint CharBlockSize = 16384;
        const uint MapBlockSize = 2048;
        const uint MapBlockSizeAffineNds = 16384;

        public bool[] DebugEnableBg = new bool[4];
        public bool DebugEnableObj = true;
        public bool DebugEnableRendering = true;

        public static uint[] ColorLut = GenerateRgb555To888Lut(false);
        public static uint[] ColorLutCorrected = GenerateRgb555To888Lut(true);

        public static uint[] GenerateRgb555To888Lut(bool colorCorrection)
        {
            uint[] lut = new uint[32768];
            for (uint i = 0; i < 32768; i++)
            {
                lut[i] = Rgb555To888(i, colorCorrection);
            }
            return lut;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Rgb555To888(uint data, bool colorCorrection)
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


        // BGCNT
        public Background[] Backgrounds;

        // DISPCNT
        public uint BgMode;
        public bool CgbMode;
        public bool DisplayFrameSelect;
        public bool HBlankIntervalFree;
        public bool ObjCharOneDimensional;
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

        // MOSAIC
        public uint BgMosaicX;
        public uint BgMosaicY;
        public uint ObjMosaicX;
        public uint ObjMosaicY;

        public uint BgMosaicYCounter;
        public uint ObjMosaicYCounter;

        // Raw register values
        public ushort WININValue;
        public ushort WINOUTValue;
        public ushort BLDCNTValue;
        public uint BLDALPHAValue;
        public byte MOSAICValueB0;
        public byte MOSAICValueB1;

        public void RenderScanlineGba(uint vcount, byte[] vramArr)
        {
            if (!ForcedBlank)
            {
                fixed (byte* vram = vramArr)
                {
                    if (BgMode <= 2)
                    {
                        PrepareBackgroundAndWindow(vcount);
                    }

                    switch (BgMode)
                    {
                        case 0:
                        case 1:
                        case 2:
                            RenderBgModes(vcount, vram);
                            break;
                        case 3:
                            RenderMode3(vcount, vram);
                            break;
                        case 4:
                            RenderMode4(vcount, vram);
                            break;
                    }

                    if (BgMode <= 2)
                    {
                        Composite(vcount);
                        if (DebugEnableObj && ScreenDisplayObj && vcount != 159) RenderObjs(vcount + 1, vram);
                    }
                }
            }
            else
            {
                RenderWhiteScanline(vcount);
            }
        }

        public void RenderScanlineNds(uint vcount, byte[] bgVramArr, byte[] objVramArr)
        {
            if (!ForcedBlank)
            {
                fixed (byte* bgVram = bgVramArr, objVram = objVramArr)
                {
                    switch (DisplayMode)
                    {
                        case 1: // Regular rendering
                            PrepareBackgroundAndWindow(vcount);
                            RenderBgModes(vcount, bgVram);
                            Composite(vcount);
                            if (DebugEnableObj && ScreenDisplayObj && vcount != 191) RenderObjs(vcount + 1, objVram);
                            break;
                        case 2: // LCDC Mode
                            RenderMode3(vcount, bgVram);
                            break;
                    }
                }
            }
            else
            {
                RenderWhiteScanline(vcount);
            }
        }

        public void RunVblankOperations()
        {
            Backgrounds[2].CopyAffineParams();
            Backgrounds[3].CopyAffineParams();

            BgMosaicYCounter = BgMosaicY;
            ObjMosaicYCounter = ObjMosaicY;

#if OPENTK_DEBUGGER
            PrepareBackground();
#endif
        }

        public void IncrementMosaicCounters()
        {
            if (++BgMosaicYCounter > BgMosaicY)
            {
                BgMosaicYCounter = 0;
            }

            if (++ObjMosaicYCounter > ObjMosaicY)
            {
                ObjMosaicYCounter = 0;
            }
        }

        public void SwapBuffers()
        {
            var temp = ScreenBack;
            ScreenBack = ScreenFront;
            ScreenFront = temp;
        }

        public int[] BgList = new int[4];
        public Background[] BgRefList = new Background[4];
        public uint BgCount = 0;
        public bool BackgroundSettingsDirty = true;

        public void PrepareBackgroundAndWindow(uint vcount)
        {
            if (BackgroundSettingsDirty)
            {
                PrepareBackground();
            }

            bool win0InsideY = (byte)(vcount - Win0VTop) < (byte)(Win0VBottom - Win0VTop) && Window0DisplayFlag;
            bool win1InsideY = (byte)(vcount - Win1VTop) < (byte)(Win1VBottom - Win1VTop) && Window1DisplayFlag;

            byte win0ThresholdX = (byte)(Win0HRight - Win0HLeft);
            byte win1ThresholdX = (byte)(Win1HRight - Win1HLeft);

            if (!win0InsideY) win0ThresholdX = 0;
            if (!win1InsideY) win1ThresholdX = 0;

            byte win0HPos = (byte)(-Win0HLeft);
            byte win1HPos = (byte)(-Win1HLeft);

            // Erase with priority 4, backdrop flag, and color 0;
            uint eraseColor = (uint)((4 << 24) | ((byte)BlendFlag.Backdrop << 16) | LookupPalette(0));
            if (AnyWindowEnabled)
            {
                for (uint i = 0; i < Width; i++)
                {
                    byte val = WinOutEnable;

                    if (win0HPos < win0ThresholdX)
                    {
                        val = Win0InEnable;
                    }
                    else if (win1HPos < win1ThresholdX)
                    {
                        val = Win1InEnable;
                    }
                    else if (ObjWindowBuffer[i] != 0)
                    {
                        val = WinObjEnable;
                    }

                    win0HPos++;
                    win1HPos++;

                    WinMasks[i + 8] = val;

                    // Also prepare backgrounds arrays in this loop
                    BgHi[i + 8] = eraseColor;
                }
            }
            else
            {
                for (uint i = 0; i < Width; i++)
                {
                    WinMasks[i + 8] = 0b111111;

                    BgHi[i + 8] = eraseColor;
                }
            }
        }

        public void PrepareBackground()
        {
            BgCount = 0;
            for (int bg = 0; bg < 4; bg++)
            {
                // -1 means disabled
                // Look up backgrounds in reverse order to ensure backgrounds are in correct order
                // (backgrounds carry a specific render order even if they are the same priority)
                int invBg = 3 - bg;
                BgList[bg] = -1;
                BgList[BgCount] = invBg;
                if (BgIsEnabled(invBg))
                {
                    BgCount++;
                }
            }

            // Insertion sort backgrounds according to priority
            int key;
            int j;
            for (int i = 1; i < BgCount; i++)
            {
                key = (int)Backgrounds[BgList[i]].Priority;
                j = i - 1;

                while (j >= 0 && Backgrounds[BgList[j]].Priority < key)
                {
                    Swap(ref BgList[j + 1], ref BgList[j]);
                    j--;
                }
            }

            // Look up references for each background
            for (int i = 0; i < BgCount; i++)
            {
                BgRefList[i] = Backgrounds[BgList[i]];
            }

            Backgrounds[0].Mode = BackgroundMode.Char;
            Backgrounds[1].Mode = BackgroundMode.Char;
            Backgrounds[2].Mode = BackgroundMode.Char;
            Backgrounds[3].Mode = BackgroundMode.Char;
            if (Nds == null)
            {
                switch (BgMode)
                {
                    case 1:
                        Backgrounds[2].Mode = BackgroundMode.Affine;
                        break;
                    case 2:
                        Backgrounds[2].Mode = BackgroundMode.Affine;
                        Backgrounds[3].Mode = BackgroundMode.Affine;
                        break;
                }
            }
            else
            {
                if (Bg0Is3D)
                {
                    Backgrounds[0].Mode = BackgroundMode.Display3D;
                }

                switch (BgMode)
                {
                    case 1:
                        Backgrounds[3].Mode = BackgroundMode.Affine;
                        break;
                    case 2:
                        Backgrounds[2].Mode = BackgroundMode.Affine;
                        Backgrounds[3].Mode = BackgroundMode.Affine;
                        break;
                    case 3:
                        Backgrounds[3].Mode = BackgroundMode.Extended;
                        break;
                    case 4:
                        Backgrounds[2].Mode = BackgroundMode.Affine;
                        Backgrounds[3].Mode = BackgroundMode.Extended;
                        break;
                    case 5:
                        Backgrounds[2].Mode = BackgroundMode.Extended;
                        Backgrounds[3].Mode = BackgroundMode.Extended;
                        break;
                    case 6:
                        Backgrounds[0].Mode = BackgroundMode.Display3D;
                        Backgrounds[2].Mode = BackgroundMode.Large;
                        break;
                }
            }

            // Extended mode backgrounds have extra options
            for (int i = 2; i < 4; i++)
            {
                var bg = Backgrounds[i];
                if (bg.Mode == BackgroundMode.Extended)
                {
                    if (bg.AffineBitmap)
                    {
                        if (bg.AffineBitmapFullColor)
                        {
                            bg.Mode = BackgroundMode.AffineFullColorBitmap;
                        }
                        else
                        {
                            bg.Mode = BackgroundMode.Affine256ColorBitmap;
                        }
                    }
                    else
                    {
                        // TODO: implement affine BGs with 16-bit BG map entries
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort LookupPalette(uint index)
        {
            return GetUshort(Palettes, index * 2);
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

        public void RenderCharBackground(uint vcount, byte* vram, Background bg)
        {
            bool enableMosaicX = bg.EnableMosaic && BgMosaicX != 0;
            fixed (byte* palettes = Palettes)
            {
#if UNSAFE
                if (enableMosaicX)
                {
                    _RenderCharBackground(vcount, vram, palettes, WinMasks, BgHi, BgLo, bg, true);
                }
                else
                {
                    _RenderCharBackground(vcount, vram, palettes, WinMasks, BgHi, BgLo, bg, false);
                }
#else
                fixed (byte* winMasks = WinMasks)
                {
                    fixed (uint* hi = BgHi, lo = BgLo)
                    {
                        if (enableMosaicX)
                        {
                            _RenderCharBackground(vcount, vram, palettes, winMasks, hi, lo, bg, true);
                        }
                        else
                        {
                            _RenderCharBackground(vcount, vram, palettes, winMasks, hi, lo, bg, false);
                        }
                    }
                }
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        private void _RenderCharBackground(
                uint vcount, byte* vram,
                byte* palettes,
                byte* winMasks,
                uint* hi, uint* lo,
                Background bg, bool mosaicX
            )
        {
            uint charBase = bg.CharBaseBlock * CharBlockSize + CharBaseBlockCoarse * CoarseBlockSize;
            uint mapBase = bg.MapBaseBlock * MapBlockSize + MapBaseBlockCoarse * CoarseBlockSize;

            uint pixelY = bg.VerticalOffset + vcount;
            if (bg.EnableMosaic)
            {
                pixelY -= BgMosaicYCounter;
            }
            uint pixelYWrapped = pixelY & 255;

            uint screenSizeBase = bg.ScreenSize * 2;
            uint verticalOffsetBlocks = CharBlockHeightTable[screenSizeBase + ((pixelY & 511) >> 8)];
            uint mapVertOffset = MapBlockSize * verticalOffsetBlocks;

            uint tileY = pixelYWrapped >> 3;
            uint intraTileY = pixelYWrapped & 7;

            uint pixelX = bg.HorizontalOffset;
            uint intraTileX = bg.HorizontalOffset & 7;
            uint lineIndex = 8 - intraTileX;

            uint tilesToRender = (uint)(Width / 8);
            if (lineIndex < 8) tilesToRender++;

            uint mosaicXCounter = BgMosaicX;

            // Every byte of these vectors are filled
            Vector256<int> metaVec = Vector256.Create((bg.Priority << 8) | (1 << bg.Id));

            for (uint tile = 0; tile < tilesToRender; tile++)
            {
                uint pixelXWrapped = pixelX & 255;

                // 2 bytes per tile
                uint tileX = pixelXWrapped >> 3;
                uint horizontalOffsetBlocks = CharBlockWidthTable[screenSizeBase + ((pixelX & 511) >> 8)];
                uint mapHoriOffset = MapBlockSize * horizontalOffsetBlocks;
                uint mapEntryIndex = mapBase + mapVertOffset + mapHoriOffset + tileY * 64 + tileX * 2;
                uint mapEntry = GetUshort(vram, mapEntryIndex);

                uint tileNumber = mapEntry & 1023; // 10 bits
                bool xFlip = BitTest(mapEntry, 10);
                bool yFlip = BitTest(mapEntry, 11);

                uint effectiveIntraTileY = intraTileY;
                if (yFlip) effectiveIntraTileY ^= 7;

                if (bg.Use8BitColor)
                {
                    uint vramTileAddr = charBase + tileNumber * 64 + effectiveIntraTileY * 8;
                    ulong data = GetUlong(vram, vramTileAddr);

                    if (data != 0)
                    {
                        Vector256<uint> indices = Avx2.ConvertToVector256Int32((byte*)&data).AsUInt32();
                        if (xFlip)
                        {
                            // First, reverse within 128-bit lanes
                            indices = Avx2.Shuffle(indices, 0b00_01_10_11);
                            // Then, swap upper and lower halves
                            indices = Avx2.Permute2x128(indices, indices, 1);
                        }
                        indices = Avx2.And(indices, Vector256.Create(0xFFU));

                        PlaceBgRow(lineIndex, palettes, 0, indices, metaVec, Vector256.Create(0xFFU), winMasks, hi, lo);
                    }

                    pixelX += 8;
                    lineIndex += 8;
                }
                else
                {
                    uint paletteRow = (mapEntry >> 12) & 0xF;
                    uint vramTileAddr = charBase + tileNumber * 32 + effectiveIntraTileY * 4;

                    uint data = GetUint(vram, vramTileAddr);

                    if (data != 0)
                    {
                        Vector256<uint> shifts;
                        if (xFlip)
                            shifts = Vector256.Create(28U, 24U, 20U, 16U, 12U, 8U, 4U, 0U);
                        else
                            shifts = Vector256.Create(0U, 4U, 8U, 12U, 16U, 20U, 24U, 28U);
                        Vector256<uint> indices = Vector256.Create(data);
                        indices = Avx2.ShiftRightLogicalVariable(indices, shifts);
                        indices = Avx2.And(indices, Vector256.Create(0xFU));

                        PlaceBgRow(lineIndex, palettes, paletteRow, indices, metaVec, Vector256.Create(0xFU), winMasks, hi, lo);
                    }

                    pixelX += 8;
                    lineIndex += 8;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaceBgRow(
                uint lineIndex,
                byte* palettes,
                uint paletteRow,
                Vector256<uint> indices, Vector256<int> meta, Vector256<uint> clearMask,
                byte* winMasks,
                uint* hi, uint* lo
            )
        {
            Vector256<int> color = Avx2.GatherVector256((int*)((ushort*)palettes + paletteRow * 16), indices.AsInt32(), sizeof(ushort));
            color = Avx2.And(color, Vector256.Create(0xFFFF));
            // Weave metadata (priority, ID) into color data
            color = Avx2.Or(color, Avx2.ShiftLeftLogical(meta, 16));

            Vector256<int> winMask = Avx2.ConvertToVector256Int32((byte*)(winMasks + lineIndex));
            winMask = Avx2.And(winMask, meta);
            winMask = Avx2.CompareEqual(winMask, Vector256<int>.Zero);
            // Get important color bits
            Vector256<int> clear = Avx2.And(indices, clearMask).AsInt32();
            // Are those bits clear? 
            clear = Avx2.CompareEqual(clear, Vector256<int>.Zero);
            // Merge with window mask
            winMask = Avx2.Or(winMask, clear);
            winMask = Avx2.Xor(winMask, Vector256.Create(0xFFFFFFFF).AsInt32());

            // Push back covered pixels from hi to lo
            Avx2.MaskStore((int*)(lo + lineIndex), winMask, Avx2.LoadVector256((int*)(hi + lineIndex)));
            Avx2.MaskStore((int*)(hi + lineIndex), winMask, color);
        }

        public readonly static int[] AffineSizeShiftTable = { 7, 8, 9, 10 };
        public readonly static uint[] AffineSizeTable = { 128, 256, 512, 1024 };
        public readonly static uint[] AffineTileSizeTable = { 16, 32, 64, 128 };
        public readonly static uint[] AffineSizeMask = { 127, 255, 511, 1023 };

        public void RenderAffineBackground(uint vcount, byte* vram, Background bg)
        {
            uint charBase = bg.CharBaseBlock * CharBlockSize;
            uint mapBase = bg.MapBaseBlock * MapBlockSize;

            ushort meta = (ushort)((bg.Priority << 8) | (1 << bg.Id));

            int posX = bg.AffinePosX;
            int posY = bg.AffinePosY;

            uint size = AffineSizeTable[bg.ScreenSize];
            uint sizeMask = AffineSizeMask[bg.ScreenSize];
            uint tileSize = AffineTileSizeTable[bg.ScreenSize];

            for (uint p = 0; p < Width; p++)
            {
                uint pixelX = (uint)((posX >> 8) & 0x7FFFF);
                uint pixelY = (uint)((posY >> 8) & 0x7FFFF);

                posX += bg.AffineA;
                posY += bg.AffineC;

                if (!bg.OverflowWrap && (pixelX >= size || pixelY >= size))
                {
                    continue;
                }

                pixelX &= sizeMask;
                pixelY &= sizeMask;

                uint tileX = pixelX >> 3;
                uint intraTileX = pixelX & 7;

                uint tileY = pixelY >> 3;
                uint intraTileY = pixelY & 7;

                // 1 byte per tile
                uint mapEntryIndex = mapBase + (tileY * tileSize) + (tileX * 1);
                uint tileNumber = vram[mapEntryIndex];

                // Always 256color
                // 256 color, 64 bytes per tile, 8 bytes per row
                uint vramAddr = charBase + (tileNumber * 64) + (intraTileY * 8) + (intraTileX / 1);
                byte vramValue = vram[vramAddr];

                if (vramValue != 0)
                {
                    PlaceBgPixel(p + 8, LookupPalette(vramValue), meta);
                }
            }

            bg.AffinePosX += bg.AffineB;
            bg.AffinePosY += bg.AffineD;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaceBgPixel(uint lineIndex, ushort color, ushort meta)
        {
            if ((WinMasks[lineIndex] & meta) != 0)
            {
                BgLo[lineIndex] = BgHi[lineIndex];
                BgHi[lineIndex] = (uint)(color | ((uint)meta << 16));
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

        public void RenderObjs(uint vcount, byte* vram)
        {
            // OAM address for the last sprite
            uint oamBase = 0;
            for (int s = 0; s < 128; s++, oamBase += 8)
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

                if ((byte)mode == 3 || (byte)shape == 3) render = false;

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

                            RenderObjPixel(vram, objPixelX, objPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
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

                    short pA = (short)GetUshort(Oam, pBase + 6);
                    short pB = (short)GetUshort(Oam, pBase + 14);
                    short pC = (short)GetUshort(Oam, pBase + 22);
                    short pD = (short)GetUshort(Oam, pBase + 30);

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

                    // Calculate starting parameters for matrix multiplications
                    int shiftedXOfs = (int)(xofs + xfofs << 8);
                    int shiftedYOfs = (int)(yofs + yfofs << 8);
                    int pBYOffset = pB * origY + shiftedXOfs;
                    int pDYOffset = pD * origY + shiftedYOfs;

                    int objPixelXEdge0 = (int)(pA * origXEdge0 + pBYOffset);
                    int objPixelYEdge0 = (int)(pC * origXEdge0 + pDYOffset);

                    for (int x = 0; x < renderXSize; x++)
                    {
                        if (screenLineBase < Width)
                        {
                            uint lerpedObjPixelX = (uint)(objPixelXEdge0 >> 8);
                            uint lerpedObjPixelY = (uint)(objPixelYEdge0 >> 8);

                            if (lerpedObjPixelX < xSize && lerpedObjPixelY < ySize)
                            {
                                RenderObjPixel(vram, (int)lerpedObjPixelX, (int)lerpedObjPixelY, tileNumber, xSize, use8BitColor, screenLineBase, palette, priority, mode);
                            }
                        }
                        objPixelXEdge0 += pA;
                        objPixelYEdge0 += pC;

                        screenLineBase = (screenLineBase + 1) % 512;
                    }
                }
            }
        }

        public readonly ushort[] NdsCharObjBoundary = new ushort[] { 32, 64, 128, 256 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RenderObjPixel(byte* vram, int objX, int objY, uint tile, uint width, bool use8BitColor, uint x, uint palette, byte priority, ObjMode mode)
        {
            uint intraTileX = (uint)(objX & 7);
            uint intraTileY = (uint)(objY & 7);

            uint tileX = (uint)(objX / 8);
            uint tileY = (uint)(objY / 8);

            uint charBase = Nds != null ? 0U : 0x10000U;

            tile <<= (int)TileObj1DBoundary;
            uint effectiveTileNumber = (uint)(tile + tileX);


            if (ObjCharOneDimensional)
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
                    PlaceObjPixel(x, LookupPalette(finalColor), finalColor, priority, mode);
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
                    PlaceObjPixel(x, LookupPalette(finalColor), finalColor, priority, mode);
                }

            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PlaceObjPixel(uint x, ushort color, byte paletteIndex, byte priority, ObjMode mode)
        {
            switch (mode)
            {
                case ObjMode.Normal:
                    if (priority < ObjBuffer[x].Priority)
                    {
                        ObjBuffer[x] = new ObjPixel(color, paletteIndex, priority, mode);
                    }
                    break;
                case ObjMode.Translucent:
                    if (priority < ObjBuffer[x].Priority)
                    {
                        ObjBuffer[x] = new ObjPixel(color, paletteIndex, priority, mode);
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

        public void Composite(uint vcount)
        {
            uint screenBase = (uint)(vcount * Width);

            for (int i = 0; i < Width; i++)
            {
                uint winMask = WinMasks[i + 8];
                ObjPixel objPixel = ObjBuffer[i];

                uint hi = BgHi[i + 8];
                uint lo = BgLo[i + 8];
                ushort hiColor = (ushort)hi;
                ushort loColor = (ushort)lo;
                BlendFlag hiPixelFlag = (BlendFlag)((byte)(hi >> 16));
                BlendFlag loPixelFlag = (BlendFlag)((byte)(lo >> 16));
                uint objPaletteIndex = objPixel.PaletteIndex + 256U;

                uint effectiveTarget1Flags = Target1Flags;
                BlendEffect effectiveBlendEffect = BlendEffect;

                if (objPaletteIndex != 256 && (winMask & (uint)WindowFlag.Obj) != 0)
                {
                    byte hiPrio = (byte)(hi >> 24);
                    byte loPrio = (byte)(lo >> 24);

                    if (objPixel.Priority <= hiPrio)
                    {
                        loColor = hiColor;
                        loPixelFlag = hiPixelFlag;

                        hiColor = LookupPalette(objPaletteIndex);
                        hiPixelFlag = BlendFlag.Obj;
                    }
                    else if (objPixel.Priority <= loPrio)
                    {
                        loColor = LookupPalette(objPaletteIndex);
                        loPixelFlag = BlendFlag.Obj;
                    }

                    if (objPixel.Mode == ObjMode.Translucent)
                    {
                        effectiveTarget1Flags |= (uint)BlendFlag.Obj;
                        effectiveBlendEffect = BlendEffect.Blend;
                        winMask |= (uint)WindowFlag.ColorMath;
                    }
                }

                if (
                    effectiveBlendEffect != BlendEffect.None &&
                    (effectiveTarget1Flags & (uint)hiPixelFlag) != 0 &&
                    (winMask & (uint)WindowFlag.ColorMath) != 0
                )
                {
                    byte r1 = (byte)((hiColor >> 0) & 0x1F);
                    byte g1 = (byte)((hiColor >> 5) & 0x1F);
                    byte b1 = (byte)((hiColor >> 10) & 0x1F);

                    byte fr = r1;
                    byte fg = g1;
                    byte fb = b1;
                    switch (BlendEffect)
                    {
                        case BlendEffect.Blend:
                            if ((Target2Flags & (uint)loPixelFlag) != 0)
                            {
                                byte r2 = (byte)((loColor >> 0) & 0x1F);
                                byte g2 = (byte)((loColor >> 5) & 0x1F);
                                byte b2 = (byte)((loColor >> 10) & 0x1F);

                                fr = (byte)((Math.Min(511U, r1 * BlendACoeff + r2 * BlendBCoeff) >> 4) & 0x1FU);
                                fg = (byte)((Math.Min(511U, g1 * BlendACoeff + g2 * BlendBCoeff) >> 4) & 0x1FU);
                                fb = (byte)((Math.Min(511U, b1 * BlendACoeff + b2 * BlendBCoeff) >> 4) & 0x1FU);
                            }
                            break;
                        case BlendEffect.Lighten:
                            fr = (byte)((r1 + (((31 - r1) * BlendBrightness) >> 4)) & 0x1FU);
                            fg = (byte)((g1 + (((31 - g1) * BlendBrightness) >> 4)) & 0x1FU);
                            fb = (byte)((b1 + (((31 - b1) * BlendBrightness) >> 4)) & 0x1FU);
                            break;
                        case BlendEffect.Darken:
                            fr = (byte)((r1 - ((r1 * BlendBrightness) >> 4)) & 0x1FU);
                            fg = (byte)((g1 - ((g1 * BlendBrightness) >> 4)) & 0x1FU);
                            fb = (byte)((b1 - ((b1 * BlendBrightness) >> 4)) & 0x1FU);
                            break;
                    }

                    ScreenBack[screenBase++] = (ushort)((fb << 10) | (fg << 5) | fr);
                }
                else
                {
                    ScreenBack[screenBase++] = hiColor;
                }

                // It's the frontend's responsibility to convert rgb555 to rgb888

                // Use this loop as an opportunity to clear the sprite buffer
                ObjBuffer[i].Color = 0;
                ObjBuffer[i].PaletteIndex = 0;
                ObjBuffer[i].Priority = 4;
                ObjWindowBuffer[i] = 0;
            }
        }

        public bool BgIsEnabled(int id)
        {
            if (Nds == null)
            {
                switch (BgMode)
                {
                    case 1:
                        if (id == 3) return false;
                        break;
                    case 2:
                        if (id == 0) return false;
                        if (id == 1) return false;
                        break;
                }
            }
            else
            {
                if (BgMode == 6 && (id == 0 || id == 2)) return false;
            }

            return ScreenDisplayBg[id] && DebugEnableBg[id];
        }

        public void RenderBgModes(uint vcount, byte* vram)
        {
            for (uint i = 0; i < BgCount; i++)
            {
                var bg = BgRefList[i];
                switch (bg.Mode)
                {
                    case BackgroundMode.Char:
                        RenderCharBackground(vcount, vram, bg);
                        break;
                    case BackgroundMode.Affine:
                        RenderAffineBackground(vcount, vram, bg);
                        break;
                    case BackgroundMode.AffineFullColorBitmap:
                        RenderAffineBitmapBackground(vcount, vram, bg, true);
                        break;
                    case BackgroundMode.Affine256ColorBitmap:
                        RenderAffineBitmapBackground(vcount, vram, bg, false);
                        break;
                    case BackgroundMode.Display3D:
                        Render3DBackground(vcount, vram, bg);
                        break;
                }
            }
        }

        public void Render3DBackground(uint vcount, byte* vram, Background bg)
        {
            uint srcBase = (uint)(vcount * Width);

            ushort meta = (ushort)((bg.Priority << 8) | (1 << bg.Id));

            for (uint i = 0; i < Width; i++)
            {
                PlaceBgPixel(i + 8, Nds.Ppu3D.Screen[srcBase + i], meta);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RenderAffineBitmapBackground(uint vcount, byte* vram, Background bg, bool fullColor)
        {
            // TODO: actually implement rendering affine here
            // TODO: implement 256-color mode (no fullColor)
            uint screenBase = (uint)(vcount * Width);
            uint vramBase = (uint)(vcount * Width * 2) + bg.MapBaseBlock * MapBlockSizeAffineNds;

            byte flag = (byte)(1 << bg.Id);
            ushort meta = (ushort)((bg.Priority << 8) | flag);

            for (uint p = 0; p < Width; p++)
            {
                ushort data = GetUshort(vram, vramBase);

                PlaceBgPixel(p + 8, data, meta);

                screenBase++;
                vramBase += 2;
            }
        }

        public void RenderMode4(uint vcount, byte* vram)
        {
            uint screenBase = (uint)(vcount * Width);
            uint vramBase = (uint)(0x0 + vcount * Width);

            for (uint p = 0; p < Width; p++)
            {
                uint vramVal = vram[vramBase];

                ScreenBack[screenBase] = LookupPalette(vramVal);

                vramBase++;
                screenBase++;
            }
        }

        public void RenderMode3(uint vcount, byte* vram)
        {
            uint screenBase = (uint)(vcount * Width);
            uint vramBase = (uint)(vcount * Width * 2) + LcdcVramBlock * 131072;

            for (uint p = 0; p < Width; p++)
            {
                byte b0 = vram[vramBase + 0];
                byte b1 = vram[vramBase + 1];

                ushort data = (ushort)((b1 << 8) | b0);

                ScreenBack[screenBase] = data;

                screenBase++;
                vramBase += 2;
            }
        }

        public void RenderWhiteScanline(uint vcount)
        {
            // Render white
            uint screenBase = (uint)(vcount * Width);

            for (uint p = 0; p < Width; p++)
            {
                ScreenBack[screenBase] = 0x7FFF;
                screenBase++;
            }
        }

        public byte ReadHwio8(uint addr)
        {
            switch (addr)
            {
                case 0x08: // BG0CNT B0
                case 0x09: // BG0CNT B1
                case 0x0A: // BG1CNT B0
                case 0x0B: // BG1CNT B1
                case 0x0C: // BG2CNT B0
                case 0x0D: // BG2CNT B1
                case 0x0E: // BG3CNT B0
                case 0x0F: // BG3CNT B1
                    return Backgrounds[(addr >> 1) & 3].ReadBGCNT(addr & 1);

                case 0x10: // BG0HOFS B0
                case 0x11: // BG0HOFS B1
                case 0x12: // BG0VOFS B0
                case 0x13: // BG0VOFS B1
                    return Backgrounds[0].ReadBGOFS(addr & 3);
                case 0x14: // BG1HOFS B0
                case 0x15: // BG1HOFS B1
                case 0x16: // BG1VOFS B0
                case 0x17: // BG1VOFS B1
                    return Backgrounds[1].ReadBGOFS(addr & 3);
                case 0x18: // BG2HOFS B0
                case 0x19: // BG2HOFS B1
                case 0x1A: // BG2VOFS B0
                case 0x1B: // BG2VOFS B1
                    return Backgrounds[2].ReadBGOFS(addr & 3);
                case 0x1C: // BG3HOFS B0
                case 0x1D: // BG3HOFS B1
                case 0x1E: // BG3VOFS B0
                case 0x1F: // BG3VOFS B1
                    return Backgrounds[3].ReadBGOFS(addr & 3);


                case 0x20: // BG2PA B0
                case 0x21: // BG2PA B1
                case 0x22: // BG2PB B0
                case 0x23: // BG2PB B1
                case 0x24: // BG2PC B0
                case 0x25: // BG2PC B1
                case 0x26: // BG2PD B0
                case 0x27: // BG2PD B1
                    return Backgrounds[3].ReadBGPX(addr & 7);
                case 0x28: // BG2X B0
                case 0x29: // BG2X B1
                case 0x2A: // BG2X B2
                case 0x2B: // BG2X B3
                case 0x2C: // BG2Y B0
                case 0x2D: // BG2Y B1
                case 0x2E: // BG2Y B2
                case 0x2F: // BG2Y B3
                    return Backgrounds[2].ReadBGXY(addr & 7);

                case 0x30: // BG3PA B0
                case 0x31: // BG3PA B1
                case 0x32: // BG3PB B0
                case 0x33: // BG3PB B1
                case 0x34: // BG3PC B0
                case 0x35: // BG3PC B1
                case 0x36: // BG3PD B0
                case 0x37: // BG3PD B1
                    return Backgrounds[3].ReadBGPX(addr & 7);
                case 0x38: // BG3X B0
                case 0x39: // BG3X B1
                case 0x3A: // BG3X B2
                case 0x3B: // BG3X B3
                case 0x3C: // BG3Y B0
                case 0x3D: // BG3Y B1
                case 0x3E: // BG3Y B2
                case 0x3F: // BG3Y B3
                    return Backgrounds[3].ReadBGXY(addr & 7);

                case 0x40: // WIN0H B0
                    return Win0HRight;
                case 0x41: // WIN0H B1
                    return Win0HLeft;
                case 0x42: // WIN1H B0
                    return Win1HRight;
                case 0x43: // WIN1H B1
                    return Win1HLeft;

                case 0x44: // WIN0V B0
                    return Win0VBottom;
                case 0x45: // WIN0V B1
                    return Win0VTop;
                case 0x46: // WIN1V B0
                    return Win1VBottom;
                case 0x47: // WIN1V B1
                    return Win1VTop;

                case 0x48: // WININ B0
                    return (byte)((WININValue >> 0) & 0x3F);
                case 0x49: // WININ B1
                    return (byte)((WININValue >> 8) & 0x3F);

                case 0x4A: // WINOUT B0
                    return (byte)((WINOUTValue >> 0) & 0x3F);
                case 0x4B: // WINOUT B1
                    return (byte)((WINOUTValue >> 8) & 0x3F);

                case 0x4C: // MOSAIC B0
                    return MOSAICValueB0;
                case 0x4D: // MOSAIC B1
                    return MOSAICValueB1;

                case 0x50: // BLDCNT B0
                    return (byte)((BLDCNTValue >> 0) & 0xFF);
                case 0x51: // BLDCNT B1
                    return (byte)((BLDCNTValue >> 8) & 0x3F);

                case 0x52: // BLDALPHA B0
                    return (byte)(BLDALPHAValue >> 0);
                case 0x53: // BLDALPHA B1
                    return (byte)(BLDALPHAValue >> 8);

                case 0x54: // BLDY
                    return (byte)BlendBrightness;
            }

            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x08: // BG0CNT B0
                case 0x09: // BG0CNT B1
                case 0x0A: // BG1CNT B0
                case 0x0B: // BG1CNT B1
                case 0x0C: // BG2CNT B0
                case 0x0D: // BG2CNT B1
                case 0x0E: // BG3CNT B0
                case 0x0F: // BG3CNT B1
                    Backgrounds[(addr >> 1) & 3].WriteBGCNT(addr & 1, val);
                    BackgroundSettingsDirty = true;
                    break;

                case 0x10: // BG0HOFS B0
                case 0x11: // BG0HOFS B1
                case 0x12: // BG0VOFS B0
                case 0x13: // BG0VOFS B1
                    Backgrounds[0].WriteBGOFS(addr & 3, val);
                    break;
                case 0x14: // BG1HOFS B0
                case 0x15: // BG1HOFS B1
                case 0x16: // BG1VOFS B0
                case 0x17: // BG1VOFS B1
                    Backgrounds[1].WriteBGOFS(addr & 3, val);
                    break;
                case 0x18: // BG2HOFS B0
                case 0x19: // BG2HOFS B1
                case 0x1A: // BG2VOFS B0
                case 0x1B: // BG2VOFS B1
                    Backgrounds[2].WriteBGOFS(addr & 3, val);
                    break;
                case 0x1C: // BG3HOFS B0
                case 0x1D: // BG3HOFS B1
                case 0x1E: // BG3VOFS B0
                case 0x1F: // BG3VOFS B1
                    Backgrounds[3].WriteBGOFS(addr & 3, val);
                    break;

                case 0x20: // BG2PA B0
                case 0x21: // BG2PA B1
                case 0x22: // BG2PB B0
                case 0x23: // BG2PB B1
                case 0x24: // BG2PC B0
                case 0x25: // BG2PC B1
                case 0x26: // BG2PD B0
                case 0x27: // BG2PD B1
                    Backgrounds[2].WriteBGPX(addr & 7, val);
                    break;
                case 0x28: // BG2X B0
                case 0x29: // BG2X B1
                case 0x2A: // BG2X B2
                case 0x2B: // BG2X B3
                case 0x2C: // BG2Y B0
                case 0x2D: // BG2Y B1
                case 0x2E: // BG2Y B2
                case 0x2F: // BG2Y B3
                    Backgrounds[2].WriteBGXY(addr & 7, val);
                    break;

                case 0x30: // BG3PA B0
                case 0x31: // BG3PA B1
                case 0x32: // BG3PB B0
                case 0x33: // BG3PB B1
                case 0x34: // BG3PC B0
                case 0x35: // BG3PC B1
                case 0x36: // BG3PD B0
                case 0x37: // BG3PD B1
                    Backgrounds[3].WriteBGPX(addr & 7, val);
                    break;
                case 0x38: // BG3X B0
                case 0x39: // BG3X B1
                case 0x3A: // BG3X B2
                case 0x3B: // BG3X B3
                case 0x3C: // BG3Y B0
                case 0x3D: // BG3Y B1
                case 0x3E: // BG3Y B2
                case 0x3F: // BG3Y B3
                    Backgrounds[3].WriteBGXY(addr & 7, val);
                    break;

                case 0x40: // WIN0H B0
                    Win0HRight = val;
                    break;
                case 0x41: // WIN0H B1
                    Win0HLeft = val;
                    break;
                case 0x42: // WIN1H B0
                    Win1HRight = val;
                    break;
                case 0x43: // WIN1H B1
                    Win1HLeft = val;
                    break;

                case 0x44: // WIN0V B0
                    Win0VBottom = val;
                    break;
                case 0x45: // WIN0V B1
                    Win0VTop = val;
                    break;
                case 0x46: // WIN1V B0
                    Win1VBottom = val;
                    break;
                case 0x47: // WIN1V B1
                    Win1VTop = val;
                    break;

                case 0x48: // WININ B0
                    Win0InEnable = (byte)(val & 0b111111U);

                    WININValue &= 0x7F00;
                    WININValue |= (ushort)(val << 0);
                    break;
                case 0x49: // WININ B1
                    Win1InEnable = (byte)(val & 0b111111U);

                    WININValue &= 0x007F;
                    WININValue |= (ushort)(val << 8);
                    break;

                case 0x4A: // WINOUT B0
                    WinOutEnable = (byte)(val & 0b111111U);

                    WINOUTValue &= 0x7F00;
                    WINOUTValue |= (ushort)(val << 0);
                    break;
                case 0x4B: // WINOUT B1
                    WinObjEnable = (byte)(val & 0b111111U);

                    WINOUTValue &= 0x007F;
                    WINOUTValue |= (ushort)(val << 8);
                    break;

                case 0x4C: // MOSAIC B0
                    MOSAICValueB0 = val;

                    BgMosaicX = (byte)((val >> 0) & 0xF);
                    BgMosaicY = (byte)((val >> 4) & 0xF);
                    break;
                case 0x4D: // MOSAIC B1
                    MOSAICValueB1 = val;

                    ObjMosaicX = (byte)((val >> 0) & 0xF);
                    ObjMosaicY = (byte)((val >> 4) & 0xF);
                    break;

                case 0x50: // BLDCNT B0
                    Target1Flags = val & 0b111111U;

                    BlendEffect = (BlendEffect)((val >> 6) & 0b11U);

                    BLDCNTValue &= 0x7F00;
                    BLDCNTValue |= (ushort)(val << 0);
                    break;
                case 0x51: // BLDCNT B1
                    Target2Flags = val & 0b111111U;

                    BLDCNTValue &= 0x00FF;
                    BLDCNTValue |= (ushort)(val << 8);
                    break;

                case 0x52: // BLDALPHA B0
                    BlendACoeff = val & 0b11111U;
                    BLDALPHAValue &= 0x7F00;
                    BLDALPHAValue |= (ushort)(val << 0);
                    break;
                case 0x53: // BLDALPHA B1
                    BlendBCoeff = val & 0b11111U;
                    BLDALPHAValue &= 0x00FF;
                    BLDALPHAValue |= (ushort)(val << 8);
                    break;

                case 0x54: // BLDY
                    BlendBrightness = (byte)(val & 0b11111);
                    break;
            }
        }
    }
}