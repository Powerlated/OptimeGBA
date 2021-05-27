using static OptimeGBA.Bits;
using System.Runtime.CompilerServices;
using static OptimeGBA.PpuRenderer;
using System;

namespace OptimeGBA
{
    public sealed unsafe class PpuGba
    {
        Gba Gba;
        Scheduler Scheduler;

        public PpuRenderer Renderer;

        public PpuGba(Gba gba, Scheduler scheduler)
        {
            Gba = gba;
            Scheduler = scheduler;
            Renderer = new PpuRenderer(false, 240, 160);

            Scheduler.AddEventRelative(SchedulerId.Ppu, 960, EndDrawingToHblank);
        }

        public byte[] Vram = MemoryUtil.AllocateManagedArray(98304);

        public long ScanlineStartCycles;

        public bool BiosMod = false;
        public bool BiosModLayer2 = false;
        public sbyte[] OamColorOffsets = new sbyte[128];

        public ushort DISPCNTValue;
        public ushort WININValue;
        public ushort WINOUTValue;
        public ushort BLDCNTValue;
        public uint BLDALPHAValue;
        public byte MOSAICValueB0;
        public byte MOSAICValueB1;

        // DISPSTAT        
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;


        public void DisableBiosMod(long cyclesLate)
        {
            BiosMod = false;
        }

        public void EnableBiosModLayer2(long cyclesLate)
        {
            BiosModLayer2 = true;

            OamColorOffsets[4] = -28;
            OamColorOffsets[20] = -28;
        }

        public long GetScanlineCycles()
        {
            return Scheduler.CurrentTicks - ScanlineStartCycles;
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
                    if (Renderer.VCount >= 160 && Renderer.VCount <= 226) val = BitSet(val, 0);
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
                    val |= (byte)Renderer.VCount;
                    break;
                case 0x4000007:
                    return 0;

                case 0x4000008: // BG0CNT B0
                case 0x4000009: // BG0CNT B1
                    return Renderer.Backgrounds[0].ReadBGCNT(addr - 0x4000008);
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    return Renderer.Backgrounds[1].ReadBGCNT(addr - 0x400000A);
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    return Renderer.Backgrounds[2].ReadBGCNT(addr - 0x400000C);
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    return Renderer.Backgrounds[3].ReadBGCNT(addr - 0x400000E);

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    return Renderer.Backgrounds[0].ReadBGOFS(addr - 0x4000010);
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    return Renderer.Backgrounds[1].ReadBGOFS(addr - 0x4000014);
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    return Renderer.Backgrounds[2].ReadBGOFS(addr - 0x4000018);
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    return Renderer.Backgrounds[3].ReadBGOFS(addr - 0x400001C);


                case 0x4000020: // BG2PA B0
                case 0x4000021: // BG2PA B1
                case 0x4000022: // BG2PB B0
                case 0x4000023: // BG2PB B1
                case 0x4000024: // BG2PC B0
                case 0x4000025: // BG2PC B1
                case 0x4000026: // BG2PD B0
                case 0x4000027: // BG2PD B1
                    return Renderer.Backgrounds[3].ReadBGPX(addr & 7);
                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    return Renderer.Backgrounds[2].ReadBGXY(addr & 7);

                case 0x4000030: // BG3PA B0
                case 0x4000031: // BG3PA B1
                case 0x4000032: // BG3PB B0
                case 0x4000033: // BG3PB B1
                case 0x4000034: // BG3PC B0
                case 0x4000035: // BG3PC B1
                case 0x4000036: // BG3PD B0
                case 0x4000037: // BG3PD B1
                    return Renderer.Backgrounds[3].ReadBGPX(addr & 7);
                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    return Renderer.Backgrounds[3].ReadBGXY(addr & 7);

                case 0x4000040: // WIN0H B0
                    return Renderer.Win0HRight;
                case 0x4000041: // WIN0H B1
                    return Renderer.Win0HLeft;
                case 0x4000042: // WIN1H B0
                    return Renderer.Win1HRight;
                case 0x4000043: // WIN1H B1
                    return Renderer.Win1HLeft;

                case 0x4000044: // WIN0V B0
                    return Renderer.Win0VBottom;
                case 0x4000045: // WIN0V B1
                    return Renderer.Win0VTop;
                case 0x4000046: // WIN1V B0
                    return Renderer.Win1VBottom;
                case 0x4000047: // WIN1V B1
                    return Renderer.Win1VTop;

                case 0x4000048: // WININ B0
                    return (byte)((WININValue >> 0) & 0x3F);
                case 0x4000049: // WININ B1
                    return (byte)((WININValue >> 8) & 0x3F);

                case 0x400004A: // WINOUT B0
                    return (byte)((WINOUTValue >> 0) & 0x3F);
                case 0x400004B: // WINOUT B1
                    return (byte)((WINOUTValue >> 8) & 0x3F);

                case 0x400004C: // MOSAIC B0
                    return MOSAICValueB0;
                case 0x400004D: // MOSAIC B1
                    return MOSAICValueB1;

                case 0x4000050: // BLDCNT B0
                    return (byte)((BLDCNTValue >> 0) & 0xFF);
                case 0x4000051: // BLDCNT B1
                    return (byte)((BLDCNTValue >> 8) & 0x3F);

                case 0x4000052: // BLDALPHA B0
                    return (byte)(BLDALPHAValue >> 0);
                case 0x4000053: // BLDALPHA B1
                    return (byte)(BLDALPHAValue >> 8);

                case 0x4000054: // BLDY
                    return (byte)Renderer.BlendBrightness;

            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    Renderer.BgMode = (uint)(val & 0b111);
                    Renderer.CgbMode = BitTest(val, 3);
                    Renderer.DisplayFrameSelect = BitTest(val, 4);
                    Renderer.HBlankIntervalFree = BitTest(val, 5);
                    Renderer.ObjCharacterVramMapping = BitTest(val, 6);
                    Renderer.ForcedBlank = BitTest(val, 7);

                    DISPCNTValue &= 0xFF00;
                    DISPCNTValue |= (ushort)(val << 0);

                    Renderer.BackgroundSettingsDirty = true;
                    break;
                case 0x4000001: // DISPCNT B1
                    Renderer.ScreenDisplayBg[0] = BitTest(val, 8 - 8);
                    Renderer.ScreenDisplayBg[1] = BitTest(val, 9 - 8);
                    Renderer.ScreenDisplayBg[2] = BitTest(val, 10 - 8);
                    Renderer.ScreenDisplayBg[3] = BitTest(val, 11 - 8);
                    Renderer.ScreenDisplayObj = BitTest(val, 12 - 8);
                    Renderer.Window0DisplayFlag = BitTest(val, 13 - 8);
                    Renderer.Window1DisplayFlag = BitTest(val, 14 - 8);
                    Renderer.ObjWindowDisplayFlag = BitTest(val, 15 - 8);
                    Renderer.AnyWindowEnabled = (val & 0b11100000) != 0;

                    DISPCNTValue &= 0x00FF;
                    DISPCNTValue |= (ushort)(val << 8);

                    Renderer.BackgroundSettingsDirty = true;
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
                    Renderer.Backgrounds[0].WriteBGCNT(addr - 0x4000008, val);
                    Renderer.BackgroundSettingsDirty = true;
                    break;
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    Renderer.Backgrounds[1].WriteBGCNT(addr - 0x400000A, val);
                    Renderer.BackgroundSettingsDirty = true;
                    break;
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    Renderer.Backgrounds[2].WriteBGCNT(addr - 0x400000C, val);
                    Renderer.BackgroundSettingsDirty = true;
                    break;
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    Renderer.Backgrounds[3].WriteBGCNT(addr - 0x400000E, val);
                    Renderer.BackgroundSettingsDirty = true;
                    break;

                case 0x4000010: // BG0HOFS B0
                case 0x4000011: // BG0HOFS B1
                case 0x4000012: // BG0VOFS B0
                case 0x4000013: // BG0VOFS B1
                    Renderer.Backgrounds[0].WriteBGOFS(addr - 0x4000010, val);
                    break;
                case 0x4000014: // BG1HOFS B0
                case 0x4000015: // BG1HOFS B1
                case 0x4000016: // BG1VOFS B0
                case 0x4000017: // BG1VOFS B1
                    Renderer.Backgrounds[1].WriteBGOFS(addr - 0x4000014, val);
                    break;
                case 0x4000018: // BG2HOFS B0
                case 0x4000019: // BG2HOFS B1
                case 0x400001A: // BG2VOFS B0
                case 0x400001B: // BG2VOFS B1
                    Renderer.Backgrounds[2].WriteBGOFS(addr - 0x4000018, val);
                    break;
                case 0x400001C: // BG3HOFS B0
                case 0x400001D: // BG3HOFS B1
                case 0x400001E: // BG3VOFS B0
                case 0x400001F: // BG3VOFS B1
                    Renderer.Backgrounds[3].WriteBGOFS(addr - 0x400001C, val);
                    break;

                case 0x4000020: // BG2PA B0
                case 0x4000021: // BG2PA B1
                case 0x4000022: // BG2PB B0
                case 0x4000023: // BG2PB B1
                case 0x4000024: // BG2PC B0
                case 0x4000025: // BG2PC B1
                case 0x4000026: // BG2PD B0
                case 0x4000027: // BG2PD B1
                    Renderer.Backgrounds[2].WriteBGPX(addr & 7, val);
                    break;
                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    Renderer.Backgrounds[2].WriteBGXY(addr & 7, val);
                    break;

                case 0x4000030: // BG3PA B0
                case 0x4000031: // BG3PA B1
                case 0x4000032: // BG3PB B0
                case 0x4000033: // BG3PB B1
                case 0x4000034: // BG3PC B0
                case 0x4000035: // BG3PC B1
                case 0x4000036: // BG3PD B0
                case 0x4000037: // BG3PD B1
                    Renderer.Backgrounds[3].WriteBGPX(addr & 7, val);
                    break;
                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    Renderer.Backgrounds[3].WriteBGXY(addr & 7, val);
                    break;

                case 0x4000040: // WIN0H B0
                    Renderer.Win0HRight = val;
                    break;
                case 0x4000041: // WIN0H B1
                    Renderer.Win0HLeft = val;
                    break;
                case 0x4000042: // WIN1H B0
                    Renderer.Win1HRight = val;
                    break;
                case 0x4000043: // WIN1H B1
                    Renderer.Win1HLeft = val;
                    break;

                case 0x4000044: // WIN0V B0
                    Renderer.Win0VBottom = val;
                    break;
                case 0x4000045: // WIN0V B1
                    Renderer.Win0VTop = val;
                    break;
                case 0x4000046: // WIN1V B0
                    Renderer.Win1VBottom = val;
                    break;
                case 0x4000047: // WIN1V B1
                    Renderer.Win1VTop = val;
                    break;

                case 0x4000048: // WININ B0
                    Renderer.Win0InEnable = (byte)(val & 0b111111U);

                    WININValue &= 0x7F00;
                    WININValue |= (ushort)(val << 0);
                    break;
                case 0x4000049: // WININ B1
                    Renderer.Win1InEnable = (byte)(val & 0b111111U);

                    WININValue &= 0x007F;
                    WININValue |= (ushort)(val << 8);
                    break;

                case 0x400004A: // WINOUT B0
                    Renderer.WinOutEnable = (byte)(val & 0b111111U);

                    WINOUTValue &= 0x7F00;
                    WINOUTValue |= (ushort)(val << 0);
                    break;
                case 0x400004B: // WINOUT B1
                    Renderer.WinObjEnable = (byte)(val & 0b111111U);

                    WINOUTValue &= 0x007F;
                    WINOUTValue |= (ushort)(val << 8);
                    break;

                case 0x400004C: // MOSAIC B0
                    MOSAICValueB0 = val;

                    Renderer.BgMosaicX = (byte)((val >> 0) & 0xF);
                    Renderer.BgMosaicY = (byte)((val >> 4) & 0xF);
                    break;
                case 0x400004D: // MOSAIC B1
                    MOSAICValueB1 = val;

                    Renderer.ObjMosaicX = (byte)((val >> 0) & 0xF);
                    Renderer.ObjMosaicY = (byte)((val >> 4) & 0xF);
                    break;

                case 0x4000050: // BLDCNT B0
                    Renderer.Target1Flags = val & 0b111111U;

                    Renderer.BlendEffect = (BlendEffect)((val >> 6) & 0b11U);

                    BLDCNTValue &= 0x7F00;
                    BLDCNTValue |= (ushort)(val << 0);
                    break;
                case 0x4000051: // BLDCNT B1
                    Renderer.Target2Flags = val & 0b111111U;

                    BLDCNTValue &= 0x00FF;
                    BLDCNTValue |= (ushort)(val << 8);
                    break;

                case 0x4000052: // BLDALPHA B0
                    Renderer.BlendACoeff = val & 0b11111U;
                    if (Renderer.BlendACoeff == 31) Renderer.BlendACoeff = 0;

                    BLDALPHAValue &= 0x7F00;
                    BLDALPHAValue |= (ushort)(val << 0);
                    break;
                case 0x4000053: // BLDALPHA B1
                    Renderer.BlendBCoeff = val & 0b11111U;
                    if (Renderer.BlendBCoeff == 31) Renderer.BlendBCoeff = 0;

                    BLDALPHAValue &= 0x00FF;
                    BLDALPHAValue |= (ushort)(val << 8);
                    break;

                case 0x4000054: // BLDY
                    Renderer.BlendBrightness = (byte)(val & 0b11111);
                    break;
            }
        }

        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(InterruptGba.HBlank);
            }

            if (Renderer.DebugEnableRendering)
            {
                Renderer.RenderScanline(Vram);
            }
            Renderer.IncrementMosaicCounters();

            Gba.Dma.RepeatHblank();
        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(InterruptGba.HBlank);
            }
        }

        public void EndHblank(long cyclesLate)
        {
            ScanlineStartCycles = Scheduler.CurrentTicks;

            if (Renderer.VCount != 227)
            {
                Renderer.VCount++;

                if (Renderer.VCount > 159)
                {
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndVblankToHblank);

                    if (Renderer.VCount == 160)
                    {
#if DS_RESOLUTION
                        while (VCount < HEIGHT) {
                            RenderScanline();
                            Renderer.IncrementMosaicCounters();
                            VCount++;
                        }
                        VCount = 160;
#endif

                        Gba.Dma.RepeatVblank();

                        Renderer.RunVblankOperations();

                        if (VBlankIrqEnable)
                        {
                            Gba.HwControl.FlagInterrupt(InterruptGba.VBlank);
                        }

                        Renderer.TotalFrames++;
                        if (Renderer.DebugEnableRendering) Renderer.SwapBuffers();

                        Renderer.RenderingDone = true;
                    }
                }
                else
                {
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);
                }
            }
            else
            {
                if (BiosMod)
                {
                    uint objE0 = 8 * 3;
                    uint objE1 = 8 * 19;
                    uint objM0 = 8 * 4;
                    uint objM1 = 8 * 20;

                    for (uint i = 0; i < 6; i++)
                    {
                        Renderer.Oam[objE0++] = 0;
                        Renderer.Oam[objE1++] = 0;
                    }

                    Renderer.Oam[objM0 + 4] = 68;
                    Renderer.Oam[objM0 + 5] |= 2;

                    if (BiosModLayer2)
                    {
                        Renderer.Oam[objM1 + 4] = 68;
                        Renderer.Oam[objM1 + 5] |= 2;
                    }

                    // uint objG0 = 8 * 6;
                    // uint objG1 = 8 * 5;
                    // uint objA0 = 8 * 22;
                    // uint objA1 = 8 * 21;
                }

                Renderer.VCount = 0;
                VCounterMatch = Renderer.VCount == VCountSetting;
                if (VCounterMatch && VCounterIrqEnable)
                {
                    Gba.HwControl.FlagInterrupt(InterruptGba.VCounterMatch);
                }
                Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);

                // Pre-render sprites for line zero
                fixed (byte* vram = Vram)
                {
                    if (Renderer.DebugEnableObj && Renderer.ScreenDisplayObj) Renderer.RenderObjs(vram, 0);
                }
            }

            VCounterMatch = Renderer.VCount == VCountSetting;

            if (VCounterMatch && VCounterIrqEnable)
            {
                Gba.HwControl.FlagInterrupt(InterruptGba.VCounterMatch);
            }
        }
    }
}
