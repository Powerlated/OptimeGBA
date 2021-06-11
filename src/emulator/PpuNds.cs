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

        public bool DebugDisableVramUpdates;

        public byte ReadVram8Arm9(uint addr)
        {
            switch (addr & 0xFFE00000)
            {
                case 0x06000000: // Engine A BG VRAM
                    return ReadVram8Arm9BgA(addr);
                case 0x06200000: // Engine B BG VRAM
                    return ReadVram8Arm9BgB(addr);
                case 0x06400000: // Engine A OBJ VRAM
                    return ReadVram8Arm9ObjA(addr);
                case 0x06600000: // Engine B OBJ VRAM
                    return ReadVram8Arm9ObjB(addr);
                case 0x06800000: // LCDC VRAM
                    return ReadVram8Arm9Lcdc(addr);
            }

            return 0;
        }

        public byte ReadVram8Arm9BgA(uint addr)
        {
            addr &= 0x1FFFFF;
            byte val = 0;
            uint offs = Nds.MemoryControl.GetOffset(0) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(0, 1))
            {
                val |= VramA[addr & 0x1FFFF];
            }
            offs = Nds.MemoryControl.GetOffset(1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(1, 1))
            {
                val |= VramB[addr & 0x1FFFF];
            }
            offs = Nds.MemoryControl.GetOffset(2) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(2, 1))
            {
                val |= VramC[addr & 0x1FFFF];
            }
            offs = Nds.MemoryControl.GetOffset(3) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(3, 1))
            {
                val |= VramD[addr & 0x1FFFF];
            }
            if (addr >= 0 && addr < 0x10000 && Nds.MemoryControl.VramEnabledAndSet(4, 1))
            {
                val |= VramE[addr & 0xFFFF];
            }
            offs = (Nds.MemoryControl.GetOffset(5) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(5) >> 1) & 1) * 0x10000;
            if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(5, 1))
            {
                val |= VramF[addr & 0x3FFF];
            }
            offs = (Nds.MemoryControl.GetOffset(6) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(6) >> 1) & 1) * 0x10000;
            if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(6, 1))
            {
                val |= VramG[addr & 0x3FFF];
            }
            return val;
        }

        public byte ReadVram8Arm9BgB(uint addr)
        {
            byte val = 0;
            addr &= 0x1FFFFF;
            if (addr < 0x20000 && Nds.MemoryControl.VramEnabledAndSet(2, 4))
            {
                val |= VramC[addr & 0x1FFFF];
            }
            if (addr < 0x8000 && Nds.MemoryControl.VramEnabledAndSet(7, 1))
            {
                val |= VramH[addr & 0x7FFF];
            }
            if (addr >= 0x8000 && addr < 0xC000 && Nds.MemoryControl.VramEnabledAndSet(8, 1))
            {
                val |= VramI[addr & 0x3FFF];
            }
            return val;
        }

        public byte ReadVram8Arm9ObjA(uint addr)
        {
            byte val = 0;
            addr &= 0x1FFFFF;
            uint offs = (Nds.MemoryControl.GetOffset(0) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(0, 2))
            {
                val |= VramA[addr & 0x1FFFF];
            }
            offs = (Nds.MemoryControl.GetOffset(1) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(1, 2))
            {
                val |= VramB[addr & 0x1FFFF];
            }
            if (addr >= 0 && addr < 0x10000 && Nds.MemoryControl.VramEnabledAndSet(4, 2))
            {
                val |= VramE[addr & 0xFFFF];
            }
            offs = (Nds.MemoryControl.GetOffset(5) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(5) >> 1) & 1) * 0x10000;
            if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(5, 2))
            {
                val |= VramF[addr & 0x3FFF];
            }
            offs = (Nds.MemoryControl.GetOffset(6) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(6) >> 1) & 1) * 0x10000;
            if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(6, 2))
            {
                val |= VramG[addr & 0x3FFF];
            }
            return val;
        }

        public byte ReadVram8Arm9ObjB(uint addr)
        {
            byte val = 0;
            addr &= 0x1FFFFF;
            if (addr < 0x20000 && Nds.MemoryControl.VramEnabledAndSet(3, 4))
            {
                val |= VramD[addr & 0x1FFFF];
            }
            if (addr < 0x4000 && Nds.MemoryControl.VramEnabledAndSet(8, 2))
            {
                val |= VramI[addr & 0x3FFF];
            }
            return val;
        }

        public byte ReadVram8Arm9Lcdc(uint addr)
        {
            switch (addr & 0xE0000)
            {
                case 0x00000: // A
                    if (Nds.MemoryControl.VramEnabledAndSet(0, 0))
                        return VramA[addr & 0x1FFFF];
                    return 0;
                case 0x20000: // B
                    if (Nds.MemoryControl.VramEnabledAndSet(1, 0))
                        return VramB[addr & 0x1FFFF];
                    return 0;
                case 0x40000: // C
                    if (Nds.MemoryControl.VramEnabledAndSet(2, 0))
                        return VramC[addr & 0x1FFFF];
                    return 0;
                case 0x60000: // D
                    if (Nds.MemoryControl.VramEnabledAndSet(3, 0))
                        return VramD[addr & 0x1FFFF];
                    return 0;
                case 0x80000: // E, F, G, H
                    switch (addr & 0xFF000)
                    {
                        case 0x00000:
                            if (Nds.MemoryControl.VramEnabledAndSet(4, 0))
                                return VramE[addr & 0xFFFF];
                            return 0;
                        case 0x90000: // F
                            if (Nds.MemoryControl.VramEnabledAndSet(5, 0))
                                return VramF[addr & 0x3FFF];
                            return 0;
                        case 0x94000: // G
                            if (Nds.MemoryControl.VramEnabledAndSet(6, 0))
                                return VramG[addr & 0x3FFF];
                            return 0;
                        case 0x98000: // H
                            if (Nds.MemoryControl.VramEnabledAndSet(7, 0))
                                return VramH[addr & 0x7FFF];
                            return 0;
                    }
                    break;
                case 0x8A000: // I
                    if (Nds.MemoryControl.VramEnabledAndSet(8, 0))
                        return VramI[addr & 0x3FFF];
                    return 0;
            }

            return 0;
        }

        public void WriteVram8Arm9(uint addr, byte val)
        {
            uint offs;
            switch (addr & 0xFFE00000)
            {
                case 0x06000000: // Engine A BG VRAM
                    addr &= 0x1FFFFF;
                    offs = Nds.MemoryControl.GetOffset(0) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(0, 1))
                    {
                        VramA[addr & 0x1FFFF] = val;
                    }
                    offs = Nds.MemoryControl.GetOffset(1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(1, 1))
                    {
                        VramB[addr & 0x1FFFF] = val;
                    }
                    offs = Nds.MemoryControl.GetOffset(2) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(2, 1))
                    {
                        VramC[addr & 0x1FFFF] = val;
                    }
                    offs = Nds.MemoryControl.GetOffset(3) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(3, 1))
                    {
                        VramD[addr & 0x1FFFF] = val;
                    }
                    if (addr >= 0 && addr < 0x10000 && Nds.MemoryControl.VramEnabledAndSet(4, 1))
                    {
                        VramE[addr & 0xFFFF] = val;
                    }
                    offs = (Nds.MemoryControl.GetOffset(5) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(5, 1))
                    {
                        VramF[addr & 0x3FFF] = val;
                    }
                    offs = (Nds.MemoryControl.GetOffset(6) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(6, 1))
                    {
                        VramG[addr & 0x3FFF] = val;
                    }
                    VramBgA[addr & 0x1FFFFF] = ReadVram8Arm9BgA(addr & 0x1FFFFF);
                    break;
                case 0x06200000: // Engine B BG VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && Nds.MemoryControl.VramEnabledAndSet(2, 4))
                    {
                        VramC[addr & 0x1FFFF] = val;
                    }
                    if (addr < 0x8000 && Nds.MemoryControl.VramEnabledAndSet(7, 1))
                    {
                        VramH[addr & 0x7FFF] = val;
                    }
                    if (addr >= 0x8000 && addr < 0xC000 && Nds.MemoryControl.VramEnabledAndSet(8, 1))
                    {
                        VramI[addr & 0x3FFF] = val;
                    }
                    VramBgB[addr & 0x1FFFF] = ReadVram8Arm9BgB(addr & 0x1FFFF);
                    break;
                case 0x06400000: // Engine A OBJ VRAM
                    addr &= 0x1FFFFF;
                    offs = (Nds.MemoryControl.GetOffset(0) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(0, 2))
                    {
                        VramA[addr & 0x1FFFF] = val;
                    }
                    offs = (Nds.MemoryControl.GetOffset(1) & 1) * 0x20000;
                    if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(1, 2))
                    {
                        VramB[addr & 0x1FFFF] = val;
                    }
                    if (addr >= 0 && addr < 0x10000 && Nds.MemoryControl.VramEnabledAndSet(4, 2))
                    {
                        VramE[addr & 0xFFFF] = val;
                    }
                    offs = (Nds.MemoryControl.GetOffset(5) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(5) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(5, 2))
                    {
                        VramF[addr & 0x3FFF] = val;
                    }
                    offs = (Nds.MemoryControl.GetOffset(6) & 1) * 0x4000 + ((Nds.MemoryControl.GetOffset(6) >> 1) & 1) * 0x10000;
                    if (addr >= offs && addr < 0x4000 + offs && Nds.MemoryControl.VramEnabledAndSet(6, 2))
                    {
                        VramG[addr & 0x3FFF] = val;
                    }
                    VramObjA[addr & 0xFFFFF] = ReadVram8Arm9ObjA(addr & 0xFFFFF);
                    break;
                case 0x06600000: // Engine B OBJ VRAM
                    addr &= 0x1FFFFF;
                    if (addr < 0x20000 && Nds.MemoryControl.VramEnabledAndSet(3, 4))
                    {
                        VramD[addr & 0x1FFFF] = val;
                    }
                    if (addr < 0x4000 && Nds.MemoryControl.VramEnabledAndSet(8, 2))
                    {
                        VramI[addr & 0x3FFF] = val;
                    }
                    VramObjB[addr & 0x1FFFF] = ReadVram8Arm9ObjB(addr & 0x1FFFF);
                    break;
                case 0x06800000: // LCDC VRAM
                    switch (addr & 0xFFFE0000)
                    {
                        case 0x06800000: // A
                            if (Nds.MemoryControl.VramEnabledAndSet(0, 0))
                                VramA[addr & 0x1FFFF] = val;
                            break;
                        case 0x06820000: // B
                            if (Nds.MemoryControl.VramEnabledAndSet(1, 0))
                                VramB[addr & 0x1FFFF] = val;
                            break;
                        case 0x06840000: // C
                            if (Nds.MemoryControl.VramEnabledAndSet(2, 0))
                                VramC[addr & 0x1FFFF] = val;
                            break;
                        case 0x06860000: // D
                            if (Nds.MemoryControl.VramEnabledAndSet(3, 0))
                                VramD[addr & 0x1FFFF] = val;
                            break;
                        case 0x06880000: // E, F, G, H
                            switch (addr & 0xFFFFF000)
                            {
                                case 0x68800000:
                                    if (Nds.MemoryControl.VramEnabledAndSet(4, 0))
                                        VramE[addr & 0xFFFF] = val;
                                    break;
                                case 0x06890000: // F
                                    if (Nds.MemoryControl.VramEnabledAndSet(5, 0))
                                        VramF[addr & 0x3FFF] = val;
                                    break;
                                case 0x06894000: // G
                                    if (Nds.MemoryControl.VramEnabledAndSet(6, 0))
                                        VramG[addr & 0x3FFF] = val;
                                    break;
                                case 0x06898000: // H
                                    if (Nds.MemoryControl.VramEnabledAndSet(7, 0))
                                        VramH[addr & 0x7FFF] = val;
                                    break;
                            }
                            break;
                        case 0x068A0000: // I
                            if (Nds.MemoryControl.VramEnabledAndSet(8, 0))
                                VramI[addr & 0x3FFF] = val;
                            break;
                    }
                    break;
            }
        }

        public byte ReadVram8Arm7(uint addr)
        {
            uint offs;
            byte val = 0;
            addr &= 0x1FFFFF;
            offs = (Nds.MemoryControl.GetOffset(2) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(2, 2))
            {
                val |= VramC[addr & 0x1FFFF];
            }
            offs = (Nds.MemoryControl.GetOffset(3) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(3, 2))
            {
                val |= VramD[addr & 0x1FFFF];
            }

            return val;
        }

        public void WriteVram8Arm7(uint addr, byte val)
        {
            uint offs;
            addr &= 0x1FFFFF;
            offs = (Nds.MemoryControl.GetOffset(2) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(2, 2))
            {
                VramC[addr & 0x1FFFF] = val;
            }
            offs = (Nds.MemoryControl.GetOffset(3) & 1) * 0x20000;
            if (addr >= offs && addr < 0x20000 + offs && Nds.MemoryControl.VramEnabledAndSet(3, 2))
            {
                VramD[addr & 0x1FFFF] = val;
            }
        }

        public void CompileVram()
        {
            if (Nds.MemoryControl.VramConfigDirty && !DebugDisableVramUpdates)
            {
                Nds.MemoryControl.VramConfigDirty = false;
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
                    Console.WriteLine("VRAM reconfigured, recompiling from scratch");
                    for (uint i = 0; i < 524288; i++)
                    {
                        VramBgA[i] = ReadVram8Arm9(0x06000000 + i);
                    }
                    for (uint i = 0; i < 262144; i++)
                    {
                        VramObjA[i] = ReadVram8Arm9(0x06400000 + i);
                    }
                    for (uint i = 0; i < 131072; i++)
                    {
                        VramBgB[i] = ReadVram8Arm9(0x06200000 + i);
                    }
                    for (uint i = 0; i < 131072; i++)
                    {
                        VramObjB[i] = ReadVram8Arm9(0x06600000 + i);
                    }
                }
            }
        }

        public long ScanlineStartCycles;

        public uint DISPCNTAValue;
        public uint DISPCNTBValue;

        // DISPSTAT        
        public bool VCounterMatch7;
        public bool VBlankIrqEnable7;
        public bool HBlankIrqEnable7;
        public bool VCounterIrqEnable7;
        public uint VCountSetting7;
        public bool VCounterMatch9;
        public bool VBlankIrqEnable9;
        public bool HBlankIrqEnable9;
        public bool VCounterIrqEnable9;
        public uint VCountSetting9;

        // State
        public uint VCount;

        public long GetScanlineCycles()
        {
            return Scheduler.CurrentTicks - ScanlineStartCycles;
        }

        public byte ReadHwio8Arm9(uint addr)
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
                    if (VCounterMatch9) val = BitSet(val, 2);
                    if (VBlankIrqEnable9) val = BitSet(val, 3);
                    if (HBlankIrqEnable9) val = BitSet(val, 4);
                    if (VCounterIrqEnable9) val = BitSet(val, 5);
                    val |= (byte)((VCountSetting9 >> 1) & 0x80);
                    return val;
                case 0x4000005: // DISPSTAT B1
                    val |= (byte)VCountSetting9;
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

        public void WriteHwio8Arm9(uint addr, byte val)
        {
            switch (addr)
            {
                // A lot of these DISPCNT values are shared between A/B.
                case 0x4000000: // DISPCNT B0
                    // A
                    Renderers[0].Bg0Is3D = BitTest(val, 3);

                    // A+B
                    Renderers[0].BgMode = BitRange(val, 0, 2);
                    Renderers[0].ObjCharOneDimensional = BitTest(val, 4);
                    Renderers[0].BitmapObjShape = BitTest(val, 5);
                    Renderers[0].BitmapObjMapping = BitTest(val, 6);
                    Renderers[0].ForcedBlank = BitTest(val, 7);

                    Renderers[0].BackgroundSettingsDirty = true;

                    DISPCNTAValue &= 0xFFFFFF00;
                    DISPCNTAValue |= (uint)(val << 0);

                    break;
                case 0x4000001: // DISPCNT B1
                    // A+B
                    Renderers[0].ScreenDisplayBg[0] = BitTest(val, 8 - 8);
                    Renderers[0].ScreenDisplayBg[1] = BitTest(val, 9 - 8);
                    Renderers[0].ScreenDisplayBg[2] = BitTest(val, 10 - 8);
                    Renderers[0].ScreenDisplayBg[3] = BitTest(val, 11 - 8);
                    Renderers[0].ScreenDisplayObj = BitTest(val, 12 - 8);
                    Renderers[0].Window0DisplayFlag = BitTest(val, 13 - 8);
                    Renderers[0].Window1DisplayFlag = BitTest(val, 14 - 8);
                    Renderers[0].ObjWindowDisplayFlag = BitTest(val, 15 - 8);
                    Renderers[0].AnyWindowEnabled = (val & 0b11100000) != 0;

                    Renderers[0].BackgroundSettingsDirty = true;

                    DISPCNTAValue &= 0xFFFF00FF;
                    DISPCNTAValue |= (uint)(val << 8);
                    break;
                case 0x4000002: // DISPCNT B2
                    // A
                    Renderers[0].LcdcVramBlock = BitRange(val, 2, 3);
                    Renderers[0].BitmapObj1DBoundary = BitTest(val, 6);

                    // A+B
                    // var oldDisplayMode = Renderers[0].DisplayMode;
                    // if (Renderers[0].DisplayMode != oldDisplayMode) VramDirty = true;
                    Renderers[0].DisplayMode = BitRange(val, 0, 1);
                    Renderers[0].TileObj1DBoundary = BitRange(val, 4, 5);
                    Renderers[0].HBlankIntervalFree = BitTest(val, 7);

                    Renderers[0].BackgroundSettingsDirty = true;

                    DISPCNTAValue &= 0xFF00FFFF;
                    DISPCNTAValue |= (uint)(val << 16);
                    break;
                case 0x4000003: // DISPCNT B3
                    // A 
                    Renderers[0].CharBaseBlockCoarse = BitRange(val, 0, 2);
                    Renderers[0].MapBaseBlockCoarse = BitRange(val, 3, 5);

                    // A+B
                    Renderers[0].BgExtendedPalettes = BitTest(val, 6);
                    Renderers[0].ObjExtendedPalettes = BitTest(val, 7);

                    DISPCNTAValue &= 0x00FFFFFF;
                    DISPCNTAValue |= (uint)(val << 24);
                    break;

                case 0x4000004: // DISPSTAT B0
                    VBlankIrqEnable9 = BitTest(val, 3);
                    HBlankIrqEnable9 = BitTest(val, 4);
                    VCounterIrqEnable9 = BitTest(val, 5);

                    VCountSetting9 &= 0x0FFU;
                    VCountSetting9 |= (uint)((val & 0x80) << 1);
                    break;
                case 0x4000005: // DISPSTAT B1
                    VCountSetting9 &= 0x100U;
                    VCountSetting9 |= val;
                    break;

                case 0x4000006: // Vcount
                case 0x4000007:
                    // throw new NotImplementedException("NDS: write to vcount");
                    break;

                case 0x4001000: // DISPCNTB B0
                    // A+B
                    Renderers[1].BgMode = BitRange(val, 0, 2);
                    Renderers[1].ObjCharOneDimensional = BitTest(val, 4);
                    Renderers[1].BitmapObjShape = BitTest(val, 5);
                    Renderers[1].BitmapObjMapping = BitTest(val, 6);
                    Renderers[1].ForcedBlank = BitTest(val, 7);

                    Renderers[1].BackgroundSettingsDirty = true;

                    DISPCNTBValue &= 0xFFFFFF00;
                    DISPCNTBValue |= (uint)(val << 0);

                    break;
                case 0x4001001: // DISPCNTB B1
                    // A+B
                    Renderers[1].ScreenDisplayBg[0] = BitTest(val, 8 - 8);
                    Renderers[1].ScreenDisplayBg[1] = BitTest(val, 9 - 8);
                    Renderers[1].ScreenDisplayBg[2] = BitTest(val, 10 - 8);
                    Renderers[1].ScreenDisplayBg[3] = BitTest(val, 11 - 8);
                    Renderers[1].ScreenDisplayObj = BitTest(val, 12 - 8);
                    Renderers[1].Window0DisplayFlag = BitTest(val, 13 - 8);
                    Renderers[1].Window1DisplayFlag = BitTest(val, 14 - 8);
                    Renderers[1].ObjWindowDisplayFlag = BitTest(val, 15 - 8);
                    Renderers[1].AnyWindowEnabled = (val & 0b11100000) != 0;

                    Renderers[1].BackgroundSettingsDirty = true;

                    DISPCNTBValue &= 0xFFFF00FF;
                    DISPCNTBValue |= (uint)(val << 8);
                    break;
                case 0x4001002: // DISPCNTB B2
                    // A+B
                    // var oldDisplayModeB = Renderers[1].DisplayMode;
                    // if (Renderers[1].DisplayMode != oldDisplayModeB) VramDirty = true;
                    Renderers[1].DisplayMode = BitRange(val, 0, 1);
                    Renderers[1].TileObj1DBoundary = BitRange(val, 4, 5);
                    Renderers[1].HBlankIntervalFree = BitTest(val, 7);

                    Renderers[1].BackgroundSettingsDirty = true;

                    DISPCNTBValue &= 0xFF00FFFF;
                    DISPCNTBValue |= (uint)(val << 16);
                    break;
                case 0x4001003: // DISPCNTB B3
                    // A+B
                    Renderers[1].BgExtendedPalettes = BitTest(val, 6);
                    Renderers[1].ObjExtendedPalettes = BitTest(val, 7);

                    DISPCNTBValue &= 0x00FFFFFF;
                    DISPCNTBValue |= (uint)(val << 24);
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

        public byte ReadHwio8Arm7(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000004: // DISPSTAT B0
                    // Vblank flag is set in scanlines 192-261, not including 262 for some reason
                    if (VCount >= 192 && VCount <= 261) val = BitSet(val, 0);
                    // Hblank flag is set at cycle 1606, not cycle 1536
                    if (GetScanlineCycles() >= 1606) val = BitSet(val, 1);
                    if (VCounterMatch7) val = BitSet(val, 2);
                    if (VBlankIrqEnable7) val = BitSet(val, 3);
                    if (HBlankIrqEnable7) val = BitSet(val, 4);
                    if (VCounterIrqEnable7) val = BitSet(val, 5);
                    val |= (byte)((VCountSetting7 >> 1) & 0x80);
                    return val;
                case 0x4000005: // DISPSTAT B1
                    val |= (byte)VCountSetting7;
                    return val;
            }

            return 0;
        }

        public void WriteHwio8Arm7(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000004: // DISPSTAT B0
                    VBlankIrqEnable7 = BitTest(val, 3);
                    HBlankIrqEnable7 = BitTest(val, 4);
                    VCounterIrqEnable7 = BitTest(val, 5);

                    VCountSetting7 &= 0x0FFU;
                    VCountSetting7 |= (uint)((val & 0x80) << 1);
                    break;
                case 0x4000005: // DISPSTAT B1
                    VCountSetting7 &= 0x100U;
                    VCountSetting7 |= val;
                    break;
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
            }
        }

        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 594 - cyclesLate, EndHblank);

            // if (HBlankIrqEnable)
            // {
            // Gba.HwControl.FlagInterrupt(InterruptGba.HBlank);
            // }

            if (Renderers[0].DisplayMode == 2) // LCDC MODE
            {
                Renderers[0].RenderScanlineNds(VCount, VramLcdc, VramLcdc);
            }
            else
            {
                if (Renderers[0].DebugEnableRendering) Renderers[0].RenderScanlineNds(VCount, VramBgA, VramObjA);
            }
            if (Renderers[1].DisplayMode == 2)
            {
                Renderers[1].RenderScanlineNds(VCount, VramLcdc, VramLcdc);
            }
            else
            {
                if (Renderers[1].DebugEnableRendering) Renderers[1].RenderScanlineNds(VCount, VramBgB, VramObjB);
            }
            Renderers[0].IncrementMosaicCounters();
            Renderers[1].IncrementMosaicCounters();

            Nds.Nds9.Dma.Repeat((byte)DmaStartTimingNds9.HBlank);
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

                        if (VBlankIrqEnable7)
                        {
                            Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.VBlank);
                        }
                        if (VBlankIrqEnable9)
                        {
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
                Scheduler.AddEventRelative(SchedulerId.Ppu, 1536 - cyclesLate, EndDrawingToHblank);

                CompileVram();

                // Pre-render sprites for line zero
                fixed (byte* vramObjA = VramObjA, vramObjB = VramObjB)
                {
                    if (Renderers[0].DebugEnableObj && Renderers[0].ScreenDisplayObj) Renderers[0].RenderObjs(0, vramObjA);
                    if (Renderers[1].DebugEnableObj && Renderers[1].ScreenDisplayObj) Renderers[1].RenderObjs(0, vramObjB);
                }
            }

            VCounterMatch7 = VCount == VCountSetting7;
            VCounterMatch9 = VCount == VCountSetting9;

            if (VCounterMatch7 && VCounterIrqEnable7)
            {
                Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.VCounterMatch);
            }
            if (VCounterMatch9 && VCounterIrqEnable9)
            {
                Nds.Nds9.HwControl.FlagInterrupt((uint)InterruptNds.VCounterMatch);
            }
        }
    }
}
