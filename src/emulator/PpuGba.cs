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

            /*
            ⠀⠀⠀⡯⡯⡾⠝⠘⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⢊⠘⡮⣣⠪⠢⡑⡌
            ⠀⠀⠀⠟⠝⠈⠀⠀⠀⠡⠀⠠⢈⠠⢐⢠⢂⢔⣐⢄⡂⢔⠀⡁⢉⠸⢨⢑⠕⡌
            ⠀⠀⡀⠁⠀⠀⠀⡀⢂⠡⠈⡔⣕⢮⣳⢯⣿⣻⣟⣯⣯⢷⣫⣆⡂⠀⠀⢐⠑⡌
            ⢀⠠⠐⠈⠀⢀⢂⠢⡂⠕⡁⣝⢮⣳⢽⡽⣾⣻⣿⣯⡯⣟⣞⢾⢜⢆⠀⡀⠀⠪
            ⣬⠂⠀⠀⢀⢂⢪⠨⢂⠥⣺⡪⣗⢗⣽⢽⡯⣿⣽⣷⢿⡽⡾⡽⣝⢎⠀⠀⠀⢡
            ⣿⠀⠀⠀⢂⠢⢂⢥⢱⡹⣪⢞⡵⣻⡪⡯⡯⣟⡾⣿⣻⡽⣯⡻⣪⠧⠑⠀⠁⢐
            ⣿⠀⠀⠀⠢⢑⠠⠑⠕⡝⡎⡗⡝⡎⣞⢽⡹⣕⢯⢻⠹⡹⢚⠝⡷⡽⡨⠀⠀⢔
            ⣿⡯⠀⢈⠈⢄⠂⠂⠐⠀⠌⠠⢑⠱⡱⡱⡑⢔⠁⠀⡀⠐⠐⠐⡡⡹⣪⠀⠀⢘
            ⣿⣽⠀⡀⡊⠀⠐⠨⠈⡁⠂⢈⠠⡱⡽⣷⡑⠁⠠⠑⠀⢉⢇⣤⢘⣪⢽⠀⢌⢎
            ⣿⢾⠀⢌⠌⠀⡁⠢⠂⠐⡀⠀⢀⢳⢽⣽⡺⣨⢄⣑⢉⢃⢭⡲⣕⡭⣹⠠⢐⢗
            ⣿⡗⠀⠢⠡⡱⡸⣔⢵⢱⢸⠈⠀⡪⣳⣳⢹⢜⡵⣱⢱⡱⣳⡹⣵⣻⢔⢅⢬⡷
            ⣷⡇⡂⠡⡑⢕⢕⠕⡑⠡⢂⢊⢐⢕⡝⡮⡧⡳⣝⢴⡐⣁⠃⡫⡒⣕⢏⡮⣷⡟
            ⣷⣻⣅⠑⢌⠢⠁⢐⠠⠑⡐⠐⠌⡪⠮⡫⠪⡪⡪⣺⢸⠰⠡⠠⠐⢱⠨⡪⡪⡰
            ⣯⢷⣟⣇⡂⡂⡌⡀⠀⠁⡂⠅⠂⠀⡑⡄⢇⠇⢝⡨⡠⡁⢐⠠⢀⢪⡐⡜⡪⡊
            ⣿⢽⡾⢹⡄⠕⡅⢇⠂⠑⣴⡬⣬⣬⣆⢮⣦⣷⣵⣷⡗⢃⢮⠱⡸⢰⢱⢸⢨⢌
            ⣯⢯⣟⠸⣳⡅⠜⠔⡌⡐⠈⠻⠟⣿⢿⣿⣿⠿⡻⣃⠢⣱⡳⡱⡩⢢⠣⡃⠢⠁
            ⡯⣟⣞⡇⡿⣽⡪⡘⡰⠨⢐⢀⠢⢢⢄⢤⣰⠼⡾⢕⢕⡵⣝⠎⢌⢪⠪⡘⡌⠀
            ⡯⣳⠯⠚⢊⠡⡂⢂⠨⠊⠔⡑⠬⡸⣘⢬⢪⣪⡺⡼⣕⢯⢞⢕⢝⠎⢻⢼⣀⠀
            ⠁⡂⠔⡁⡢⠣⢀⠢⠀⠅⠱⡐⡱⡘⡔⡕⡕⣲⡹⣎⡮⡏⡑⢜⢼⡱⢩⣗⣯⣟
            ⢀⢂⢑⠀⡂⡃⠅⠊⢄⢑⠠⠑⢕⢕⢝⢮⢺⢕⢟⢮⢊⢢⢱⢄⠃⣇⣞⢞⣞⢾
            ⢀⠢⡑⡀⢂⢊⠠⠁⡂⡐⠀⠅⡈⠪⠪⠪⠣⠫⠑⡁⢔⠕⣜⣜⢦⡰⡎⡯⡾⡽
            */
            // if (Gba.Provider.BootBios)
            // {
            //     BiosMod = true;
            //     // 250 frames
            //     Scheduler.AddEventRelative(SchedulerId.None, 70224000, DisableBiosMod);
            //     // 120 frames
            //     Scheduler.AddEventRelative(SchedulerId.None, 33707520, EnableBiosModLayer2);
            // }
        }

        public byte[] Vram = MemoryUtil.AllocateManagedArray(98304);

        public long ScanlineStartCycles;

        public bool BiosMod = false;
        public bool BiosModLayer2 = false;
        public sbyte[] OamColorOffsets = new sbyte[128];

        public ushort DISPCNTValue;

        // DISPSTAT        
        public bool VCounterMatch;
        public bool VBlankIrqEnable;
        public bool HBlankIrqEnable;
        public bool VCounterIrqEnable;
        public byte VCountSetting;

        // State
        public uint VCount;

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

                default:
                    return Renderer.ReadHwio8(addr & 0xFF);
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
                    Renderer.ObjCharOneDimensional = BitTest(val, 6);
                    Renderer.ForcedBlank = BitTest(val, 7);

                    DISPCNTValue &= 0xFF00;
                    DISPCNTValue |= (ushort)(val << 0);

                    Renderer.BackgroundSettingsDirty = true;
                    return;
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
                    return;

                case 0x4000004: // DISPSTAT B0
                    VBlankIrqEnable = BitTest(val, 3);
                    HBlankIrqEnable = BitTest(val, 4);
                    VCounterIrqEnable = BitTest(val, 5);
                    return;
                case 0x4000005: // DISPSTAT B1
                    VCountSetting = val;
                    return;

                default:
                    Renderer.WriteHwio8(addr & 0xFF, val);
                    return;
            }

        }

        public void EndDrawingToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt((uint)InterruptGba.HBlank);
            }

            if (Renderer.DebugEnableRendering)
            {
                Renderer.RenderScanlineGba(VCount, Vram);
            }
            Renderer.IncrementMosaicCounters();

            Gba.Dma.RepeatHblank();
        }

        public void EndVblankToHblank(long cyclesLate)
        {
            Scheduler.AddEventRelative(SchedulerId.Ppu, 272 - cyclesLate, EndHblank);

            if (HBlankIrqEnable)
            {
                Gba.HwControl.FlagInterrupt((uint)InterruptGba.HBlank);
            }
        }

        public void EndHblank(long cyclesLate)
        {
            ScanlineStartCycles = Scheduler.CurrentTicks;

            if (VCount != 227)
            {
                VCount++;

                if (VCount > 159)
                {
                    Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndVblankToHblank);

                    if (VCount == 160)
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
                            Gba.HwControl.FlagInterrupt((uint)InterruptGba.VBlank);
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

                VCount = 0;
                VCounterMatch = VCount == VCountSetting;
                if (VCounterMatch && VCounterIrqEnable)
                {
                    Gba.HwControl.FlagInterrupt((uint)InterruptGba.VCounterMatch);
                }
                Scheduler.AddEventRelative(SchedulerId.Ppu, 960 - cyclesLate, EndDrawingToHblank);

                // Pre-render sprites for line zero
                fixed (byte* vram = Vram)
                {
                    if (Renderer.DebugEnableObj && Renderer.ScreenDisplayObj) Renderer.RenderObjs(0, vram);
                }
            }

            VCounterMatch = VCount == VCountSetting;

            if (VCounterMatch && VCounterIrqEnable)
            {
                Gba.HwControl.FlagInterrupt((uint)InterruptGba.VCounterMatch);
            }
        }
    }
}
