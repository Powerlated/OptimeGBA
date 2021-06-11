using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL;
using System;
using System.IO;
using ImGuiNET;
using System.Threading;
using ImGuiUtils;
using static Util;
using System.Collections.Generic;
using OptimeGBA;
using System.Text;
using System.Runtime.InteropServices;
using static SDL2.SDL;
using static OptimeGBAEmulator.Window;
using System.Linq;
using System.Numerics;
using static OptimeGBA.Bits;

namespace OptimeGBAEmulator
{
    public unsafe class WindowNds
    {
        GameWindow Window;

        int[] screenTexIds = new int[2];
        int[] palTexIds = new int[4];
        ImGuiController _controller;
        int VertexBufferObject;
        int VertexArrayObject;

        string[] Log;
        int LogIndex = -0;

        Nds Nds;
        Thread EmulationThread;
        AutoResetEvent ThreadSync = new AutoResetEvent(false);

        static bool SyncToAudio = true;

        const uint AUDIO_SAMPLE_THRESHOLD = 1024;
        const uint AUDIO_SAMPLE_FULL_THRESHOLD = 1024;
        const int SAMPLES_PER_CALLBACK = 32;
        const int CyclesPerFrameNds = 560190;

        static SDL_AudioSpec want, have;
        static uint AudioDevice;

        static bool LogHwioAccesses;


        public int ThreadCyclesQueued;
        public void EmulationThreadHandler()
        {
            SDL_Init(SDL_INIT_AUDIO);

            want.channels = 2;
            want.freq = 32768;
            want.samples = SAMPLES_PER_CALLBACK;
            want.format = AUDIO_S16LSB;
            // want.callback = NeedMoreAudioCallback;
            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);

            while (true)
            {
                ThreadSync.WaitOne();

                int cyclesLeft = CyclesPerFrameNds;
                while (cyclesLeft > 0 && !Nds.Nds7.Cpu.Errored && !Nds.Nds9.Cpu.Errored)
                {
                    cyclesLeft -= (int)Nds.Step();
                    CheckBreakpoints();
                }

                while (!SyncToAudio && !Nds.Nds7.Cpu.Errored && !Nds.Nds9.Cpu.Errored && RunEmulator)
                {
                    Nds.Step();
                    CheckBreakpoints();
                    ThreadCyclesQueued = 0;
                }
            }
        }

        public uint Arm9Breakpoint = 0x02324862;
        public uint Arm7Breakpoint = 0x1CD22342;
        public bool EnableBreakpoints = true;

        public void CheckBreakpoints()
        {
            var arm9 = Nds.Nds9.Cpu;
            var arm7 = Nds.Nds7.Cpu;

            uint addr9 = GetCurrentInstrAddr(arm9);
            uint addr7 = GetCurrentInstrAddr(arm7);

            if (EnableBreakpoints)
            {
                if (Arm9Breakpoint == addr9) arm9.Error("Breakpoint hit");
                if (Arm7Breakpoint == addr7) arm7.Error("Breakpoint hit");
            }

            if (addr9 >= 0x10000000 && addr9 <= 0xF0000000)
            {
                arm9.Error("wtf???");
            }

            // if (arm9.GetInstructionArm(arm9.LastIns) == Arm.SWI) arm9.Error("damn it");
        }

        static bool FrameNow = false;

        /*
        public short[] AudioArray = new short[SAMPLES_PER_CALLBACK * 2];
        public void NeedMoreAudioCallback(IntPtr userdata, IntPtr stream, int len)
        {
            if (RunEmulator)
            {
                // const uint CyclesPerSample = 16777216 / 32768;
                // if (Nds.NdsAudio.SampleBuffer.Entries / 2 < 4096)
                // {
                //     ThreadCyclesQueued += (int)(CyclesPerSample * SAMPLES_PER_CALLBACK * 4);
                // }

                // ThreadCyclesQueued += (int)(CyclesPerSample * SAMPLES_PER_CALLBACK * 4);
                // ThreadSync.Set();

                for (uint i = 0; i < SAMPLES_PER_CALLBACK * 2; i++)
                {
                    AudioArray[i] = Nds.NdsAudio.SampleBuffer.Pop();
                }

                int bytes = sizeof(short) * AudioArray.Length;
                Marshal.Copy(AudioArray, 0, stream, AudioArray.Length);
            }
            else
            {
                for (int i = 0; i < len; i++)
                {
                    Marshal.WriteByte(stream, i, 0);
                }
            }
        }
        */

        public void RunCycles(int cycles)
        {
            while (cycles > 0 && !Nds.Nds7.Cpu.Errored && !Nds.Nds9.Cpu.Errored && RunEmulator)
            {
                cycles -= (int)Nds.Step();
            }
        }

        int CyclesLeft;
        public void RunFrame()
        {
            CyclesLeft += FrameCycles;
            while (CyclesLeft > 0 && !Nds.Nds7.Cpu.Errored && !Nds.Nds9.Cpu.Errored)
            {
                CyclesLeft -= (int)Nds.Step();
            }
        }

        public void RunScanline()
        {
            CyclesLeft += ScanlineCycles;
            while (CyclesLeft > 0 && !Nds.Nds7.Cpu.Errored && !Nds.Nds9.Cpu.Errored)
            {
                CyclesLeft -= (int)Nds.Step();
            }
        }

        public void RunAudioSync()
        {
            if (GetAudioSamplesInQueue() < AUDIO_SAMPLE_THRESHOLD || !SyncToAudio)
            {
                RunFrame();
            }
        }

        bool RunEmulator = false;

        public static uint GetAudioSamplesInQueue()
        {
            return SDL_GetQueuedAudioSize(AudioDevice) / sizeof(short);
        }

        public WindowNds(GameWindow window)
        {
            Window = window;

            // Init SDL
            byte[] bios7 = System.IO.File.ReadAllBytes("bios7.bin");
            byte[] bios9 = System.IO.File.ReadAllBytes("bios9.bin");
            byte[] firmware = System.IO.File.ReadAllBytes("firmware.bin");
            Nds = new Nds(new ProviderNds(bios7, bios9, firmware, new byte[0], "", AudioReady) { DirectBoot = false });

            EmulationThread = new Thread(EmulationThreadHandler);
            EmulationThread.Name = "Emulation Core";
            EmulationThread.Start();

            string file = "";
            Log = file.Split('\n');

            SetupRegViewer();
        }

        static IntPtr AudioTempBufPtr = Marshal.AllocHGlobal(16384);
        static void AudioReady(short[] data)
        {
            // Don't queue audio if too much is in buffer
            if (SyncToAudio || GetAudioSamplesInQueue() < AUDIO_SAMPLE_FULL_THRESHOLD)
            {
                int bytes = sizeof(short) * data.Length;

                Marshal.Copy(data, 0, AudioTempBufPtr, data.Length);

                // Console.WriteLine("Outputting samples to SDL");

                SDL_QueueAudio(AudioDevice, AudioTempBufPtr, (uint)bytes);
            }
        }

        public void OnLoad()
        {
            GL.GenTextures(2, screenTexIds);
            GL.GenTextures(4, palTexIds);

            Window.VSync = VSyncMode.Off;
            Window.UpdateFrequency = 59.7275;
        }

        public void LoadRomAndSave(byte[] rom, byte[] sav, string savPath)
        {
            var bios7 = Nds.Provider.Bios7;
            var bios9 = Nds.Provider.Bios9;
            var firmware = Nds.Provider.Firmware;
            Nds = new Nds(new ProviderNds(bios7, bios9, firmware, rom, savPath, AudioReady) { DirectBoot = false });
            // Nds.Mem.SaveProvider.LoadSave(sav);
        }

        public double Time;
        public bool RecordTime;
        public uint RecordStartFrames;

        public void OnUpdateFrame(FrameEventArgs e)
        {
            Nds.Keypad.B = Window.KeyboardState.IsKeyDown(Keys.Z);
            Nds.Keypad.A = Window.KeyboardState.IsKeyDown(Keys.X);
            Nds.Keypad.Left = Window.KeyboardState.IsKeyDown(Keys.Left);
            Nds.Keypad.Up = Window.KeyboardState.IsKeyDown(Keys.Up);
            Nds.Keypad.Right = Window.KeyboardState.IsKeyDown(Keys.Right);
            Nds.Keypad.Down = Window.KeyboardState.IsKeyDown(Keys.Down);
            Nds.Keypad.Start = Window.KeyboardState.IsKeyDown(Keys.Enter) || Window.KeyboardState.IsKeyDown(Keys.KeyPadEnter);
            Nds.Keypad.Select = Window.KeyboardState.IsKeyDown(Keys.Backspace);
            Nds.Keypad.L = Window.KeyboardState.IsKeyDown(Keys.Q);
            Nds.Keypad.R = Window.KeyboardState.IsKeyDown(Keys.E);

            SyncToAudio = !(Window.KeyboardState.IsKeyDown(Keys.Tab) || Window.KeyboardState.IsKeyDown(Keys.Space));
            // SyncToAudio = false;

            if (RunEmulator)
            {
                FrameNow = true;
                ThreadSync.Set();
            }

            if (RecordTime)
            {
                Time += e.Time;
            }

            if (Nds.Nds7.Mem.SaveProvider.Dirty)
            {
                DumpSav();
            }
        }

        const int FrameCycles = 560190;
        const int ScanlineCycles = 2130;

        public void OnRenderFrame(FrameEventArgs e)
        {
            DrawDisplay();
            DrawSchedulerInfo();
            DrawDebug();
            DrawInstrViewer();
            DrawInstrInfo();
            DrawRegViewer();
            DrawSoundVisualizer();
            DrawMemoryViewer();
            DrawHwioLog();
            DrawBankedRegisters();
            DrawCpuProfiler();
            DrawInterruptStatus();
        }

        public void ResetNds()
        {
            byte[] save = Nds.Nds7.Mem.SaveProvider.GetSave();
            ProviderNds p = Nds.Provider;
            Nds = new Nds(p);
            Nds.Nds7.Mem.SaveProvider.LoadSave(save);
        }

        static int MemoryViewerInit = 1;
        int MemoryViewerCurrent = MemoryViewerInit;
        uint MemoryViewerCurrentAddr = baseAddrs[MemoryViewerInit];
        uint MemoryViewerHoverAddr = 0;
        uint MemoryViewerHoverVal = 0;
        bool MemoryViewerHover = false;
        byte[] MemoryViewerGoToAddr = new byte[16];

        public void DrawBankedRegisters()
        {
            if (ImGui.Begin("Banked Registers"))
            {
                ImGui.Columns(5);

                ImGui.Text("User");
                ImGui.Text("R13: " + Hex(Nds.Nds7.Cpu.R13usr, 8));
                ImGui.Text("R14: " + Hex(Nds.Nds7.Cpu.R14usr, 8));

                ImGui.NextColumn();

                ImGui.Text("Supervisor");
                ImGui.Text("R13: " + Hex(Nds.Nds7.Cpu.R13svc, 8));
                ImGui.Text("R14: " + Hex(Nds.Nds7.Cpu.R14svc, 8));

                ImGui.NextColumn();

                ImGui.Text("Abort");
                ImGui.Text("R13: " + Hex(Nds.Nds7.Cpu.R13abt, 8));
                ImGui.Text("R14: " + Hex(Nds.Nds7.Cpu.R14abt, 8));

                ImGui.NextColumn();

                ImGui.Text("IRQ");
                ImGui.Text("R13: " + Hex(Nds.Nds7.Cpu.R13irq, 8));
                ImGui.Text("R14: " + Hex(Nds.Nds7.Cpu.R14irq, 8));

                ImGui.NextColumn();

                ImGui.Text("Undefined");
                ImGui.Text("R13: " + Hex(Nds.Nds7.Cpu.R13und, 8));
                ImGui.Text("R14: " + Hex(Nds.Nds7.Cpu.R14und, 8));

                ImGui.End();
            }
        }

        static String[] baseNames = {
                    "ITCM",
                    "BIOS DTCM",
                    "Main Memory",
                    "Upper Main Memory",
                    "Shared Memory",
                    "Engine A BG VRAM",
                    "Engine A BG VRAM 0x6018000",
                    "Engine A OBJ VRAM",
                    "OAM",
                };

        static uint[] baseAddrs = {
                0x00000000,
                0x00800000,
                0x02000000,
                0x027FF800,
                0x03000000,
                0x06000000,
                0x06018000,
                0x06400000,
                0x07000000,
            };

        public void DrawMemoryViewer()
        {

            int rows = 2048;
            int cols = 16;

            if (ImGui.Begin("Memory Viewer ARM9"))
            {
                if (ImGui.BeginCombo("", $"{baseNames[MemoryViewerCurrent]}: {Hex(baseAddrs[MemoryViewerCurrent], 8)}"))
                {
                    for (int n = 0; n < baseNames.Length; n++)
                    {
                        bool isSelected = (MemoryViewerCurrent == n);
                        String display = $"{baseNames[n]}: {Hex(baseAddrs[n], 8)}";
                        if (ImGui.Selectable(display, isSelected))
                        {
                            MemoryViewerCurrent = n;
                            MemoryViewerCurrentAddr = baseAddrs[n];
                        }
                        if (isSelected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                };

                // ImGui.InputText("", MemoryViewerGoToAddr, (uint)MemoryViewerGoToAddr.Length);
                // if (ImGui.Button("Go To"))
                // {
                //     try
                //     {
                //         String s = System.Text.Encoding.ASCII.GetString(MemoryViewerGoToAddr);
                //         MemoryViewerCurrentAddr = uint.Parse(s);
                //     }
                //     catch (Exception e)
                //     {
                //         Console.Error.WriteLine(e);
                //     }
                // }

                uint tempBase = MemoryViewerCurrentAddr;
                if (MemoryViewerHover)
                {
                    ImGui.Text($"Addr: {HexN(MemoryViewerHoverAddr, 8)}");
                    ImGui.SameLine(); ImGui.Text($"Val: {HexN(MemoryViewerHoverVal, 2)}");
                }
                else
                {
                    ImGui.Text("");
                }

                ImGui.Separator();

                MemoryViewerHover = false;

                ImGui.BeginChild("Memory");
                for (int i = 0; i < rows; i++)
                {
                    ImGui.Text($"{Util.HexN(tempBase, 8)}:");
                    for (int j = 0; j < cols; j++)
                    {
                        uint val = Nds.Nds9.Mem.Read8(tempBase);

                        ImGui.SameLine();
                        ImGui.Selectable($"{HexN(val, 2)}");


                        if (ImGui.IsItemHovered())
                        {
                            MemoryViewerHover = true;
                            MemoryViewerHoverAddr = tempBase;
                            MemoryViewerHoverVal = val;
                        }

                        tempBase++;
                    }
                }
                ImGui.EndChild();
                ImGui.End();
            }
        }

        public String BuildLogText()
        {
            if (LogIndex < Log.Length)
            {
                if (Log[LogIndex].Length > 154)
                {
                    return Log[LogIndex].Substring(0, 135) + Log[LogIndex].Substring(144, 14) + $" {LogIndex + 1}";
                }
                else
                {
                    return "<bad log>";
                }
            }
            else
            {
                return "<log past end>";
            }

        }

        public String BuildEmuText()
        {
            String text = "";
            text += $"{HexN(Nds.Nds7.Cpu.R[0], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[1], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[2], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[3], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[4], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[5], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[6], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[7], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[8], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[9], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[10], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[11], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[12], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[13], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[14], 8)} ";
            text += $"{HexN(Nds.Nds7.Cpu.R[15], 8)} ";
            text += $"cpsr: {HexN(Nds.Nds7.Cpu.GetCPSR(), 8)} ";
            String emuText = text.Substring(0, 135) + text.Substring(144, 14) + $" {LogIndex + 1}";
            return emuText;
        }

        int DebugStepFor = 0;
        byte[] text = new byte[4];

        public void DrawInstrInfo()
        {
            if (ImGui.Begin("Instruction Info"))
            {
                String logText = BuildLogText();
                String emuText = BuildEmuText();

                if (LogIndex >= 0)
                    ImGui.Text(logText);
                ImGui.Separator();
                if (emuText != logText)
                {
                    ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.0f, 0.0f, 1.0f), emuText);
                }
                else
                {
                    ImGui.Text(emuText);
                }

                ImGui.Separator();
                ImGui.Text(Nds.Nds7.Cpu.Debug);
                ImGui.Separator();
                ImGui.Text(Nds.Nds9.Cpu.Debug);
                ImGui.End();
            }
        }

        uint[] PaletteImageBuffer = new uint[16 * 16];

        public void drawCpuInfo(Arm7 arm7)
        {
            ImGui.Text($"R0:  {Hex(arm7.R[0], 8)}");
            ImGui.Text($"R1:  {Hex(arm7.R[1], 8)}");
            ImGui.Text($"R2:  {Hex(arm7.R[2], 8)}");
            ImGui.Text($"R3:  {Hex(arm7.R[3], 8)}");
            ImGui.Text($"R4:  {Hex(arm7.R[4], 8)}");
            ImGui.Text($"R5:  {Hex(arm7.R[5], 8)}");
            ImGui.Text($"R6:  {Hex(arm7.R[6], 8)}");
            ImGui.Text($"R7:  {Hex(arm7.R[7], 8)}");
            ImGui.Text($"R8:  {Hex(arm7.R[8], 8)}");
            ImGui.Text($"R9:  {Hex(arm7.R[9], 8)}");
            ImGui.Text($"R10: {Hex(arm7.R[10], 8)}");
            ImGui.Text($"R11: {Hex(arm7.R[11], 8)}");
            ImGui.Text($"R12: {Hex(arm7.R[12], 8)}");
            ImGui.Text($"R13: {Hex(arm7.R[13], 8)}");
            ImGui.Text($"R14: {Hex(arm7.R[14], 8)}");
            ImGui.Text($"R15: {Hex(arm7.R[15], 8)}");
            ImGui.Text($"CPSR: {Hex(arm7.GetCPSR(), 8)}");
            // ImGui.Text($"Instruction: {Hex(arm7.LastIns, arm7.ThumbState ? 4 : 8)}");
            ImGui.Text($"Prev. Ins.: {Hex(arm7.LastIns, arm7.LastThumbState ? 4 : 8)}");
            ImGui.Text($"Disasm: {(arm7.LastThumbState ? disasmThumb((ushort)arm7.LastIns) : disasmArm(arm7.LastIns))}");
            ImGui.Text($"Decoded as: {(arm7.LastThumbState ? arm7.GetInstructionThumb((ushort)arm7.LastIns).Method.Name : arm7.GetInstructionArm(arm7.LastIns).Method.Name)}");

            ImGui.Text($"Mode: {arm7.Mode}");
            ImGui.Text($"Last Cycles: {arm7.InstructionCycles}");
            ImGui.Text($"Total Instrs.: {arm7.InstructionsRan}");
            ImGui.Text($"Pipeline: {arm7.Pipeline}");

            // bool Negative = arm7.Negative;
            // bool Zero = arm7.Zero;
            // bool Carry = arm7.Carry;
            // bool Overflow = arm7.Overflow;
            // bool Sticky = arm7.Sticky;
            // bool IRQDisable = arm7.IRQDisable;
            // bool FIQDisable = arm7.FIQDisable;
            // bool ThumbState = arm7.ThumbState;

            // ImGui.Checkbox("Negative", ref Negative);
            // ImGui.Checkbox("Zero", ref Zero);
            // ImGui.Checkbox("Carry", ref Carry);
            // ImGui.Checkbox("Overflow", ref Overflow);
            // ImGui.Checkbox("Sticky", ref Sticky);
            // ImGui.Checkbox("IRQ Disable", ref IRQDisable);
            // ImGui.Checkbox("FIQ Disable", ref FIQDisable);
            // ImGui.Checkbox("Thumb State", ref ThumbState);

        }

        public void DrawDebug()
        {
            if (ImGui.Begin("Debug"))
            {
                ImGui.Columns(5);
                ImGui.Text("ARM9");
                drawCpuInfo(Nds.Nds9.Cpu);
                displayCheckbox("IRQ Disable", Nds.Nds9.Cpu.IRQDisable);
                ImGui.Checkbox("Disable ARM9", ref Nds.DebugDisableArm9);
                ImGui.NextColumn();
                ImGui.Text("ARM7");
                drawCpuInfo(Nds.Nds7.Cpu);
                displayCheckbox("IRQ Disable", Nds.Nds7.Cpu.IRQDisable);
                ImGui.Checkbox("Disable ARM7", ref Nds.DebugDisableArm7);
                ImGui.Text($"Total Steps: " + Nds.Steps);
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                // ImGui.Text($"Ins Next Up: {(Nds.Nds7.Cpu.ThumbState ? Hex(Nds.Nds7.Cpu.THUMBDecode, 4) : Hex(Nds.Nds7.Cpu.ARMDecode, 8))}");

                ImGui.Text($"");

                if (ImGui.Button("flag ARM7 RTC IRQ"))
                {
                    Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.Rtc);
                }

                if (ImGui.Button("Reset"))
                {
                    ResetNds();
                }

                if (ImGui.Button("Frame Advance"))
                {
                    RunFrame();
                }

                if (ImGui.Button("Scanline Advance"))
                {
                    RunScanline();
                }


                // if (ImGui.Button("Start Time"))
                // {
                //     RecordTime = true;
                //     Time = 0;
                //     RecordStartFrames = Nds.Ppu.TotalFrames;
                // }

                // if (ImGui.Button("Stop Time"))
                // {
                //     RecordTime = false;
                // }

                if (ImGui.Button("Un-error"))
                {
                    Nds.Nds7.Cpu.Errored = false;
                    Nds.Nds9.Cpu.Errored = false;
                }
                if (ImGui.Button("Step"))
                {
                    Nds.Step();
                    LogIndex++;
                }
                if (ImGui.Button("Step until ARM9 unhalted"))
                {
                    while (Nds.Nds9.Cpu.Halted)
                    {
                        Nds.Step();
                    }
                }
                // if (ImGui.Button("Step Until Error"))
                // {
                //     bool exit = false;
                //     while (!Nds.Nds7.Cpu.Errored && !exit)
                //     {

                //         Nds.Step();
                //         LogIndex++;

                //         if (BuildEmuText() != BuildLogText())
                //         {
                //             exit = true;
                //         }
                //     }
                // }

                ImGui.InputInt("Instrs", ref DebugStepFor);
                if (ImGui.Button("Step For"))
                {
                    int num = DebugStepFor;
                    while (num > 0 && !Nds.Nds7.Cpu.Errored)
                    {
                        Nds.Step();
                        num--;
                    }
                }

                var times = 5000000;
                if (ImGui.Button("Step " + times))
                {
                    using (StreamWriter file7 = new StreamWriter("log7.txt"), file9 = new StreamWriter("log9.txt"))
                    {
                        Action nds7Executed = () =>
                        {
                            file7.WriteLine(BuildEmuFullText(Nds.Nds7.Cpu));
                            if (Nds.Nds7.Cpu.InterruptServiced)
                            {
                                Nds.Nds7.Cpu.InterruptServiced = false;
                                file7.WriteLine("---------------- INTERRUPT ----------------");
                            }
                        };

                        Action nds9Executed = () =>
                        {
                            file9.WriteLine(BuildEmuFullText(Nds.Nds9.Cpu));
                            if (Nds.Nds9.Cpu.InterruptServiced)
                            {
                                Nds.Nds9.Cpu.InterruptServiced = false;
                                file9.WriteLine("---------------- INTERRUPT ----------------");
                            }
                        };

                        Nds.Nds7.Cpu.PreExecutionCallback = nds7Executed;
                        Nds.Nds9.Cpu.PreExecutionCallback = nds9Executed;

                        int num = times;
                        while (num > 0 && !Nds.Nds9.Cpu.Errored && !Nds.Nds7.Cpu.Errored)
                        {
                            Nds.Step();

                            LogIndex++;
                            num--;
                        }

                        Nds.Nds7.Cpu.PreExecutionCallback = null;
                        Nds.Nds9.Cpu.PreExecutionCallback = null;
                    }
                }


                ImGui.Checkbox("Run Emulator", ref RunEmulator);

                ImGui.NextColumn();
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 150);

                // ImGui.Text($"BIOS Reads: {Nds.Nds7.Mem.BiosReads}");
                // ImGui.Text($"EWRAM Reads: {Nds.Nds7.Mem.EwramReads}");
                // ImGui.Text($"IWRAM Reads: {Nds.Nds7.Mem.IwramReads}");
                // ImGui.Text($"ROM Reads: {Nds.Nds7.Mem.RomReads}");
                // ImGui.Text($"HWIO Reads: {Nds.Nds7.Mem.HwioReads}");
                // ImGui.Text($"Palette Reads: {Nds.Nds7.Mem.PaletteReads}");
                // ImGui.Text($"VRAM Reads: {Nds.Nds7.Mem.VramReads}");
                // ImGui.Text($"OAM Reads: {Nds.Nds7.Mem.OamReads}");
                ImGui.Text("");
                // ImGui.Text($"EWRAM Writes: {Nds.Nds7.Mem.EwramWrites}");
                // ImGui.Text($"IWRAM Writes: {Nds.Nds7.Mem.IwramWrites}");
                // ImGui.Text($"HWIO Writes: {Nds.Nds7.Mem.HwioWrites}");
                // ImGui.Text($"Palette Writes: {Nds.Nds7.Mem.PaletteWrites}");
                // ImGui.Text($"VRAM Writes: {Nds.Nds7.Mem.VramWrites}");
                // ImGui.Text($"OAM Writes: {Nds.Nds7.Mem.OamWrites}");
                ImGui.Text("");
                // bool ticked = Nds.HwControl.IME;
                // ImGui.Checkbox("IME", ref ticked);

                ImGui.Checkbox("Log HWIO", ref LogHwioAccesses);
                Nds.Nds7.Mem.LogHwioAccesses = LogHwioAccesses;
                Nds.Nds9.Mem.LogHwioAccesses = LogHwioAccesses;
                ImGui.Checkbox("Direct Boot", ref Nds.Provider.DirectBoot);
                // ImGui.Checkbox("Big Screen", ref BigScreen);
                // ImGui.Checkbox("Back Buffer", ref ShowBackBuf);

                // ImGui.NextColumn();

                // ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                // ImGui.Text($"Total Frames: {Nds.Ppu.TotalFrames}");
                // if (RecordTime)
                // {
                //     ImGui.Text($"Timed Frames: {Nds.Ppu.TotalFrames - RecordStartFrames}");
                //     ImGui.Text($"Timed Seconds: {Time}");
                //     ImGui.Text($"Timed FPS: {(uint)(Nds.Ppu.TotalFrames - RecordStartFrames) / Time}");
                // }

                // ImGui.Text($"VCOUNT: {Nds.Ppu.VCount}");
                // ImGui.Text($"Scanline Cycles: {Nds.Ppu.GetScanlineCycles()}");

                ImGuiColumnSeparator();

                ImGui.Text("NDS9---------------");
                ImGui.Text($"0 Src:   {Hex(Nds.Nds9.Dma.Ch[0].DmaSource, 8)}");
                ImGui.Text($"1 Src:   {Hex(Nds.Nds9.Dma.Ch[1].DmaSource, 8)}");
                ImGui.Text($"2 Src:   {Hex(Nds.Nds9.Dma.Ch[2].DmaSource, 8)}");
                ImGui.Text($"3 Src:   {Hex(Nds.Nds9.Dma.Ch[3].DmaSource, 8)}");
                ImGui.Text($"0 Dest:  {Hex(Nds.Nds9.Dma.Ch[0].DmaDest, 8)}");
                ImGui.Text($"1 Dest:  {Hex(Nds.Nds9.Dma.Ch[1].DmaDest, 8)}");
                ImGui.Text($"2 Dest:  {Hex(Nds.Nds9.Dma.Ch[2].DmaDest, 8)}");
                ImGui.Text($"3 Dest:  {Hex(Nds.Nds9.Dma.Ch[3].DmaDest, 8)}");
                ImGui.Text($"0 Words: {Hex(Nds.Nds9.Dma.Ch[0].DMACNT_L, 4)}");
                ImGui.Text($"1 Words: {Hex(Nds.Nds9.Dma.Ch[1].DMACNT_L, 4)}");
                ImGui.Text($"2 Words: {Hex(Nds.Nds9.Dma.Ch[2].DMACNT_L, 4)}");
                ImGui.Text($"3 Words: {Hex(Nds.Nds9.Dma.Ch[3].DMACNT_L, 4)}");
                ImGui.Text($"0 Start: {((DmaStartTimingNds9)(Nds.Nds9.Dma.Ch[0].StartTiming)).ToString()}");
                ImGui.Text($"1 Start: {((DmaStartTimingNds9)(Nds.Nds9.Dma.Ch[1].StartTiming)).ToString()}");
                ImGui.Text($"2 Start: {((DmaStartTimingNds9)(Nds.Nds9.Dma.Ch[2].StartTiming)).ToString()}");
                ImGui.Text($"3 Start: {((DmaStartTimingNds9)(Nds.Nds9.Dma.Ch[3].StartTiming)).ToString()}");

                ImGui.Text("NDS7---------------");
                ImGui.Text($"0 Src:   {Hex(Nds.Nds7.Dma.Ch[0].DmaSource, 8)}");
                ImGui.Text($"1 Src:   {Hex(Nds.Nds7.Dma.Ch[1].DmaSource, 8)}");
                ImGui.Text($"2 Src:   {Hex(Nds.Nds7.Dma.Ch[2].DmaSource, 8)}");
                ImGui.Text($"3 Src:   {Hex(Nds.Nds7.Dma.Ch[3].DmaSource, 8)}");
                ImGui.Text($"0 Dest:  {Hex(Nds.Nds7.Dma.Ch[0].DmaDest, 8)}");
                ImGui.Text($"1 Dest:  {Hex(Nds.Nds7.Dma.Ch[1].DmaDest, 8)}");
                ImGui.Text($"2 Dest:  {Hex(Nds.Nds7.Dma.Ch[2].DmaDest, 8)}");
                ImGui.Text($"3 Dest:  {Hex(Nds.Nds7.Dma.Ch[3].DmaDest, 8)}");
                ImGui.Text($"0 Words: {Hex(Nds.Nds7.Dma.Ch[0].DMACNT_L, 4)}");
                ImGui.Text($"1 Words: {Hex(Nds.Nds7.Dma.Ch[1].DMACNT_L, 4)}");
                ImGui.Text($"2 Words: {Hex(Nds.Nds7.Dma.Ch[2].DMACNT_L, 4)}");
                ImGui.Text($"3 Words: {Hex(Nds.Nds7.Dma.Ch[3].DMACNT_L, 4)}");
                ImGui.Text($"0 Start: {((DmaStartTimingNds7)(Nds.Nds7.Dma.Ch[0].StartTiming)).ToString()}");
                ImGui.Text($"1 Start: {((DmaStartTimingNds7)(Nds.Nds7.Dma.Ch[1].StartTiming)).ToString()}");
                ImGui.Text($"2 Start: {((DmaStartTimingNds7)(Nds.Nds7.Dma.Ch[2].StartTiming)).ToString()}");
                ImGui.Text($"3 Start: {((DmaStartTimingNds7)(Nds.Nds7.Dma.Ch[3].StartTiming)).ToString()}");

                ImGuiColumnSeparator();

                ImGui.NextColumn();

                ImGui.Text("A---------------");
                var rendA = Nds.Ppu.Renderers[0];
                ImGui.Text($"BG0 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[0].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[0].ScreenSize]}");
                ImGui.Text($"BG0 Scroll X: {rendA.Backgrounds[0].HorizontalOffset}");
                ImGui.Text($"BG0 Scroll Y: {rendA.Backgrounds[0].VerticalOffset}");
                ImGui.Text($"BG1 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[1].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[1].ScreenSize]}");
                ImGui.Text($"BG1 Scroll X: {rendA.Backgrounds[1].HorizontalOffset}");
                ImGui.Text($"BG1 Scroll Y: {rendA.Backgrounds[1].VerticalOffset}");
                ImGui.Text($"BG2 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[2].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Affine Size: {PpuRenderer.AffineSizeTable[rendA.Backgrounds[2].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendA.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Scroll X: {rendA.Backgrounds[2].HorizontalOffset}");
                ImGui.Text($"BG2 Scroll Y: {rendA.Backgrounds[2].VerticalOffset}");
                ImGui.Text($"BG3 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[3].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Affine Size: {PpuRenderer.AffineSizeTable[rendA.Backgrounds[3].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendA.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X: {rendA.Backgrounds[3].HorizontalOffset}");
                ImGui.Text($"BG3 Scroll Y: {rendA.Backgrounds[3].VerticalOffset}");
                ImGui.Text("Debug BG0123/OBJ");
                ImGui.Checkbox("##rendAbg0", ref rendA.DebugEnableBg[0]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg1", ref rendA.DebugEnableBg[1]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg2", ref rendA.DebugEnableBg[2]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg3", ref rendA.DebugEnableBg[3]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAobj", ref rendA.DebugEnableObj);

                ImGui.Text("B---------------");
                var rendB = Nds.Ppu.Renderers[1];
                ImGui.Text($"BG0 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[0].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[0].ScreenSize]}");
                ImGui.Text($"BG0 Scroll X: {rendB.Backgrounds[0].HorizontalOffset}");
                ImGui.Text($"BG0 Scroll Y: {rendB.Backgrounds[0].VerticalOffset}");
                ImGui.Text($"BG1 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[1].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[1].ScreenSize]}");
                ImGui.Text($"BG1 Scroll X: {rendB.Backgrounds[1].HorizontalOffset}");
                ImGui.Text($"BG1 Scroll Y: {rendB.Backgrounds[1].VerticalOffset}");
                ImGui.Text($"BG2 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[2].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Affine Size: {PpuRenderer.AffineSizeTable[rendB.Backgrounds[2].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendB.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Scroll X: {rendB.Backgrounds[2].HorizontalOffset}");
                ImGui.Text($"BG2 Scroll Y: {rendB.Backgrounds[2].VerticalOffset}");
                ImGui.Text($"BG3 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[3].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Affine Size: {PpuRenderer.AffineSizeTable[rendB.Backgrounds[3].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendB.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X: {rendB.Backgrounds[3].HorizontalOffset}");
                ImGui.Text($"BG3 Scroll Y: {rendB.Backgrounds[3].VerticalOffset}");
                ImGui.Text("Debug BG0123/OBJ");
                ImGui.Checkbox("##rendBbg0", ref rendB.DebugEnableBg[0]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg1", ref rendB.DebugEnableBg[1]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg2", ref rendB.DebugEnableBg[2]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg3", ref rendB.DebugEnableBg[3]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBobj", ref rendB.DebugEnableObj);

                // ImGui.Text($"Window 0 Left..: {Nds.Ppu.Win0HLeft}");
                // ImGui.Text($"Window 0 Right.: {Nds.Ppu.Win0HRight}");
                // ImGui.Text($"Window 0 Top...: {Nds.Ppu.Win0VTop}");
                // ImGui.Text($"Window 0 Bottom: {Nds.Ppu.Win0VBottom}");
                // ImGui.Text($"Window 1 Left..: {Nds.Ppu.Win1HLeft}");
                // ImGui.Text($"Window 1 Right.: {Nds.Ppu.Win1HRight}");
                // ImGui.Text($"Window 1 Top...: {Nds.Ppu.Win1VTop}");
                // ImGui.Text($"Window 1 Bottom: {Nds.Ppu.Win1VBottom}");

                ImGui.NextColumn();

                String[] prescalerCodes = { "F/1", "F/64", "F/256", "F/1024" };

                ImGui.Text("NDS9---------------");
                ImGui.Text($"Timer 0 Counter: {Hex(Nds.Nds9.Timers.T[0].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Nds.Nds9.Timers.T[1].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Nds.Nds9.Timers.T[2].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Nds.Nds9.Timers.T[3].CalculateCounter(), 4)}");
                ImGui.Text("");
                ImGui.Text($"Timer 0 Reload: {Hex(Nds.Nds9.Timers.T[0].ReloadVal, 4)}");
                ImGui.Text($"Timer 1 Reload: {Hex(Nds.Nds9.Timers.T[1].ReloadVal, 4)}");
                ImGui.Text($"Timer 2 Reload: {Hex(Nds.Nds9.Timers.T[2].ReloadVal, 4)}");
                ImGui.Text($"Timer 3 Reload: {Hex(Nds.Nds9.Timers.T[3].ReloadVal, 4)}");
                ImGui.Text("");
                ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Nds.Nds9.Timers.T[0].PrescalerSel]}");
                ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Nds.Nds9.Timers.T[1].PrescalerSel]}");
                ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Nds.Nds9.Timers.T[2].PrescalerSel]}");
                ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Nds.Nds9.Timers.T[3].PrescalerSel]}");
                ImGui.Text("NDS7---------------");
                ImGui.Text($"Timer 0 Counter: {Hex(Nds.Nds7.Timers.T[0].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Nds.Nds7.Timers.T[1].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Nds.Nds7.Timers.T[2].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Nds.Nds7.Timers.T[3].CalculateCounter(), 4)}");
                ImGui.Text("");
                ImGui.Text($"Timer 0 Reload: {Hex(Nds.Nds7.Timers.T[0].ReloadVal, 4)}");
                ImGui.Text($"Timer 1 Reload: {Hex(Nds.Nds7.Timers.T[1].ReloadVal, 4)}");
                ImGui.Text($"Timer 2 Reload: {Hex(Nds.Nds7.Timers.T[2].ReloadVal, 4)}");
                ImGui.Text($"Timer 3 Reload: {Hex(Nds.Nds7.Timers.T[3].ReloadVal, 4)}");
                ImGui.Text("");
                ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Nds.Nds7.Timers.T[0].PrescalerSel]}");
                ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Nds.Nds7.Timers.T[1].PrescalerSel]}");
                ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Nds.Nds7.Timers.T[2].PrescalerSel]}");
                ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Nds.Nds7.Timers.T[3].PrescalerSel]}");


                ImGuiColumnSeparator();

                ImGui.Text("VRAMCNT_A: " + Hex(Nds.MemoryControl.VRAMCNT[0], 2));
                ImGui.Text("VRAMCNT_B: " + Hex(Nds.MemoryControl.VRAMCNT[1], 2));
                ImGui.Text("VRAMCNT_C: " + Hex(Nds.MemoryControl.VRAMCNT[2], 2));
                ImGui.Text("VRAMCNT_D: " + Hex(Nds.MemoryControl.VRAMCNT[3], 2));
                ImGui.Text("VRAMCNT_E: " + Hex(Nds.MemoryControl.VRAMCNT[4], 2));
                ImGui.Text("VRAMCNT_F: " + Hex(Nds.MemoryControl.VRAMCNT[5], 2));
                ImGui.Text("VRAMCNT_G: " + Hex(Nds.MemoryControl.VRAMCNT[6], 2));
                ImGui.Text("VRAMCNT_H: " + Hex(Nds.MemoryControl.VRAMCNT[7], 2));
                ImGui.Text("VRAMCNT_I: " + Hex(Nds.MemoryControl.VRAMCNT[8], 2));
                ImGui.Checkbox("Disable VRAM Updates", ref Nds.Ppu.DebugDisableVramUpdates);

                ImGui.Text("Firmware State: " + Nds.Nds7.Spi.FlashState.ToString());
                ImGui.Text("Firmware Addr: " + Hex(Nds.Nds7.Spi.Address, 6));
                ImGui.Text("Slot 1 Access: " + (Nds.MemoryControl.Slot1AccessRights ? "ARM7" : "ARM9"));
                ImGui.Text("Slot 1 State: " + Nds.Cartridge.State.ToString());
                ImGui.Text("Slot 1 Addr: " + Hex(Nds.Cartridge.DataPos, 8));
                ImGui.Text("Slot 1 Tx. So Far: " + Nds.Cartridge.BytesTransferred + " bytes");
                ImGui.Text("Slot 1 Tx. Length: " + Nds.Cartridge.TransferLength);

                ImGui.Columns(1);
                ImGui.Separator();

                ImGui.Text("Palettes");

                int texIdIndex = 0;
                for (uint i = 0; i < 2; i++)
                {
                    for (uint j = 0; j < 2; j++)
                    {
                        uint paletteBase = j * 256;
                        for (uint p = 0; p < 256; p++)
                        {
                            PaletteImageBuffer[p] = PpuRenderer.Rgb555To888(Nds.Ppu.Renderers[i].LookupPalette(paletteBase + p), false);
                        }

                        GL.BindTexture(TextureTarget.Texture2D, palTexIds[texIdIndex]);

                        // TexParameter needed for something to display :)
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);

                        GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                        GL.TexImage2D(
                            TextureTarget.Texture2D,
                            0,
                            PixelInternalFormat.Rgba,
                            16,
                            16,
                            0,
                            PixelFormat.Rgba,
                            PixelType.UnsignedByte,
                            PaletteImageBuffer
                        );

                        // ImGui.Text($"Pointer: {texId}");
                        ImGui.Image((IntPtr)palTexIds[texIdIndex], new Vector2(16 * 8, 16 * 8)); ImGui.SameLine();
                        texIdIndex++;
                    }
                }

                ImGui.End();
            }
        }

        public void ImGuiColumnSeparator()
        {
            ImGui.Dummy(new Vector2(0.0f, 0.5f));

            // Draw separator within column
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            Vector2 pos = ImGui.GetCursorScreenPos();
            drawList.AddLine(new Vector2(pos.X - 9999, pos.Y), new Vector2(pos.X + 9999, pos.Y), ImGui.GetColorU32(ImGuiCol.Border));

            ImGui.Dummy(new Vector2(0.0f, 1f));
        }

        public void DrawInstrViewer()
        {
            if (ImGui.Begin("Instruction Viewer"))
            {
                ImGui.Columns(2);
                ImGui.Text("ARM9\n");
                drawDisassembly(Nds.Nds9);
                ImGui.NextColumn();
                ImGui.Text("ARM7\n");
                drawDisassembly(Nds.Nds7);
                ImGui.Columns(1);

                ImGui.End();
            }
        }

        public uint[] DisplayBuffer = new uint[256 * 192];

        public bool BigScreen = false;
        public bool ShowBackBuf = false;
        public unsafe void DrawDisplay()
        {
            if (ImGui.Begin("Display", ImGuiWindowFlags.NoResize))
            {
                float width = BigScreen ? 256 * 4 : 256 * 2;
                float height = BigScreen ? 192 * 4 : 192 * 2;

                for (int i = 0; i < 2; i++)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, screenTexIds[i]);

                    // TexParameter needed for something to display :)
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
                    var renderer = Nds.Ppu.Renderers[i];

                    var buf = ShowBackBuf ? renderer.ScreenBack : renderer.ScreenFront;
                    for (uint j = 0; j < 256 * 192; j++)
                    {
                        DisplayBuffer[j] = PpuRenderer.ColorLut[buf[j] & 0x7FFF];
                    }

                    GL.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        PixelInternalFormat.Rgba,
                        256,
                        192,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        DisplayBuffer
                    );

                    ImGui.Image((IntPtr)screenTexIds[i], new Vector2(width, height));
                }
                ImGui.SetWindowSize(new Vector2(width + 16, height * 2 + 48));

                ImGui.End();
            }
        }

        public List<Register> Registers7 = new List<Register>();
        public List<Register> Registers9 = new List<Register>();
        public class Register
        {
            public RegisterField[] Fields;
            public uint Address;
            public String Name;
            public Register(String name, uint address, params RegisterField[] fields)
            {
                Fields = fields;
                Address = address;
                Name = name;
            }
        }
        public class RegisterField
        {
            public byte Bit;
            public byte EndBit; // Non-checkbox only
            public String Name;
            public bool Checkbox;

            public RegisterField(String name, byte bit)
            {
                Name = name;
                Bit = bit;
                EndBit = 0;
                Checkbox = true;
            }

            public RegisterField(String name, byte bit, byte endBit)
            {
                Name = name;
                Bit = bit;
                EndBit = endBit;
                Checkbox = false;
            }
        }

        public void SetupRegViewer()
        {
            Registers9.Add(
                new Register("DISPCNT - PPU Control", 0x4000000,
                    new RegisterField("BG Mode", 0, 2),
                    new RegisterField("BG0 is 3D", 3),
                    new RegisterField("OBJ Character VRAM Mapping", 4),
                    new RegisterField("OBJ Character Dimension", 5),
                    new RegisterField("OBJ Bitmap VRAM Mapping", 6),
                    new RegisterField("Forced Blank", 7),

                    new RegisterField("Screen Display BG0", 8),
                    new RegisterField("Screen Display BG1", 9),
                    new RegisterField("Screen Display BG2", 10),
                    new RegisterField("Screen Display BG3", 11),
                    new RegisterField("Screen Display OBJ", 12),
                    new RegisterField("Window 0 Display Flag", 13),
                    new RegisterField("Window 1 Display Flag", 14),
                    new RegisterField("OBJ Window Display Flag", 15),

                    new RegisterField("Display Mode", 16, 17),
                    new RegisterField("LCDC VRAM Block", 18, 19),
                    new RegisterField("Tile OBJ 1D Boundary", 20, 21),
                    new RegisterField("Bitmap OBJ 1D Boundary", 22),
                    new RegisterField("Disable H-Blank Rendering", 23),

                    new RegisterField("Coarse Character Block Base", 24, 26),
                    new RegisterField("Coarse Map Block Base", 27, 29),
                    new RegisterField("Enable BG Extended Palettes", 30),
                    new RegisterField("Enable OBJ Extended Palettes", 31)
                ));

            Registers9.Add(
                new Register("DISPCNTB - PPU Control B", 0x4001000,
                    new RegisterField("BG Mode", 0, 2),
                    new RegisterField("OBJ Character VRAM Mapping", 4),
                    new RegisterField("OBJ Character Dimension", 5),
                    new RegisterField("OBJ Bitmap VRAM Mapping", 6),
                    new RegisterField("Forced Blank", 7),

                    new RegisterField("Screen Display BG0", 8),
                    new RegisterField("Screen Display BG1", 9),
                    new RegisterField("Screen Display BG2", 10),
                    new RegisterField("Screen Display BG3", 11),
                    new RegisterField("Screen Display OBJ", 12),
                    new RegisterField("Window 0 Display Flag", 13),
                    new RegisterField("Window 1 Display Flag", 14),
                    new RegisterField("OBJ Window Display Flag", 15),

                    new RegisterField("Display Mode", 16, 17),
                    new RegisterField("Tile OBJ 1D Boundary", 20, 21),
                    new RegisterField("Disable H-Blank Rendering", 23),

                    new RegisterField("Enable BG Extended Palettes", 30),
                    new RegisterField("Enable OBJ Extended Palettes", 31)
                ));


            Registers9.Add(
                new Register("DISPSTAT - General PPU Status", 0x4000004,
                    new RegisterField("V-Blank flag", 0),
                    new RegisterField("H-Blank flag", 1),
                    new RegisterField("V-Counter flag", 2),
                    new RegisterField("V-Blank IRQ Enable", 3),
                    new RegisterField("H-Blank IRQ Enable", 4),
                    new RegisterField("V-Counter IRQ Enable", 5),
                    new RegisterField("V-Count Setting", 8, 15)
            ));

            uint[] bgCntAddrs = { 0x4000008, 0x400000A, 0x400000C, 0x400000E };
            for (uint r = 0; r < 4; r++)
            {
                Registers9.Add(
                    new Register($"BG{r}CNT - BG{r} Control", bgCntAddrs[r],
                        new RegisterField("BG Priority", 0, 1),
                        new RegisterField("Character Base Block", 2, 3),
                        new RegisterField("Mosaic", 6),
                        new RegisterField("8-bit Color", 7),
                        new RegisterField("Map Base Block", 8, 12),
                        new RegisterField("Overflow Wraparound", 13),
                        new RegisterField("Screen Size", 14, 15)

                ));
            }

            Registers9.Add(
                new Register("WININ - Window Interior Control", 0x4000048,
                    new RegisterField("Window 0 BG0", 0),
                    new RegisterField("Window 0 BG1", 1),
                    new RegisterField("Window 0 BG2", 2),
                    new RegisterField("Window 0 BG3", 3),
                    new RegisterField("Window 0 OBJ", 4),
                    new RegisterField("Window 0 Color Math", 5),

                    new RegisterField("Window 1 BG0", 8),
                    new RegisterField("Window 1 BG1", 9),
                    new RegisterField("Window 1 BG2", 10),
                    new RegisterField("Window 1 BG3", 11),
                    new RegisterField("Window 1 OBJ", 12),
                    new RegisterField("Window 1 Color Math", 13)
            ));

            Registers9.Add(
                new Register("WINOUT - Window Exterior Control", 0x400004A,
                    new RegisterField("Window 0 BG0", 0),
                    new RegisterField("Window 0 BG1", 1),
                    new RegisterField("Window 0 BG2", 2),
                    new RegisterField("Window 0 BG3", 3),
                    new RegisterField("Window 0 OBJ", 4),
                    new RegisterField("Window 0 Color Math", 5),

                    new RegisterField("OBJ Window BG0", 8),
                    new RegisterField("OBJ Window BG1", 9),
                    new RegisterField("OBJ Window BG2", 10),
                    new RegisterField("OBJ Window BG3", 11),
                    new RegisterField("OBJ Window OBJ", 12),
                    new RegisterField("OBJ Window Color Math", 13)
            ));

            Registers9.Add(
                new Register($"BLDCNT - Blending Control", 0x4000050,
                    new RegisterField("BG0 1st Target Pixel", 0),
                    new RegisterField("BG1 1st Target Pixel", 1),
                    new RegisterField("BG2 1st Target Pixel", 2),
                    new RegisterField("BG3 1st Target Pixel", 3),
                    new RegisterField("OBJ 1st Target Pixel", 4),
                    new RegisterField("BD  1st Target Pixel", 5),
                    new RegisterField("Blending Effect", 6, 7),
                    new RegisterField("BG0 2nd Target Pixel", 8),
                    new RegisterField("BG1 2nd Target Pixel", 9),
                    new RegisterField("BG2 2nd Target Pixel", 10),
                    new RegisterField("BG3 2nd Target Pixel", 11),
                    new RegisterField("OBJ 2nd Target Pixel", 12),
                    new RegisterField("BD  2nd Target Pixel", 13)
                ));

            Registers9.Add(
                new Register($"BLDALPHA - Blending Coefficients", 0x4000052,
                    new RegisterField("EVA Coefficient", 0, 4),
                    new RegisterField("EVB Coefficient", 8, 12)
                ));

            Registers9.Add(
                new Register($"BLDY - Blending Brightness", 0x4000054,
                    new RegisterField("EVY Coefficient", 0, 4)
                ));

            Registers9.Add(
                new Register($"SOUNDCNT_H - DMA Sound Control", 0x4000082,
                    new RegisterField("Sound # 1-4 Volume", 0, 1),
                    new RegisterField("DMA Sound A Volume", 2, 2),
                    new RegisterField("DMA Sound B Volume", 3, 3),
                    new RegisterField("DMA Sound A Enable RIGHT", 8),
                    new RegisterField("DMA Sound A Enable LEFT", 9),
                    new RegisterField("DMA Sound A Timer Select", 10, 10),
                    new RegisterField("DMA Sound B Enable RIGHT", 12),
                    new RegisterField("DMA Sound B Enable LEFT", 13),
                    new RegisterField("DMA Sound B Timer Select", 14, 14)
            ));

            uint[] dmaAddrs = { 0x40000BA, 0x40000C6, 0x40000D2, 0x40000DE };
            for (uint r = 0; r < 4; r++)
            {
                var reg = new Register($"DMA{r}CNT_H - DMA {r} Control", dmaAddrs[r],
                        new RegisterField("Dest Addr Control", 5, 6),
                        new RegisterField("Source Addr Control", 7, 8),
                        new RegisterField("DMA Repeat", 9),
                        new RegisterField("DMA 32-bit Mode", 10),
                        new RegisterField("Game Pak DRQ", 11),
                        new RegisterField("DMA Start Timing", 12, 13),
                        new RegisterField("IRQ on Word Count Drain", 14),
                        new RegisterField("DMA Enable", 15));
                Registers9.Add(reg);
                Registers7.Add(reg);
            }

            uint[] timerAddrs = { 0x4000102, 0x4000106, 0x400010A, 0x400010E };
            for (uint r = 0; r < 4; r++)
            {
                Registers9.Add(
                    new Register($"TM{r}CNT_L - Timer {r} Control", timerAddrs[r],
                        new RegisterField("Prescaler Selection", 0, 1),
                        new RegisterField("Timer Cascade", 2),
                        new RegisterField("Timer IRQ Enable", 6),
                        new RegisterField("Timer Start / Stop", 7)
                ));
            }

            Registers9.Add(
                new Register("KEYINPUT - Key Status", 0x4000130,
                    new RegisterField("Button A", 0),
                    new RegisterField("Button B", 1),
                    new RegisterField("Select", 2),
                    new RegisterField("Start", 3),
                    new RegisterField("Right", 4),
                    new RegisterField("Left", 5),
                    new RegisterField("Up", 6),
                    new RegisterField("Down", 7),
                    new RegisterField("Button R", 8),
                    new RegisterField("Button L", 9)
            ));

            Registers7.Add(
                new Register("SPICNT - SPI Bus Control/Status", 0x40001C0,
                    new RegisterField("Baudrate", 0, 1),
                    new RegisterField("Busy", 7),
                    new RegisterField("Device", 8, 9),
                    new RegisterField("Chip Select Hold", 11),
                    new RegisterField("Enable IRQ", 14),
                    new RegisterField("Enable", 15)
            ));

            var ipcSync = new Register("IPCSYNC", 0x4000180,
                new RegisterField("Data In", 0, 3),
                new RegisterField("Data Out", 8, 11),
                new RegisterField("Send Remote IRQ", 13),
                new RegisterField("Enable Remote IRQ", 14)
            );
            var ipcFifoCnt = new Register("IPCFIFOCNT", 0x4000184,
                new RegisterField("Send FIFO is Empty", 0),
                new RegisterField("Send FIFO is Full", 1),
                new RegisterField("Enable Send FIFO Pending IRQ", 2),
                new RegisterField("Receive FIFO is Empty", 8),
                new RegisterField("Receive FIFO is Full", 9),
                new RegisterField("Enable Receive FIFO Pending IRQ", 10),
                new RegisterField("Read Empty/Send Full Error", 14),
                new RegisterField("Enable FIFOs", 15)
            );

            Registers7.Add(ipcSync);
            Registers9.Add(ipcSync);
            Registers7.Add(ipcFifoCnt);
            Registers9.Add(ipcFifoCnt);

            RegViewerSelected9 = Registers9[0];
            RegViewerSelected7 = Registers7[0];
        }

        Register RegViewerSelected7;
        Register RegViewerSelected9;

        public void DrawRegViewer()
        {
            if (ImGui.Begin("Register Viewer"))
            {
                ImGui.Columns(2);

                if (ImGui.BeginCombo("##regviewer9-combo", $"{Hex(RegViewerSelected9.Address, 8)} {RegViewerSelected9.Name}"))
                {
                    foreach (Register r in Registers9)
                    {
                        bool selected = r == RegViewerSelected9;
                        if (ImGui.Selectable($"{Hex(r.Address, 8)} {r.Name}", selected))
                        {
                            RegViewerSelected9 = r;
                        }
                        if (selected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }
                uint value = Nds.Nds9.Mem.ReadDebug32(RegViewerSelected9.Address);
                foreach (RegisterField f in RegViewerSelected9.Fields)
                {
                    if (f.Checkbox)
                    {
                        bool ticked = Bits.BitTest(value, f.Bit);
                        // ImGui.Text($"{f.Bit}");
                        // ImGui.SameLine(); 
                        ImGui.Checkbox(f.Name, ref ticked);
                    }
                    else
                    {
                        ImGui.Text($" {Bits.BitRange(value, f.Bit, f.EndBit)}");
                        ImGui.SameLine(); ImGui.Text(f.Name);
                    }
                }

                ImGui.NextColumn();

                if (ImGui.BeginCombo("##regviewer7-combo", $"{Hex(RegViewerSelected7.Address, 8)} {RegViewerSelected7.Name}"))
                {
                    foreach (Register r in Registers7)
                    {
                        bool selected = r == RegViewerSelected7;
                        if (ImGui.Selectable($"{Hex(r.Address, 8)} {r.Name}", selected))
                        {
                            RegViewerSelected7 = r;
                        }
                        if (selected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }
                value = Nds.Nds7.Mem.ReadDebug32(RegViewerSelected7.Address);
                foreach (RegisterField f in RegViewerSelected7.Fields)
                {
                    if (f.Checkbox)
                    {
                        bool ticked = Bits.BitTest(value, f.Bit);
                        // ImGui.Text($"{f.Bit}");
                        // ImGui.SameLine(); 
                        ImGui.Checkbox(f.Name, ref ticked);
                    }
                    else
                    {
                        ImGui.Text($" {Bits.BitRange(value, f.Bit, f.EndBit)}");
                        ImGui.SameLine(); ImGui.Text(f.Name);
                    }
                }

                ImGui.End();
            }
        }

        public Dictionary<ThumbExecutor, uint> CpuProfilerDictThumb = new Dictionary<ThumbExecutor, uint>();
        public Dictionary<ArmExecutor, uint> CpuProfilerDictArm = new Dictionary<ArmExecutor, uint>();
        public void DrawCpuProfiler()
        {
            if (ImGui.Begin("CPU Profiler"))
            {
                foreach (var key in new List<ThumbExecutor>(CpuProfilerDictThumb.Keys))
                {
                    CpuProfilerDictThumb[key] = 0;
                }
                foreach (var key in new List<ArmExecutor>(CpuProfilerDictArm.Keys))
                {
                    CpuProfilerDictArm[key] = 0;
                }

                for (int ti = 0; ti < 1024; ti++)
                {
                    ThumbExecutor k = Nds.Nds9.Cpu.ThumbDispatch[ti];
                    if (!CpuProfilerDictThumb.TryGetValue(k, out uint val))
                    {
                        CpuProfilerDictThumb[k] = 0;
                    }
                    CpuProfilerDictThumb[k] += Nds.Nds7.Cpu.ThumbExecutorProfile[ti];
                }

                for (int ai = 0; ai < 4096; ai++)
                {
                    ArmExecutor k = Nds.Nds9.Cpu.ArmDispatch[ai];
                    if (!CpuProfilerDictArm.TryGetValue(k, out uint val))
                    {
                        CpuProfilerDictArm[k] = 0;
                    }
                    CpuProfilerDictArm[k] += Nds.Nds7.Cpu.ArmExecutorProfile[ai];
                }

                ImGui.Columns(1);
                ImGui.Text("THUMB Mode");
                ImGui.Columns(2);

                foreach (var (k, v) in CpuProfilerDictThumb.OrderByDescending(p => p.Value))
                {
                    ImGui.Text(k.Method.Name);
                    ImGui.NextColumn();
                    ImGui.Text(v.ToString());
                    ImGui.NextColumn();
                }
                ImGui.Separator();

                ImGui.Columns(1);
                ImGui.Text("ARM Mode");
                ImGui.Columns(2);

                foreach (var (k, v) in CpuProfilerDictArm.OrderByDescending(p => p.Value))
                {
                    ImGui.Text(k.Method.Name);
                    ImGui.NextColumn();
                    ImGui.Text(v.ToString());
                    ImGui.NextColumn();
                }

                ImGui.End();
            }
        }

        public void DrawHwioLog()
        {
            if (ImGui.Begin("HWIO Log"))
            {
                if (ImGui.Button("Dump"))
                {
                    using (StreamWriter file9 = new StreamWriter("hwio9.txt"))
                    {
                        file9.WriteLine("ARM9");
                        lock (Nds.Nds9.Mem.HwioReadLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioReadLog)
                            {
                                file9.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                            }
                        }
                        file9.WriteLine("---------");
                        lock (Nds.Nds9.Mem.HwioWriteLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioWriteLog)
                            {
                                file9.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                            }
                        }
                    }

                    using (StreamWriter file7 = new StreamWriter("hwio7.txt"))
                    {
                        file7.WriteLine("ARM7");
                        lock (Nds.Nds7.Mem.HwioReadLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioReadLog)
                            {
                                file7.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                            }
                        }
                        file7.WriteLine("---------");
                        lock (Nds.Nds7.Mem.HwioWriteLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioWriteLog)
                            {
                                file7.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                            }
                        }
                    }
                }

                ImGui.Columns(2);

                ImGui.Text("ARM9");
                lock (Nds.Nds9.Mem.HwioReadLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioReadLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                    }
                }
                ImGui.Text("---------");
                lock (Nds.Nds9.Mem.HwioWriteLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioWriteLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                    }
                }

                ImGui.NextColumn();

                ImGui.Text("ARM7");
                lock (Nds.Nds7.Mem.HwioReadLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioReadLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                    }
                }
                ImGui.Text("---------");
                lock (Nds.Nds7.Mem.HwioWriteLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioWriteLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                    }
                }

                ImGui.End();
            }
        }

        public void DumpSav()
        {
            // try
            // {
            //     System.IO.File.WriteAllBytesAsync(Gba.Provider.SavPath, Gba.Mem.SaveProvider.GetSave());
            // }
            // catch
            // {
            //     Console.WriteLine("Failed to write .sav file!");
            // }
        }

        public void DrawSchedulerInfo()
        {
            if (ImGui.Begin("Scheduler"))
            {
                // if (ImGui.Button("Pop First Event")) Nds.Scheduler.PopFirstEvent();
                // if (ImGui.Button("Add 0")) Nds.Scheduler.AddEventRelative(SchedulerId.None, 0, (long cyclesLate) => { });
                // if (ImGui.Button("Add 100")) Nds.Scheduler.AddEventRelative(SchedulerId.None, 100, (long cyclesLate) => { });
                // if (ImGui.Button("Add 500")) Nds.Scheduler.AddEventRelative(SchedulerId.None, 500, (long cyclesLate) => { });
                // if (ImGui.Button("Add 42069")) Nds.Scheduler.AddEventRelative(SchedulerId.None, 42069, (long cyclesLate) => { });

                ImGui.Text($"Current Ticks: {Nds.Scheduler.CurrentTicks}");
                ImGui.Text($"Next event at: {Nds.Scheduler.NextEventTicks}");
                ImGui.Text($"Events queued: {Nds.Scheduler.EventsQueued}");

                ImGui.Separator();

                ImGui.Columns(3);

                ImGui.Text("Index");
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 50);
                ImGui.NextColumn();
                ImGui.Text("Ticks");
                ImGui.NextColumn();
                ImGui.Text("ID");
                ImGui.NextColumn();

                ImGui.Separator();

                SchedulerEvent evt = Nds.Scheduler.RootEvent.NextEvent;
                int index = 0;
                while (evt != null)
                {
                    ImGui.Text(index.ToString());
                    ImGui.NextColumn();
                    ImGui.Text((evt.Ticks - Nds.Scheduler.CurrentTicks).ToString());
                    ImGui.NextColumn();
                    ImGui.Text(Scheduler.ResolveId(evt.Id));
                    ImGui.NextColumn();

                    evt = evt.NextEvent;
                    index++;
                }

                ImGui.Columns(1);

                ImGui.End();
            }
        }

        public static readonly string[] IeIfBitNames = {
            "PPU V-Blank",
            "PPU H-Blank",
            "PPU V-Counter Match",
            "Timer 0 Overflow",
            "Timer 1 Overflow",
            "Timer 2 Overflow",
            "Timer 3 Overflow",
            "SIO/RCNT/RTC",
            "DMA 0",
            "DMA 1",
            "DMA 2",
            "DMA 3",
            "Keypad",
            "Slot 2",
            "Unused",
            "Unused",
            "IPC Sync",
            "IPC Empty Send FIFO",
            "IPC Pending FIFO",
            "Slot 1 Data Transfer Complete",
            "Slot 1 IREQ_MC",
            "Geometry Command FIFO",
            "Screens Unfolding",
            "SPI Bus",
            "Wi-Fi"
        };

        public void DrawInterruptStatus()
        {
            if (ImGui.Begin("Interrupt Status"))
            {
                ImGui.Columns(2);

                ImGui.Text("ARM9");
                ImGui.SetColumnWidth(0, 61);
                displayCheckbox("IME##arm9", Nds.Nds9.HwControl.IME);
                drawInterruptColumn(Nds.Nds9.HwControl.IE, Nds.Nds9.HwControl.IF, false);
                ImGui.NextColumn();
                ImGui.Text("ARM7");
                displayCheckbox("IME##arm7", Nds.Nds7.HwControl.IME);
                drawInterruptColumn(Nds.Nds7.HwControl.IE, Nds.Nds7.HwControl.IF, true);

                ImGui.End();
            }
        }

        public void drawInterruptColumn(uint IE, uint IF, bool text)
        {
            ImGui.Text("IE  IF");
            for (uint i = 0; i < IeIfBitNames.Length; i++)
            {
                displayCheckbox("", BitTest(IE, (byte)i)); ImGui.SameLine();
                displayCheckbox("", BitTest(IF, (byte)i));
                if (text)
                {
                    ImGui.SameLine();
                    ImGui.Text(IeIfBitNames[i]);
                }
            }
        }

        public void DrawSoundVisualizer()
        {
            if (ImGui.Begin("Sound Visualizer"))
            {
                for (uint i = 0; i < 16; i++)
                {
                    var c = Nds.Nds7.Audio.Channels[i];

                    var drawList = ImGui.GetWindowDrawList();

                    var size = new Vector2(ImGui.GetWindowContentRegionWidth(), 40);

                    Vector2 pos = ImGui.GetCursorScreenPos();

                    uint totalSamples = 0;
                    uint loopSamples = 0;
                    switch (c.Format)
                    {
                        case 0: // PCM8
                            totalSamples = (c.SOUNDPNT + c.SOUNDLEN) * 4;
                            loopSamples = c.SOUNDPNT * 4;
                            break;
                        case 1: // PCM16
                            totalSamples = (c.SOUNDPNT + c.SOUNDLEN) * 2;
                            loopSamples = c.SOUNDPNT * 2;
                            break;
                        case 2: // IMA-ADPCM
                            totalSamples = (c.SOUNDPNT + c.SOUNDLEN) * 8;
                            loopSamples = c.SOUNDPNT * 8;
                            break;
                    }
                    float fillPortion = (float)c.SamplePos / (float)totalSamples;
                    float loopPortion = (float)loopSamples / (float)totalSamples;

                    // ImGui.Text(Pad(i.ToString(), 2, '0')); ImGui.SameLine();
                    // displayCheckbox("Playing##" + i, c.Playing);
                    var fillSize = new Vector2(size.X * fillPortion, size.Y);
                    var bgColor = ImGui.GetColorU32(c.Playing ? ImGuiCol.Button : ImGuiCol.Border);
                    drawList.AddRectFilled(pos, pos + size, bgColor); // fill BG

                    if (c.Playing)
                    {
                        float volumePortion = (float)(c.Volume >> (1 << c.VolumeDiv)) / 127f;
                        uint fillColor = (ImGui.GetColorU32(ImGuiCol.ButtonHovered) & 0x00FFFFFF) | ((uint)(255f * volumePortion) << 24);
                        drawList.AddRectFilled(pos, pos + fillSize, fillColor); // fill
                        drawList.AddRectFilled(new Vector2(pos.X + fillSize.X - 2, pos.Y), new Vector2(pos.X + fillSize.X, pos.Y + size.Y - 1), ImGui.GetColorU32(ImGuiCol.ButtonActive));
                    }
                    if (c.RepeatMode == 1)
                    {
                        var loopOffs = size.X * loopPortion;
                        drawList.AddRectFilled(new Vector2(pos.X + loopOffs - 2, pos.Y), new Vector2(pos.X + loopOffs, pos.Y + size.Y - 1), 0xFF00FF00);
                    }
                    else if (c.RepeatMode == 2)
                    {
                        drawList.AddRectFilled(new Vector2(pos.X + size.X - 2, pos.Y), new Vector2(pos.X + size.X, pos.Y + size.Y - 1), 0xFF0000FF);
                    }
                    drawList.AddRect(pos, pos + size, ImGui.GetColorU32(ImGuiCol.Border)); // border

                    ImGui.Checkbox("##soundvis-check" + i, ref c.DebugEnable); ImGui.SameLine();
                    if (ImGui.Button("Solo##soundvis" + i))
                    {
                        int numEnabled = 0;
                        for (uint chI = 0; chI < 16; chI++)
                        {
                            if (Nds.Nds7.Audio.Channels[chI].DebugEnable)
                            {
                                numEnabled++;
                            }
                        }

                        bool thisChannelEnabled = c.DebugEnable;
                        for (uint chI = 0; chI < 16; chI++)
                        {
                            if (thisChannelEnabled)
                            {
                                if (numEnabled == 1)
                                {
                                    Nds.Nds7.Audio.Channels[chI].DebugEnable = true;
                                }
                                else if (chI != i)
                                {
                                    Nds.Nds7.Audio.Channels[chI].DebugEnable = false;
                                }
                            }
                            else
                            {
                                if (chI != i)
                                {
                                    Nds.Nds7.Audio.Channels[chI].DebugEnable = false;
                                }
                                else
                                {
                                    Nds.Nds7.Audio.Channels[chI].DebugEnable = true;
                                }
                            }
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Replay##soundvis" + i))
                    {
                        c.SOUNDLEN = 4000;
                        c.SamplePos = 0;
                        c.Playing = true;
                        c.Volume = 127;
                        c.VolumeDiv = 1;
                    }
                    ImGui.SameLine(); ImGui.Text("Interval: " + Hex(c.Interval, 4));
                    ImGui.SameLine(); ImGui.Text("Len: " + (totalSamples - loopSamples));

                    ImGui.Text("Source: " + HexN(c.SOUNDSAD, 7));
                    ImGui.SameLine(); ImGui.Text("Repeat: " + c.RepeatMode);
                    ImGui.SameLine(); ImGui.Text("Format: " + c.Format);

                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 40);

                    ImGui.Dummy(size);

                }
                ImGui.End();
            }
        }
    }
}