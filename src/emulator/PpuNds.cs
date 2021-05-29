using static OptimeGBA.Bits;
using static OptimeGBA.MemoryUtil;
using System.Runtime.CompilerServices;
using System;

namespace OptimeGBA
{
    public sealed unsafe class PpuNds
    {
        Nds Nds;
        Scheduler Scheduler;

        public PpuRenderer[] Renderers;

        public PpuNds(Nds gba, Scheduler scheduler)
        {
            Nds = gba;
            Scheduler = scheduler;
            Renderers = new PpuRenderer[] {
                new PpuRenderer(true, 256, 192),
                new PpuRenderer(true, 256, 192)
            };

            Scheduler.AddEventRelative(SchedulerId.Ppu, 1536, EndDrawingToHblank);
        }

        // Raw VRAM Blocks
        public byte[] VramA = MemoryUtil.AllocateManagedArray(131072);
        public byte[] VramB = MemoryUtil.AllocateManagedArray(131072);
        public byte[] VramC = MemoryUtil.AllocateManagedArray(131072);
        public byte[] VramD = MemoryUtil.AllocateManagedArray(131072);
        public byte[] VramE = MemoryUtil.AllocateManagedArray(65536);
        public byte[] VramF = MemoryUtil.AllocateManagedArray(16384);
        public byte[] VramG = MemoryUtil.AllocateManagedArray(16384);
        public byte[] VramH = MemoryUtil.AllocateManagedArray(32768);
        public byte[] VramI = MemoryUtil.AllocateManagedArray(16384);

        // Built arrays (Passed to PpuRenderer for rendering)
        public byte[] VramLcdc = MemoryUtil.AllocateManagedArray(671744);
        public byte[] VramBgA = MemoryUtil.AllocateManagedArray(524288);
        public byte[] VramObjA = MemoryUtil.AllocateManagedArray(262144);
        public byte[] VramBgB = MemoryUtil.AllocateManagedArray(131072);
        public byte[] VramObjB = MemoryUtil.AllocateManagedArray(131072);

        public bool VramDirty;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EnabledAndSet(uint bank, uint mst)
        {
            uint vramcntMst = Nds.MemoryControl.VRAMCNT[bank] & 0b111U;
            bool vramcntEnable = BitTest(Nds.MemoryControl.VRAMCNT[bank], 7);

            return vramcntEnable && vramcntMst == mst;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetOffset(uint bank)
        {
            return (uint)(Nds.MemoryControl.VRAMCNT[bank] >> 3) & 0b11U;
        }

        // Needed since some games write to VRAM over and over again with the
        // same values.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetVram(byte[] arr, uint offs, byte val)
        {
            if (arr[offs] != val)
            {
                arr[offs] = val;
                VramDirty = true;
            }
        }

        public void WriteVram8(uint addr, byte val)
        {
            uint offs;
            switch (addr & 0xFFF00000)
            {
                case 0x06000000: // Engine A BG VRAM
                    addr &= 0x1FFFFF;
                    offs = GetOffset(0) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(0, 1))
                    {
                        SetVram(VramA, addr & 0x1FFFF, val);
                    }
                    offs = GetOffset(1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(1, 1))
                    {
                        SetVram(VramB, addr & 0x1FFFF, val);
                    }
                    offs = GetOffset(2) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(2, 1))
                    {
                        SetVram(VramC, addr & 0x1FFFF, val);
                    }
                    offs = GetOffset(3) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(3, 1))
                    {
                        SetVram(VramD, addr & 0x1FFFF, val);
                    }
                    if (addr >= 0 && addr < 0x10000 && EnabledAndSet(4, 1))
                    {
                        SetVram(VramE, addr & 0xFFFF, val);
                    }
                    offs = (GetOffset(5) & 1) * 0x4000 + ((GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(5, 1))
                    {
                        SetVram(VramF, addr & 0x3FFF, val);
                    }
                    offs = (GetOffset(6) & 1) * 0x4000 + ((GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(6, 1))
                    {
                        SetVram(VramG, addr & 0x3FFF, val);
                    }
                    break;
                case 0x06200000: // Engine B BG VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && EnabledAndSet(2, 4))
                    {
                        SetVram(VramC, addr & 0x1FFFF, val);
                    }
                    if (addr < 0x8000 && EnabledAndSet(7, 1))
                    {
                        SetVram(VramH, addr & 0x7FFF, val);
                    }
                    if (addr >= 0x8000 && addr < 0xC000 && EnabledAndSet(8, 1))
                    {
                        SetVram(VramI, addr & 0x3FFF, val);
                    }
                    break;
                case 0x06400000: // Engine A OBJ VRAM
                    addr &= 0x1FFFFF;
                    offs = (GetOffset(0) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(0, 2))
                    {
                        SetVram(VramA, addr & 0x1FFFF, val);
                    }
                    offs = (GetOffset(1) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(1, 2))
                    {
                        SetVram(VramB, addr & 0x1FFFF, val);
                    }
                    if (addr >= 0 && addr < 0x10000 && EnabledAndSet(4, 2))
                    {
                        SetVram(VramE, addr & 0xFFFF, val);
                    }
                    offs = (GetOffset(5) & 1) * 0x4000 + ((GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(5, 2))
                    {
                        SetVram(VramF, addr & 0x3FFF, val);
                    }
                    offs = (GetOffset(6) & 1) * 0x4000 + ((GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(6, 2))
                    {
                        SetVram(VramG, addr & 0x3FFF, val);
                    }
                    break;
                case 0x06600000: // Engine B OBJ VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && EnabledAndSet(3, 4))
                    {
                        SetVram(VramD, addr & 0x1FFFF, val);
                    }
                    if (addr < 0x4000 && EnabledAndSet(8, 2))
                    {
                        SetVram(VramI, addr & 0x3FFF, val);
                    }
                    break;
                case 0x06800000: // LCDC VRAM
                    switch (addr & 0xFFFE0000)
                    {
                        case 0x06800000: // A
                            if (EnabledAndSet(0, 0))
                                SetVram(VramA, addr & 0x1FFFF, val);
                            break;
                        case 0x06820000: // B
                            if (EnabledAndSet(1, 0))
                                SetVram(VramB, addr & 0x1FFFF, val);
                            break;
                        case 0x06840000: // C
                            if (EnabledAndSet(2, 0))
                                SetVram(VramC, addr & 0x1FFFF, val);
                            break;
                        case 0x06860000: // D
                            if (EnabledAndSet(3, 0))
                                SetVram(VramD, addr & 0x1FFFF, val);
                            break;
                        case 0x06880000: // E, F, G, H
                            switch (addr & 0xFFFFF000)
                            {
                                case 0x68800000:
                                    if (EnabledAndSet(4, 0))
                                        SetVram(VramE, addr & 0xFFFF, val);
                                    break;
                                case 0x06890000: // F
                                    if (EnabledAndSet(5, 0))
                                        SetVram(VramF, addr & 0x3FFF, val);
                                    break;
                                case 0x06894000: // G
                                    if (EnabledAndSet(6, 0))
                                        SetVram(VramG, addr & 0x3FFF, val);
                                    break;
                                case 0x06898000: // H
                                    if (EnabledAndSet(7, 0))
                                        SetVram(VramH, addr & 0x7FFF, val);
                                    break;
                            }
                            break;
                        case 0x068A0000: // I
                            if (EnabledAndSet(8, 0))
                                SetVram(VramI, addr & 0x3FFF, val);
                            break;
                    }
                    break;
            }
        }

        public byte ReadVram8(uint addr)
        {
            uint offs;
            byte val = 0;

            switch (addr & 0xFFE00000)
            {
                case 0x06000000: // Engine A BG VRAM
                    addr &= 0x1FFFFF;
                    offs = GetOffset(0) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(0, 1))
                    {
                        val |= VramA[addr & 0x1FFFF];
                    }
                    offs = GetOffset(1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(1, 1))
                    {
                        val |= VramB[addr & 0x1FFFF];
                    }
                    offs = GetOffset(2) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(2, 1))
                    {
                        val |= VramC[addr & 0x1FFFF];
                    }
                    offs = GetOffset(3) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(3, 1))
                    {
                        val |= VramD[addr & 0x1FFFF];
                    }
                    if (addr >= 0 && addr < 0x10000 && EnabledAndSet(4, 1))
                    {
                        val |= VramE[addr & 0xFFFF];
                    }
                    offs = (GetOffset(5) & 1) * 0x4000 + ((GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(5, 1))
                    {
                        val |= VramF[addr & 0x3FFF];
                    }
                    offs = (GetOffset(6) & 1) * 0x4000 + ((GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(6, 1))
                    {
                        val |= VramG[addr & 0x3FFF];
                    }
                    break;
                case 0x06200000: // Engine B BG VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && EnabledAndSet(2, 4))
                    {
                        val |= VramC[addr & 0x1FFFF];
                    }
                    if (addr < 0x8000 && EnabledAndSet(7, 1))
                    {
                        val |= VramH[addr & 0x7FFF];
                    }
                    if (addr >= 0x8000 && addr < 0xC000 && EnabledAndSet(8, 1))
                    {
                        val |= VramI[addr & 0x3FFF];
                    }
                    break;
                case 0x06400000: // Engine A OBJ VRAM
                    addr &= 0x1FFFFF;
                    offs = (GetOffset(0) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(0, 2))
                    {
                        val |= VramA[addr & 0x1FFFF];
                    }
                    offs = (GetOffset(1) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && EnabledAndSet(1, 2))
                    {
                        val |= VramB[addr & 0x1FFFF];
                    }
                    if (addr >= 0 && addr < 0x10000 && EnabledAndSet(4, 2))
                    {
                        val |= VramE[addr & 0xFFFF];
                    }
                    offs = (GetOffset(5) & 1) * 0x4000 + ((GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(5, 2))
                    {
                        val |= VramF[addr & 0x3FFF];
                    }
                    offs = (GetOffset(6) & 1) * 0x4000 + ((GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && EnabledAndSet(6, 2))
                    {
                        val |= VramG[addr & 0x3FFF];
                    }
                    break;
                case 0x06600000: // Engine B OBJ VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && EnabledAndSet(3, 4))
                    {
                        val |= VramD[addr & 0x1FFFF];
                    }
                    if (addr < 0x4000 && EnabledAndSet(8, 2))
                    {
                        val |= VramI[addr & 0x3FFF];
                    }
                    break;
                case 0x06800000: // LCDC VRAM
                    switch (addr & 0xFFFE0000)
                    {
                        case 0x06800000: // A
                            if (EnabledAndSet(0, 0))
                                val = VramA[addr & 0x1FFFF];
                            break;
                        case 0x06820000: // B
                            if (EnabledAndSet(1, 0))
                                val = VramB[addr & 0x1FFFF];
                            break;
                        case 0x06840000: // C
                            if (EnabledAndSet(2, 0))
                                val = VramC[addr & 0x1FFFF];
                            break;
                        case 0x06860000: // D
                            if (EnabledAndSet(3, 0))
                                val = VramD[addr & 0x1FFFF];
                            break;
                        case 0x06880000: // E, F, G, H
                            switch (addr & 0xFFFFF000)
                            {
                                case 0x68800000:
                                    if (EnabledAndSet(4, 0))
                                        val = VramE[addr & 0xFFFF];
                                    break;
                                case 0x06890000: // F
                                    if (EnabledAndSet(5, 0))
                                        val = VramF[addr & 0x3FFF];
                                    break;
                                case 0x06894000: // G
                                    if (EnabledAndSet(6, 0))
                                        val = VramG[addr & 0x3FFF];
                                    break;
                                case 0x06898000: // H
                                    if (EnabledAndSet(7, 0))
                                        val = VramH[addr & 0x7FFF];
                                    break;
                            }
                            break;
                        case 0x068A0000: // I
                            if (EnabledAndSet(8, 0))
                                val = VramI[addr & 0x3FFF];
                            break;
                    }
                    break;
            }

            return val;
        }

        public void CompileVram()
        {
            // TODO: Optimize this 
            if (VramDirty)
            {
                VramDirty = false;

                if (Renderers[0].DisplayMode == 2) // LCDC MODE
                {
                    uint index = 0;
                    VramA.CopyTo(VramLcdc, index); index += 131072;
                    VramB.CopyTo(VramLcdc, index); index += 131072;
                    VramC.CopyTo(VramLcdc, index); index += 131072;
                    VramD.CopyTo(VramLcdc, index); index += 131072;
                    VramE.CopyTo(VramLcdc, index); index += 65536;
                    VramF.CopyTo(VramLcdc, index); index += 16384;
                    VramG.CopyTo(VramLcdc, index); index += 16384;
                    VramH.CopyTo(VramLcdc, index); index += 32768;
                    VramI.CopyTo(VramLcdc, index); index += 16384;
                }
                else
                {
                    // TODO: Stop running this every scanline. This kills performance.
                    for (uint i = 0; i < 524288; i++)
                    {
                        VramBgA[i] = ReadVram8(0x06000000 + i);
                    }
                    for (uint i = 0; i < 262144; i++)
                    {
                        VramObjA[i] = ReadVram8(0x06400000 + i);
                    }
                    for (uint i = 0; i < 131072; i++)
                    {
                        VramBgB[i] = ReadVram8(0x06200000 + i);
                    }
                    for (uint i = 0; i < 131072; i++)
                    {
                        VramObjB[i] = ReadVram8(0x06600000 + i);
                    }
                }
            }
        }

        public long ScanlineStartCycles;

        public uint DISPCNTAValue;
        public uint DISPCNTBValue;

        // DISPSTAT        
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;

        // State
        public uint VCount;

        public long GetScanlineCycles()
        {
            return Scheduler.CurrentTicks - ScanlineStartCycles;
        }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000000: // DISPCNTA B0
                    return (byte)(DISPCNTAValue >> 0);
                case 0x4000001: // DISPCNTA B1
                    return (byte)(DISPCNTAValue >> 8);
                case 0x4000002: // DISPCNTA B2
                    return (byte)(DISPCNTAValue >> 16);
                case 0x4000003: // DISPCNTA B3
                    return (byte)(DISPCNTAValue >> 24);

                case 0x4000004: // DISPSTAT B0
                    // Vblank flag is set in scanlines 192-261, not including 262 for some reason
                    if (VCount >= 192 && VCount <= 261) val = BitSet(val, 0);
                    // Hblank flag is set at cycle 1606, not cycle 1536
                    if (GetScanlineCycles() >= 1606) val = BitSet(val, 1);
                    if (VCounterMatch) val = BitSet(val, 2);
                    if (VBlankIrqEnable) val = BitSet(val, 3);
                    if (HBlankIrqEnable) val = BitSet(val, 4);
                    if (VCounterIrqEnable) val = BitSet(val, 5);
                    return val;
                case 0x4000005: // DISPSTAT B1
                    val |= VCountSetting;
                    return val;

                case 0x4000006: // VCOUNT B0 - B1 only exists for Nintendo DS
                    val |= (byte)VCount;
                    return val;
                case 0x4000007:
                    val |= (byte)((VCount >> 8) & 1);
                    return val;

                case 0x4001000: // DISPCNTB B0
                    return (byte)(DISPCNTBValue >> 0);
                case 0x4001001: // DISPCNTB B1
                    return (byte)(DISPCNTBValue >> 8);
                case 0x4001002: // DISPCNTB B2
                    return (byte)(DISPCNTBValue >> 16);
                case 0x4001003: // DISPCNTB B3
                    return (byte)(DISPCNTBValue >> 24);
            }

            if (addr >= 0x4000000 && addr < 0x4000058)
            {
                return Renderers[0].ReadHwio8(addr & 0xFF);
            }
            if (addr >= 0x4001000 && addr < 0x4001058)
            {
                return Renderers[1].ReadHwio8(addr & 0xFF);
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                // A lot of these DISPCNT values are shared between A/B.
                case 0x4000000: // DISPCNT B0
                    Renderers[0].Bg0Is3D = BitTest(val, 3);

                    for (int i = 0; i < 2; i++)
                    {
                        Renderers[i].BgMode = BitRange(val, 0, 2);
                        Renderers[i].ObjCharOneDimensional = BitTest(val, 4);
                        Renderers[i].BitmapObjShape = BitTest(val, 5);
                        Renderers[i].BitmapObjMapping = BitTest(val, 6);
                        Renderers[i].ForcedBlank = BitTest(val, 7);

                        Renderers[i].BackgroundSettingsDirty = true;
                    }

                    DISPCNTAValue &= 0xFFFFFF00;
                    DISPCNTAValue |= (uint)(val << 0);

                    break;
                case 0x4000001: // DISPCNT B1
                    for (int i = 0; i < 2; i++)
                    {
                        Renderers[i].ScreenDisplayBg[0] = BitTest(val, 8 - 8);
                        Renderers[i].ScreenDisplayBg[1] = BitTest(val, 9 - 8);
                        Renderers[i].ScreenDisplayBg[2] = BitTest(val, 10 - 8);
                        Renderers[i].ScreenDisplayBg[3] = BitTest(val, 11 - 8);
                        Renderers[i].ScreenDisplayObj = BitTest(val, 12 - 8);
                        Renderers[i].Window0DisplayFlag = BitTest(val, 13 - 8);
                        Renderers[i].Window1DisplayFlag = BitTest(val, 14 - 8);
                        Renderers[i].ObjWindowDisplayFlag = BitTest(val, 15 - 8);
                        Renderers[i].AnyWindowEnabled = (val & 0b11100000) != 0;

                        Renderers[i].BackgroundSettingsDirty = true;
                    }
                    
                    DISPCNTAValue &= 0xFFFF00FF;
                    DISPCNTAValue |= (uint)(val << 8);
                    break;
                case 0x4000002: // DISPCNT B2
                    Renderers[0].LcdcVramBlock = BitRange(val, 2, 3);
                    Renderers[0].BitmapObj1DBoundary = BitTest(val, 6);

                    for (int i = 0; i < 2; i++)
                    {
                        var oldDisplayMode = Renderers[i].DisplayMode;
                        if (Renderers[i].DisplayMode != oldDisplayMode) VramDirty = true;
                        Renderers[i].DisplayMode = BitRange(val, 0, 1);
                        Renderers[i].TileObj1DBoundary = BitRange(val, 4, 5);
                        Renderers[i].HBlankIntervalFree = BitTest(val, 7);

                        Renderers[i].BackgroundSettingsDirty = true;
                    }

                    DISPCNTAValue &= 0xFF00FFFF;
                    DISPCNTAValue |= (uint)(val << 16);
                    break;
                case 0x4000003: // DISPCNT B3
                    Renderers[0].CharBaseBlockCoarse = BitRange(val, 0, 2);
                    Renderers[0].MapBaseBlockCoarse = BitRange(val, 3, 5);

                    for (int i = 0; i < 2; i++)
                    {
                        Renderers[i].BgExtendedPalettes = BitTest(val, 6);
                        Renderers[i].ObjExtendedPalettes = BitTest(val, 7);
                    }

                    DISPCNTAValue &= 0x00FFFFFF;
                    DISPCNTAValue |= (uint)(val << 24);
                    break;

                case 0x4000004: // DISPSTAT B0
                    VBlankIrqEnable = BitTest(val, 3);
                    HBlankIrqEnable = BitTest(val, 4);
                    VCounterIrqEnable = BitTest(val, 5);
                    break;
                case 0x4000005: // DISPSTAT B1
                    VCountSetting = val;
                    break;

                case 0x4000006: // Vcount
                case 0x4000007:
                    // throw new NotImplementedException("NDS: write to vcount");
                    break;

                case 0x4001000: // DISPCNT B0
                    DISPCNTAValue &= 0xFFFFFF00;
                    DISPCNTAValue |= (uint)(val << 0);
                    break;
                case 0x4001001: // DISPCNT B1
                    DISPCNTAValue &= 0xFFFF00FF;
                    DISPCNTAValue |= (uint)(val << 8);
                    break;
                case 0x4001002: // DISPCNT B2
                    Renderers[1].BitmapObj1DBoundary = BitTest(val, 6);

                    DISPCNTAValue &= 0xFF00FFFF;
                    DISPCNTAValue |= (uint)(val << 16);
                    break;
                case 0x4001003: // DISPCNT B3
                    Renderers[1].CharBaseBlockCoarse = BitRange(val, 0, 2);
                    Renderers[1].MapBaseBlockCoarse = BitRange(val, 3, 5);

                    DISPCNTAValue &= 0x00FFFFFF;
                    DISPCNTAValue |= (uint)(val << 24);
                    break;
            }

            if (addr >= 0x4000000 && addr < 0x4000058)
            {
                Renderers[0].WriteHwio8(addr & 0xFF, val);
            }
            if (addr >= 0x4001000 && addr < 0x4001058)
            {
                Renderers[1].WriteHwio8(addr & 0xFF, val);
            }
        }

        public byte ReadOam8(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetByte(Renderers[id].Oam, addr);
        }

        public ushort ReadOam16(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetUshort(Renderers[id].Oam, addr);
        }

        public uint ReadOam32(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetUint(Renderers[id].Oam, addr);
        }

        public void WriteOam16(uint addr, ushort val)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            SetUshort(Renderers[id].Oam, addr, val);
        }

        public void WriteOam32(uint addr, uint val)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            SetUint(Renderers[id].Oam, addr, val);
        }

        public byte ReadPalettes8(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetByte(Renderers[id].Palettes, addr);
        }

        public ushort ReadPalettes16(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetUshort(Renderers[id].Palettes, addr);
        }

        public uint ReadPalettes32(uint addr)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            return GetUint(Renderers[id].Palettes, addr);
        }

        public void WritePalettes16(uint addr, ushort val)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            if (GetUshort(Renderers[id].Palettes, addr) != val)
            {
                SetUshort(Renderers[id].Palettes, addr, val);
                Renderers[id].UpdatePalette(addr / 2);
            }
        }

        public void WritePalettes32(uint addr, uint val)
        {
            addr &= 0x7FF;
            var id = addr >= 0x400 ? 1 : 0;
            addr &= 0x3FF;
            if (GetUint(Renderers[id].Palettes, addr) != val)
            {
                SetUint(Renderers[id].Palettes, addr, val);
                Renderers[id].UpdatePalette(addr / 2);
            }
        }

        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 594 - cyclesLate, EndHblank);

            // if (HBlankIrqEnable)
            // {
            // Gba.HwControl.FlagInterrupt(InterruptGba.HBlank);
            // }

            CompileVram();
            if (Renderers[0].DisplayMode == 2) // LCDC MODE
            {
                Renderers[0].RenderScanlineNds(VCount, VramLcdc, VramLcdc);
            }
            else
            {
                if (Renderers[0].DebugEnableRendering) Renderers[0].RenderScanlineNds(VCount, VramBgA, VramObjA);
                if (Renderers[1].DebugEnableRendering) Renderers[1].RenderScanlineNds(VCount, VramBgB, VramObjB);
            }
            Renderers[0].IncrementMosaicCounters();
            Renderers[1].IncrementMosaicCounters();

            // Gba.Dma.RepeatHblank();
        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 594 - cyclesLate, EndHblank);

            // if (HBlankIrqEnable)
            // {
            //     Nds.HwControl.FlagInterrupt(InterruptGba.HBlank);
            // }
        }

        public void EndHblank(long cyclesLate)
        {
            ScanlineStartCycles = Scheduler.CurrentTicks;

            if (VCount != 262)
            {
                VCount++;

                if (VCount > 191)
                {
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 1536 - cyclesLate, EndVblankToHblank);

                    if (VCount == 192)
                    {
                        // Nds.Dma.RepeatVblank();

                        if (VBlankIrqEnable)
                        {
                            Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.VBlank);
                            Nds.Nds9.HwControl.FlagInterrupt((uint)InterruptNds.VBlank);
                        }

                        Renderers[0].RunVblankOperations();

                        Renderers[0].TotalFrames++;
                        if (Renderers[0].DebugEnableRendering) Renderers[0].SwapBuffers();
                        if (Renderers[1].DebugEnableRendering) Renderers[1].SwapBuffers();

                        Renderers[0].RenderingDone = true;
                    }
                }
                else
                {
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 1536 - cyclesLate, EndDrawingToHblank);
                }
            }
            else
            {
                VCount = 0;
                VCounterMatch = VCount == VCountSetting;
                // if (VCounterMatch && VCounterIrqEnable)
                // {
                //     Nds.HwControl.FlagInterrupt(InterruptGba.VCounterMatch);
                // }
                Scheduler.AddEventRelative(SchedulerId.Ppu, 1536 - cyclesLate, EndDrawingToHblank);

                // Pre-render sprites for line zero
                fixed (byte* vramObjA = VramObjA, vramObjB = VramObjB)
                {
                    if (Renderers[0].DebugEnableObj && Renderers[0].ScreenDisplayObj) Renderers[0].RenderObjs(0, vramObjA);
                    if (Renderers[1].DebugEnableObj && Renderers[1].ScreenDisplayObj) Renderers[1].RenderObjs(0, vramObjB);
                }
            }

            VCounterMatch = VCount == VCountSetting;

            if (VCounterMatch && VCounterIrqEnable)
            {
                Nds.Nds9.HwControl.FlagInterrupt((uint)InterruptNds.VCounterMatch);
                Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.VCounterMatch);
            }
        }
    }
}
