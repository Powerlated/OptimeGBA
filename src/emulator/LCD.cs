using static OptimeGBA.Bits;
using System.Threading;
using System;

namespace OptimeGBA
{
    public class BgControl
    {
        int Priority = 0;
        int CharBaseBlock = 0;
        bool EnableMosaic = false;
        bool Use8BitColor = false;
        int MapBaseBlock = 0;
        bool OverflowWrap = false;
        int ScreenSize = 0;

        byte[] Value = new byte[2];

        public byte ReadHwio8(uint addr)
        {
            switch (addr)
            {
                case 0x00: // BGCNT B0
                    return Value[0];
                case 0x01: // BGCNT B1
                    return Value[1];
            }
            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x00: // BGCNT B0
                    Priority = (val >> 0) & 0b11;
                    CharBaseBlock = (val >> 2) & 0b11;
                    EnableMosaic = BitTest(val, 6);
                    Use8BitColor = BitTest(val, 7);

                    Value[0] = val;
                    break;
                case 0x01: // BGCNT B1
                    MapBaseBlock = (val >> 0) & 0b11111;
                    OverflowWrap = BitTest(val, 5);
                    ScreenSize = (val >> 6) & 0b11;

                    Value[1] = val;
                    break;
            }
        }
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
        public BgControl[] BgControl = new BgControl[4] {
            new BgControl(),
            new BgControl(),
            new BgControl(),
            new BgControl(),
        };

        // DISPCNT
        public uint Mode;
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

        public void SwapBuffers()
        {
            var temp = ScreenBack;
            ScreenBack = ScreenFront;
            ScreenFront = temp;
        }

        public byte[] Palettes = new byte[1024];
        public byte[,] ProcessedPalettes = new byte[512, 3];
        public byte[] Vram = new byte[98304];
        public byte[] Oam = new byte[1024];

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
                    val |= (byte)(Mode & 0b111);
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

                case 0x4000008: // BG0CNT B0
                case 0x4000009: // BG0CNT B1
                    return BgControl[0].ReadHwio8(addr - 0x4000008);
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    return BgControl[1].ReadHwio8(addr - 0x400000A);
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    return BgControl[2].ReadHwio8(addr - 0x400000C);
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    return BgControl[3].ReadHwio8(addr - 0x400000E);
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000000: // DISPCNT B0
                    Mode = (uint)(val & 0b111);
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
                    BgControl[0].WriteHwio8(addr - 0x4000008, val);
                    break;
                case 0x400000A: // BG1CNT B0
                case 0x400000B: // BG1CNT B1
                    BgControl[1].WriteHwio8(addr - 0x400000A, val);
                    break;
                case 0x400000C: // BG2CNT B0
                case 0x400000D: // BG2CNT B1
                    BgControl[2].WriteHwio8(addr - 0x400000C, val);
                    break;
                case 0x400000E: // BG3CNT B0
                case 0x400000F: // BG3CNT B1
                    BgControl[3].WriteHwio8(addr - 0x400000E, val);
                    break;
            }
        }

        public uint TotalFrames;

        public uint VCount;

        public uint CycleCount;
        public LCDEnum lcdEnum;
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
                                if (VCounterMatch && VCounterIrqEnable) Gba.HwControl.FlagInterrupt(Interrupt.VCounterMatch);
                                if (VCount > 159)
                                {
                                    lcdEnum = LCDEnum.VBlank;
                                    VBlank = true;

                                    if (VCount == 160)
                                    {
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
                                    RenderScanline();
                                }
                            }
                            else
                            {
                                VCount = 0;
                                VCounterMatch = VCount == VCountSetting;
                                if (VCounterMatch && VCounterIrqEnable) Gba.HwControl.FlagInterrupt(Interrupt.VCounterMatch);
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
            switch (Mode)
            {
                case 1:
                    RenderMode1();
                    return;
                case 3:
                    RenderMode3();
                    return;
                case 4:
                    RenderMode4();
                    return;
            }
        }

        public void RenderMode1()
        {
            uint screenBase = VCount * WIDTH * BYTES_PER_PIXEL;
            uint vramBase = 0x0 + (VCount * WIDTH);

            for (uint p = 0; p < WIDTH; p++)
            {
                uint vramVal = Vram[vramBase];

                ScreenBack[screenBase + 0] = ProcessedPalettes[0, 0];
                ScreenBack[screenBase + 1] = ProcessedPalettes[0, 1];
                ScreenBack[screenBase + 2] = ProcessedPalettes[0, 2];

                vramBase++;
                screenBase += BYTES_PER_PIXEL;
            }
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