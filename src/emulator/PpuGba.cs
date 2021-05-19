using static OptimeGBA.Bits;
using static OptimeGBA.PpuRenderer;
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

    public sealed unsafe partial class PpuGba
    {
        Device DeviceUnit;
        Scheduler Scheduler;

        public PpuRenderer Renderer = new PpuRenderer(240, 160);

        public PpuGba(Device deviceUnit, Scheduler scheduler)
        {
            DeviceUnit = deviceUnit;
            Scheduler = scheduler;

            Scheduler.AddEventRelative(SchedulerId.Ppu, 960, EndDrawingToHblank);
        }

        public long ScanlineStartCycles;

        public bool BiosMod = false;
        public bool BiosModLayer2 = false;
        public sbyte[] OamColorOffsets = new sbyte[128];

        public ushort DISPCNTValue;
        public ushort WININValue;
        public ushort WINOUTValue;
        public ushort BLDCNTValue;
        public uint BLDALPHAValue;

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

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    return Renderer.Backgrounds[2].ReadBGXY(addr - 0x04000028);

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    return Renderer.Backgrounds[3].ReadBGXY(addr - 0x04000038);

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

                case 0x4000028: // BG2X B0
                case 0x4000029: // BG2X B1
                case 0x400002A: // BG2X B2
                case 0x400002B: // BG2X B3
                case 0x400002C: // BG2Y B0
                case 0x400002D: // BG2Y B1
                case 0x400002E: // BG2Y B2
                case 0x400002F: // BG2Y B3
                    Renderer.Backgrounds[2].WriteBGXY(addr - 0x04000028, val);
                    break;

                case 0x4000038: // BG3X B0
                case 0x4000039: // BG3X B1
                case 0x400003A: // BG3X B2
                case 0x400003B: // BG3X B3
                case 0x400003C: // BG3Y B0
                case 0x400003D: // BG3Y B1
                case 0x400003E: // BG3Y B2
                case 0x400003F: // BG3Y B3
                    Renderer.Backgrounds[3].WriteBGXY(addr - 0x04000038, val);
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
                    Renderer.Win0InEnable = val & 0b111111U;

                    WININValue &= 0x7F00;
                    WININValue |= (ushort)(val << 0);
                    break;
                case 0x4000049: // WININ B1
                    Renderer.Win1InEnable = val & 0b111111U;

                    WININValue &= 0x007F;
                    WININValue |= (ushort)(val << 8);
                    break;

                case 0x400004A: // WINOUT B0
                    Renderer.WinOutEnable = val & 0b111111U;

                    WINOUTValue &= 0x7F00;
                    WINOUTValue |= (ushort)(val << 0);
                    break;
                case 0x400004B: // WINOUT B1
                    Renderer.WinObjEnable = val & 0b111111U;

                    WINOUTValue &= 0x007F;
                    WINOUTValue |= (ushort)(val << 8);
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

            DeviceUnit.Dma.RepeatHblank();

            if (HBlankIrqEnable)
            {
                DeviceUnit.HwControl.FlagInterrupt(InterruptGba.HBlank);
            }

        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                DeviceUnit.HwControl.FlagInterrupt(InterruptGba.HBlank);
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
                            VCount++;
                        }
                        VCount = 160;
#endif

                        DeviceUnit.Dma.RepeatVblank();

                        if (VBlankIrqEnable)
                        {
                            DeviceUnit.HwControl.FlagInterrupt(InterruptGba.VBlank);
                        }

                        Renderer.TotalFrames++;
                        if (Renderer.DebugEnableRendering) Renderer.SwapBuffers();

                        Renderer.RenderingDone = true;
                    }
                }
                else
                {
                    if (Renderer.DebugEnableRendering) Renderer.RenderScanline();
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
                    DeviceUnit.HwControl.FlagInterrupt(InterruptGba.VCounterMatch);
                }
                Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);

                // Pre-render sprites for line zero
                if (Renderer.DebugEnableObj && Renderer.ScreenDisplayObj) Renderer.RenderObjs(0);
                if (Renderer.DebugEnableRendering) Renderer.RenderScanline();
            }

            VCounterMatch = Renderer.VCount == VCountSetting;

            if (VCounterMatch && VCounterIrqEnable)
            {
                DeviceUnit.HwControl.FlagInterrupt(InterruptGba.VCounterMatch);
            }
        }
    }
}
