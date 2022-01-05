using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL;
using System;
using System.IO;
using ImGuiNET;
using static OptimeGBA.MemoryUtil;
using System.Threading;
using ImGuiUtils;
using static Util;
using System.Collections.Generic;
using OptimeGBA;
using System.Runtime.Intrinsics.X86;
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
        Window Window;

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
                while (cyclesLeft > 0 && !Nds.Cpu7.Errored && !Nds.Cpu9.Errored)
                {
                    cyclesLeft -= (int)Nds.Step();
                    CheckBreakpoints();
                }

                while (!SyncToAudio && !Nds.Cpu7.Errored && !Nds.Cpu9.Errored && Window.RunEmulator)
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
            var arm9 = Nds.Cpu9;
            var arm7 = Nds.Cpu7;

            uint addr9 = arm9.GetCurrentInstrAddr();
            uint addr7 = arm7.GetCurrentInstrAddr();

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
            while (cycles > 0 && !Nds.Cpu7.Errored && !Nds.Cpu9.Errored && Window.RunEmulator)
            {
                cycles -= (int)Nds.Step();
            }
        }

        int CyclesLeft;
        public void RunFrame()
        {
            CyclesLeft += FrameCycles;
            while (CyclesLeft > 0 && !Nds.Cpu7.Errored && !Nds.Cpu9.Errored)
            {
                CyclesLeft -= (int)Nds.Step();
            }
        }

        public void RunScanline()
        {
            CyclesLeft += ScanlineCycles;
            while (CyclesLeft > 0 && !Nds.Cpu7.Errored && !Nds.Cpu9.Errored)
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

        public static uint GetAudioSamplesInQueue()
        {
            return SDL_GetQueuedAudioSize(AudioDevice) / sizeof(short);
        }

        public WindowNds(Window window)
        {
            Window = window;

            // Init SDL
            byte[] bios7 = System.IO.File.ReadAllBytes("bios7.bin");
            byte[] bios9 = System.IO.File.ReadAllBytes("bios9.bin");
            byte[] firmware = System.IO.File.ReadAllBytes("firmware.bin");
            Nds = new Nds(new ProviderNds(bios7, bios9, firmware, new byte[0], "", AudioReady) { DirectBoot = true });

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
        }

        public void LoadRomAndSave(byte[] rom, byte[] sav, string savPath)
        {
            var bios7 = Nds.Provider.Bios7;
            var bios9 = Nds.Provider.Bios9;
            var firmware = Nds.Provider.Firmware;
            Nds = new Nds(new ProviderNds(bios7, bios9, firmware, rom, savPath, AudioReady) { DirectBoot = true });
            Nds.Cartridge.LoadSave(sav);
        }

        public double Time;
        public bool RecordTime;
        public uint RecordStartFrames;

        public void OnUpdateFrame(FrameEventArgs e)
        {
            Window.VSync = VSyncMode.Off;
            Window.UpdateFrequency = 5585644D / (355D * 263D);

            Nds.Keypad.B = Window.KeyboardState.IsKeyDown(Keys.Z);
            Nds.Keypad.A = Window.KeyboardState.IsKeyDown(Keys.X);
            Nds.Keypad.Y = Window.KeyboardState.IsKeyDown(Keys.A);
            Nds.Keypad.X = Window.KeyboardState.IsKeyDown(Keys.S);
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

            if (Window.RunEmulator)
            {
                FrameNow = true;
                ThreadSync.Set();
            }

            if (RecordTime)
            {
                Time += e.Time;
            }

            if (Nds.Mem7.SaveProvider.Dirty)
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
            DrawVoidTool();
        }

        public void ResetNds()
        {
            byte[] save = Nds.Cartridge.GetSave();
            ProviderNds p = Nds.Provider;
            Nds = new Nds(p);
            Nds.Cartridge.LoadSave(save);
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
                ImGui.Text("R13: " + Hex(Nds.Cpu7.GetModeReg(13, Arm7Mode.USR), 8));
                ImGui.Text("R14: " + Hex(Nds.Cpu7.GetModeReg(14, Arm7Mode.USR), 8));

                ImGui.NextColumn();

                ImGui.Text("Supervisor");
                ImGui.Text("R13: " + Hex(Nds.Cpu7.GetModeReg(13, Arm7Mode.SVC), 8));
                ImGui.Text("R14: " + Hex(Nds.Cpu7.GetModeReg(14, Arm7Mode.SVC), 8));

                ImGui.NextColumn();

                ImGui.Text("Abort");
                ImGui.Text("R13: " + Hex(Nds.Cpu7.GetModeReg(13, Arm7Mode.ABT), 8));
                ImGui.Text("R14: " + Hex(Nds.Cpu7.GetModeReg(14, Arm7Mode.ABT), 8));

                ImGui.NextColumn();

                ImGui.Text("IRQ");
                ImGui.Text("R13: " + Hex(Nds.Cpu7.GetModeReg(13, Arm7Mode.IRQ), 8));
                ImGui.Text("R14: " + Hex(Nds.Cpu7.GetModeReg(14, Arm7Mode.IRQ), 8));

                ImGui.NextColumn();

                ImGui.Text("Undefined");
                ImGui.Text("R13: " + Hex(Nds.Cpu7.GetModeReg(13, Arm7Mode.UND), 8));
                ImGui.Text("R14: " + Hex(Nds.Cpu7.GetModeReg(14, Arm7Mode.UND), 8));

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
                    ImGui.Spacing();
                }

                ImGui.Separator();

                MemoryViewerHover = false;

                ImGui.BeginChild("Memory");
                for (int i = 0; i < rows; i++)
                {
                    ImGui.Text($"{Util.HexN(tempBase, 8)}:");
                    for (int j = 0; j < cols; j++)
                    {
                        uint val = Nds.Mem9.Read8(tempBase);

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
            text += $"{HexN(Nds.Cpu7.R[0], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[1], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[2], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[3], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[4], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[5], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[6], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[7], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[8], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[9], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[10], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[11], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[12], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[13], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[14], 8)} ";
            text += $"{HexN(Nds.Cpu7.R[15], 8)} ";
            text += $"cpsr: {HexN(Nds.Cpu7.GetCPSR(), 8)} ";
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
                ImGui.Text(Nds.Cpu7.Debug);
                ImGui.Separator();
                ImGui.Text(Nds.Cpu9.Debug);
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
                drawCpuInfo(Nds.Cpu9);
                displayCheckbox("IRQ Disable", Nds.Cpu9.IRQDisable);
                ImGui.NextColumn();
                ImGui.Text("ARM7");
                drawCpuInfo(Nds.Cpu7);
                displayCheckbox("IRQ Disable", Nds.Cpu7.IRQDisable);
                ImGui.Text($"Total Steps: " + Nds.Steps);
                // ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                // ImGui.Text($"Ins Next Up: {(Nds.Cpu7.ThumbState ? Hex(Nds.Cpu7.THUMBDecode, 4) : Hex(Nds.Cpu7.ARMDecode, 8))}");

                ImGui.Text($"");

                if (ImGui.Button("flag ARM7 RTC IRQ"))
                {
                    Nds.HwControl7.FlagInterrupt((uint)InterruptNds.Rtc);
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
                    Nds.Cpu7.Errored = false;
                    Nds.Cpu9.Errored = false;
                }
                if (ImGui.Button("Step"))
                {
                    Nds.Step();
                    LogIndex++;
                }
                if (ImGui.Button("Step until ARM9 unhalted"))
                {
                    while (Nds.Cpu9.Halted)
                    {
                        Nds.Step();
                    }
                }
                // if (ImGui.Button("Step Until Error"))
                // {
                //     bool exit = false;
                //     while (!Nds.Cpu7.Errored && !exit)
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
                if (ImGui.Button("Step For " + DebugStepFor))
                {
                    int num = DebugStepFor;
                    while (num > 0 && !Nds.Cpu7.Errored)
                    {
                        Nds.Step();
                        num--;
                    }
                }

                var times = DebugStepFor;
                if (ImGui.Button("Step For " + DebugStepFor + " & Log"))
                {
                    using (StreamWriter file7 = new StreamWriter("log7.txt"), file9 = new StreamWriter("log9.txt"))
                    {
                        Action nds7Executed = () =>
                        {
                            file7.WriteLine(BuildEmuFullText(Nds.Cpu7));
                            if (Nds.Cpu7.InterruptServiced)
                            {
                                Nds.Cpu7.InterruptServiced = false;
                                file7.WriteLine("---------------- INTERRUPT ----------------");
                            }
                        };

                        Action nds9Executed = () =>
                        {
                            file9.WriteLine(BuildEmuFullText(Nds.Cpu9));
                            if (Nds.Cpu9.InterruptServiced)
                            {
                                Nds.Cpu9.InterruptServiced = false;
                                file9.WriteLine("---------------- INTERRUPT ----------------");
                            }
                        };

                        Nds.Cpu7.PreExecutionCallback = nds7Executed;
                        Nds.Cpu9.PreExecutionCallback = nds9Executed;

                        int num = times;
                        while (num > 0 && !Nds.Cpu9.Errored && !Nds.Cpu7.Errored)
                        {
                            Nds.Step();

                            LogIndex++;
                            num--;
                        }

                        Nds.Cpu7.PreExecutionCallback = null;
                        Nds.Cpu9.PreExecutionCallback = null;
                    }
                }


                ImGui.Checkbox("Run Emulator", ref Window.RunEmulator);

                ImGui.NextColumn();
                // ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 150);

                // ImGui.Text($"BIOS Reads: {Nds.Mem7.BiosReads}");
                // ImGui.Text($"EWRAM Reads: {Nds.Mem7.EwramReads}");
                // ImGui.Text($"IWRAM Reads: {Nds.Mem7.IwramReads}");
                // ImGui.Text($"ROM Reads: {Nds.Mem7.RomReads}");
                // ImGui.Text($"HWIO Reads: {Nds.Mem7.HwioReads}");
                // ImGui.Text($"Palette Reads: {Nds.Mem7.PaletteReads}");
                // ImGui.Text($"VRAM Reads: {Nds.Mem7.VramReads}");
                // ImGui.Text($"OAM Reads: {Nds.Mem7.OamReads}");
                ImGui.Spacing();
                // ImGui.Text($"EWRAM Writes: {Nds.Mem7.EwramWrites}");
                // ImGui.Text($"IWRAM Writes: {Nds.Mem7.IwramWrites}");
                // ImGui.Text($"HWIO Writes: {Nds.Mem7.HwioWrites}");
                // ImGui.Text($"Palette Writes: {Nds.Mem7.PaletteWrites}");
                // ImGui.Text($"VRAM Writes: {Nds.Mem7.VramWrites}");
                // ImGui.Text($"OAM Writes: {Nds.Mem7.OamWrites}");
                ImGui.Spacing();
                // bool ticked = Nds.HwControl.IME;
                // ImGui.Checkbox("IME", ref ticked);

                ImGui.Checkbox("Log HWIO", ref LogHwioAccesses);
                Nds.Mem7.LogHwioAccesses = LogHwioAccesses;
                Nds.Mem9.LogHwioAccesses = LogHwioAccesses;
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
                ImGui.Text($"0 Src:   {Hex(Nds.Dma9.Ch[0].DmaSource, 8)}");
                ImGui.Text($"1 Src:   {Hex(Nds.Dma9.Ch[1].DmaSource, 8)}");
                ImGui.Text($"2 Src:   {Hex(Nds.Dma9.Ch[2].DmaSource, 8)}");
                ImGui.Text($"3 Src:   {Hex(Nds.Dma9.Ch[3].DmaSource, 8)}");
                ImGui.Text($"0 Dest:  {Hex(Nds.Dma9.Ch[0].DmaDest, 8)}");
                ImGui.Text($"1 Dest:  {Hex(Nds.Dma9.Ch[1].DmaDest, 8)}");
                ImGui.Text($"2 Dest:  {Hex(Nds.Dma9.Ch[2].DmaDest, 8)}");
                ImGui.Text($"3 Dest:  {Hex(Nds.Dma9.Ch[3].DmaDest, 8)}");
                ImGui.Text($"0 Words: {Hex(Nds.Dma9.Ch[0].DMACNT_L, 4)}");
                ImGui.Text($"1 Words: {Hex(Nds.Dma9.Ch[1].DMACNT_L, 4)}");
                ImGui.Text($"2 Words: {Hex(Nds.Dma9.Ch[2].DMACNT_L, 4)}");
                ImGui.Text($"3 Words: {Hex(Nds.Dma9.Ch[3].DMACNT_L, 4)}");
                ImGui.Text($"0 Start: {((DmaStartTimingNds9)(Nds.Dma9.Ch[0].StartTiming)).ToString()}");
                ImGui.Text($"1 Start: {((DmaStartTimingNds9)(Nds.Dma9.Ch[1].StartTiming)).ToString()}");
                ImGui.Text($"2 Start: {((DmaStartTimingNds9)(Nds.Dma9.Ch[2].StartTiming)).ToString()}");
                ImGui.Text($"3 Start: {((DmaStartTimingNds9)(Nds.Dma9.Ch[3].StartTiming)).ToString()}");

                ImGui.Text("NDS7---------------");
                ImGui.Text($"0 Src:   {Hex(Nds.Dma7.Ch[0].DmaSource, 8)}");
                ImGui.Text($"1 Src:   {Hex(Nds.Dma7.Ch[1].DmaSource, 8)}");
                ImGui.Text($"2 Src:   {Hex(Nds.Dma7.Ch[2].DmaSource, 8)}");
                ImGui.Text($"3 Src:   {Hex(Nds.Dma7.Ch[3].DmaSource, 8)}");
                ImGui.Text($"0 Dest:  {Hex(Nds.Dma7.Ch[0].DmaDest, 8)}");
                ImGui.Text($"1 Dest:  {Hex(Nds.Dma7.Ch[1].DmaDest, 8)}");
                ImGui.Text($"2 Dest:  {Hex(Nds.Dma7.Ch[2].DmaDest, 8)}");
                ImGui.Text($"3 Dest:  {Hex(Nds.Dma7.Ch[3].DmaDest, 8)}");
                ImGui.Text($"0 Words: {Hex(Nds.Dma7.Ch[0].DMACNT_L, 4)}");
                ImGui.Text($"1 Words: {Hex(Nds.Dma7.Ch[1].DMACNT_L, 4)}");
                ImGui.Text($"2 Words: {Hex(Nds.Dma7.Ch[2].DMACNT_L, 4)}");
                ImGui.Text($"3 Words: {Hex(Nds.Dma7.Ch[3].DMACNT_L, 4)}");
                ImGui.Text($"0 Start: {((DmaStartTimingNds7)(Nds.Dma7.Ch[0].StartTiming)).ToString()}");
                ImGui.Text($"1 Start: {((DmaStartTimingNds7)(Nds.Dma7.Ch[1].StartTiming)).ToString()}");
                ImGui.Text($"2 Start: {((DmaStartTimingNds7)(Nds.Dma7.Ch[2].StartTiming)).ToString()}");
                ImGui.Text($"3 Start: {((DmaStartTimingNds7)(Nds.Dma7.Ch[3].StartTiming)).ToString()}");

                ImGuiColumnSeparator();

                ImGui.NextColumn();

                ImGui.Text("A---------------");

                ImGui.Checkbox("Force Display 3D Layer", ref Nds.Ppu.Renderers[0].DebugForce3DLayer);

                var rendA = Nds.Ppu.Renderers[0];
                ImGui.Text($"BG0 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[0].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[0].ScreenSize]}");
                ImGui.Text($"BG0 Scroll X/Y: {rendA.Backgrounds[0].HorizontalOffset}/{rendA.Backgrounds[0].VerticalOffset}");
                ImGui.Text($"BG1 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[1].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[1].ScreenSize]}");
                ImGui.Text($"BG2 Scroll X/Y: {rendA.Backgrounds[1].HorizontalOffset}/{rendA.Backgrounds[1].VerticalOffset}");
                ImGui.Text($"BG2 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[2].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Affine Size: {PpuRenderer.AffineSizeTable[rendA.Backgrounds[2].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendA.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X/Y: {rendA.Backgrounds[2].HorizontalOffset}/{rendA.Backgrounds[2].VerticalOffset}");
                ImGui.Text($"BG3 Size X/Y: {PpuRenderer.CharWidthTable[rendA.Backgrounds[3].ScreenSize]}/{PpuRenderer.CharHeightTable[rendA.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Affine Size: {PpuRenderer.AffineSizeTable[rendA.Backgrounds[3].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendA.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X/Y: {rendA.Backgrounds[3].HorizontalOffset}/{rendA.Backgrounds[3].VerticalOffset}");
                ImGui.Text("Debug BG0123/OBJ");
                ImGui.Checkbox("##rendAbg0", ref rendA.DebugEnableBg[0]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg1", ref rendA.DebugEnableBg[1]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg2", ref rendA.DebugEnableBg[2]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAbg3", ref rendA.DebugEnableBg[3]);
                ImGui.SameLine(); ImGui.Checkbox("##rendAobj", ref rendA.DebugEnableObj);

                ImGui.Text("B---------------");
                var rendB = Nds.Ppu.Renderers[1];
                ImGui.Text($"BG0 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[0].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[0].ScreenSize]}");
                ImGui.Text($"BG0 Scroll X/Y: {rendB.Backgrounds[0].HorizontalOffset}/{rendB.Backgrounds[0].VerticalOffset}");
                ImGui.Text($"BG1 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[1].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[1].ScreenSize]}");
                ImGui.Text($"BG2 Scroll X/Y: {rendB.Backgrounds[1].HorizontalOffset}/{rendB.Backgrounds[1].VerticalOffset}");
                ImGui.Text($"BG2 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[2].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Affine Size: {PpuRenderer.AffineSizeTable[rendB.Backgrounds[2].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendB.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X/Y: {rendB.Backgrounds[2].HorizontalOffset}/{rendB.Backgrounds[2].VerticalOffset}");
                ImGui.Text($"BG3 Size X/Y: {PpuRenderer.CharWidthTable[rendB.Backgrounds[3].ScreenSize]}/{PpuRenderer.CharHeightTable[rendB.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Affine Size: {PpuRenderer.AffineSizeTable[rendB.Backgrounds[3].ScreenSize]}/{PpuRenderer.AffineSizeTable[rendB.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X/Y: {rendB.Backgrounds[3].HorizontalOffset}/{rendB.Backgrounds[3].VerticalOffset}");
                ImGui.Text("Debug BG0123/OBJ");
                ImGui.Checkbox("##rendBbg0", ref rendB.DebugEnableBg[0]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg1", ref rendB.DebugEnableBg[1]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg2", ref rendB.DebugEnableBg[2]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBbg3", ref rendB.DebugEnableBg[3]);
                ImGui.SameLine(); ImGui.Checkbox("##rendBobj", ref rendB.DebugEnableObj);

                ImGuiColumnSeparator();

                // ImGui.Text("Viewport 1 X: " + Nds.Ppu3D.Viewport1[0]);
                // ImGui.Text("Viewport 1 Y: " + Nds.Ppu3D.Viewport1[1]);
                // ImGui.Text("Viewport 2 X: " + Nds.Ppu3D.Viewport2[0]);
                // ImGui.Text("Viewport 2 Y: " + Nds.Ppu3D.Viewport2[1]);

                // doesn't work right now because different matrices can be used in the same frame
                ImGui.Text($"Matrix Mode: {Nds.Ppu3D.MatrixMode}");
                ImGui.Text("Projection");
                DisplayMatrix(ref Nds.Ppu3D.ProjectionStack.Current);
                ImGui.Text("Position");
                DisplayMatrix(ref Nds.Ppu3D.PositionStack.Current);
                ImGui.Text("Direction");
                DisplayMatrix(ref Nds.Ppu3D.DirectionStack.Current);

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
                ImGui.Text($"Timer 0 Counter: {Hex(Nds.Timers9.T[0].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Nds.Timers9.T[1].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Nds.Timers9.T[2].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Nds.Timers9.T[3].CalculateCounter(), 4)}");
                ImGui.Spacing();
                ImGui.Text($"Timer 0 Reload: {Hex(Nds.Timers9.T[0].ReloadVal, 4)}");
                ImGui.Text($"Timer 1 Reload: {Hex(Nds.Timers9.T[1].ReloadVal, 4)}");
                ImGui.Text($"Timer 2 Reload: {Hex(Nds.Timers9.T[2].ReloadVal, 4)}");
                ImGui.Text($"Timer 3 Reload: {Hex(Nds.Timers9.T[3].ReloadVal, 4)}");
                ImGui.Spacing();
                ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Nds.Timers9.T[0].PrescalerSel]}");
                ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Nds.Timers9.T[1].PrescalerSel]}");
                ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Nds.Timers9.T[2].PrescalerSel]}");
                ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Nds.Timers9.T[3].PrescalerSel]}");
                ImGui.Text("NDS7---------------");
                ImGui.Text($"Timer 0 Counter: {Hex(Nds.Timers7.T[0].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Nds.Timers7.T[1].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Nds.Timers7.T[2].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Nds.Timers7.T[3].CalculateCounter(), 4)}");
                ImGui.Spacing();
                ImGui.Text($"Timer 0 Reload: {Hex(Nds.Timers7.T[0].ReloadVal, 4)}");
                ImGui.Text($"Timer 1 Reload: {Hex(Nds.Timers7.T[1].ReloadVal, 4)}");
                ImGui.Text($"Timer 2 Reload: {Hex(Nds.Timers7.T[2].ReloadVal, 4)}");
                ImGui.Text($"Timer 3 Reload: {Hex(Nds.Timers7.T[3].ReloadVal, 4)}");
                ImGui.Spacing();
                ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Nds.Timers7.T[0].PrescalerSel]}");
                ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Nds.Timers7.T[1].PrescalerSel]}");
                ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Nds.Timers7.T[2].PrescalerSel]}");
                ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Nds.Timers7.T[3].PrescalerSel]}");


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

                ImGui.Text("Firmware State: " + Nds.Spi.Flash.FlashState.ToString());
                ImGui.Text("Firmware Addr: " + Hex(Nds.Spi.Flash.Address, 6));
                ImGui.Text("Slot 1 Access: " + (Nds.MemoryControl.Nds7Slot1AccessRights ? "ARM7" : "ARM9"));
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

        public void DisplayMatrix(ref Matrix m)
        {
            ImGui.Text($"{HexN((uint)m.Data[0x0], 8)} {HexN((uint)m.Data[0x1], 8)} {HexN((uint)m.Data[0x2], 8)} {HexN((uint)m.Data[0x3], 8)}");
            ImGui.Text($"{HexN((uint)m.Data[0x4], 8)} {HexN((uint)m.Data[0x5], 8)} {HexN((uint)m.Data[0x6], 8)} {HexN((uint)m.Data[0x7], 8)}");
            ImGui.Text($"{HexN((uint)m.Data[0x8], 8)} {HexN((uint)m.Data[0x9], 8)} {HexN((uint)m.Data[0xA], 8)} {HexN((uint)m.Data[0xB], 8)}");
            ImGui.Text($"{HexN((uint)m.Data[0xC], 8)} {HexN((uint)m.Data[0xD], 8)} {HexN((uint)m.Data[0xE], 8)} {HexN((uint)m.Data[0xF], 8)}");
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
                drawDisassembly(Nds.Cpu9);
                ImGui.NextColumn();
                ImGui.Text("ARM7\n");
                drawDisassembly(Nds.Cpu7);
                ImGui.Columns(1);

                ImGui.End();
            }
        }

        public uint[] DisplayBuffer = new uint[256 * 192];

        public bool BigScreen = false;
        public bool ShowBackBuf = false;
        public unsafe void DrawDisplay()
        {
            if (ImGui.Begin("Display", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

                float width = BigScreen ? 256 * 4 : 256 * 2;
                float height = BigScreen ? 192 * 4 : 192 * 2;

                for (int i = 0; i < 2; i++)
                {
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, screenTexIds[i]);

                    // TexParameter needed for something to display :)
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
                    var renderer = Nds.Ppu.Renderers[i ^ (Nds.DisplaySwap ? 0 : 1)];

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

                    var screenPos = ImGui.GetCursorScreenPos();

                    var uv0 = new Vector2(0.0f, 0.0f);
                    var uv1 = new Vector2(1.0f, 1.0f);
                    var size = new Vector2(width, height);

                    if (i == 1)
                    {
                        float x = Window.MouseState.X - screenPos.X;
                        float y = Window.MouseState.Y - screenPos.Y;

                        bool down = Window.MouseState.IsButtonDown(MouseButton.Left);
                        ImGui.ImageButton((IntPtr)screenTexIds[i], size, uv0, uv1, 0);

                        // Normalize
                        uint touchX = (uint)((x / size.X) * 256f);
                        uint touchY = (uint)((y / size.Y) * 192f);

                        if (touchX < 256 && touchY < 192)
                        {
                            // ImGui.Text($"({touchX}, {touchY}) {(down ? "DOWN" : "")}");

                            if (down)
                            {
                                Nds.Spi.SetTouchPos(touchX, touchY);
                                Nds.Keypad.Touch = true;
                            }
                            else
                            {
                                Nds.Spi.ClearTouchPos();
                                Nds.Keypad.Touch = false;
                            }
                        }
                        else
                        {
                            Nds.Spi.ClearTouchPos();
                            Nds.Keypad.Touch = false;
                        }
                    }
                    else
                    {
                        ImGui.Image((IntPtr)screenTexIds[i], size, uv0, uv1);
                    }
                }

                ImGui.PopStyleVar();

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
                uint value = Nds.Mem9.ReadDebug32(RegViewerSelected9.Address);
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
                value = Nds.Mem7.ReadDebug32(RegViewerSelected7.Address);
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
                    ThumbExecutor k = Nds.Cpu9.ThumbDispatch[ti];
                    if (!CpuProfilerDictThumb.TryGetValue(k, out uint val))
                    {
                        CpuProfilerDictThumb[k] = 0;
                    }
                    CpuProfilerDictThumb[k] += Arm7.ThumbExecutorProfile[ti];
                }

                for (int ai = 0; ai < 4096; ai++)
                {
                    ArmExecutor k = Nds.Cpu9.ArmDispatch[ai];
                    if (!CpuProfilerDictArm.TryGetValue(k, out uint val))
                    {
                        CpuProfilerDictArm[k] = 0;
                    }
                    CpuProfilerDictArm[k] += Arm7.ArmExecutorProfile[ai];
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
                        lock (Nds.Mem9.HwioReadLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Mem9.HwioReadLog)
                            {
                                file9.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                            }
                        }
                        file9.WriteLine("---------");
                        lock (Nds.Mem9.HwioWriteLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Mem9.HwioWriteLog)
                            {
                                file9.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                            }
                        }
                    }

                    using (StreamWriter file7 = new StreamWriter("hwio7.txt"))
                    {
                        file7.WriteLine("ARM7");
                        lock (Nds.Mem7.HwioReadLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Mem7.HwioReadLog)
                            {
                                file7.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                            }
                        }
                        file7.WriteLine("---------");
                        lock (Nds.Mem7.HwioWriteLog)
                        {
                            foreach (KeyValuePair<uint, uint> entry in Nds.Mem7.HwioWriteLog)
                            {
                                file7.WriteLine($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                            }
                        }
                    }
                }

                ImGui.Columns(2);

                ImGui.Text("ARM9");
                lock (Nds.Mem9.HwioReadLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Mem9.HwioReadLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                    }
                }
                ImGui.Text("---------");
                lock (Nds.Mem9.HwioWriteLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Mem9.HwioWriteLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                    }
                }

                ImGui.NextColumn();

                ImGui.Text("ARM7");
                lock (Nds.Mem7.HwioReadLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Mem7.HwioReadLog)
                    {
                        ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                    }
                }
                ImGui.Text("---------");
                lock (Nds.Mem7.HwioWriteLog)
                {
                    foreach (KeyValuePair<uint, uint> entry in Nds.Mem7.HwioWriteLog)
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
                displayCheckbox("IME##arm9", Nds.HwControl9.IME);
                drawInterruptColumn(Nds.HwControl9.IE, Nds.HwControl9.IF, false);
                ImGui.NextColumn();
                ImGui.Text("ARM7");
                displayCheckbox("IME##arm7", Nds.HwControl7.IME);
                drawInterruptColumn(Nds.HwControl7.IE, Nds.HwControl7.IF, true);

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

        public uint LerpColor(uint c0, uint c1, float factor) {
            int c00 = (int)(c0 >> 0) & 0xFF; 
            int c01 = (int)(c0 >> 8) & 0xFF; 
            int c02 = (int)(c0 >> 16) & 0xFF; 
            int c03 = (int)(c0 >> 24) & 0xFF;
            int c10 = (int)(c1 >> 0) & 0xFF;
            int c11 = (int)(c1 >> 8) & 0xFF;
            int c12 = (int)(c1 >> 16) & 0xFF;
            int c13 = (int)(c1 >> 24) & 0xFF;
 
            uint cf0 = (uint)((c10 - c00) * factor + c00) & 0xFF; 
            uint cf1 = (uint)((c11 - c01) * factor + c01) & 0xFF; 
            uint cf2 = (uint)((c12 - c02) * factor + c02) & 0xFF; 
            uint cf3 = (uint)((c13 - c03) * factor + c03) & 0xFF; 
            
            return cf0 | (cf1 << 8) | (cf2 << 16) | (cf3 << 24);
        }

        public string[] SoundDivs = new string[] { "/1", "/2", "/4", "/16" };

        public void DrawSoundVisualizer()
        {
            if (ImGui.Begin("Sound Visualizer"))
            {
                // if (ImGui.Button("Dump Shared Memory")) {
                    // System.IO.File.WriteAllBytes("sharedram.bin", Nds.SharedRam);
                // }

                 if (ImGui.Button("Dump ARM7 Memory")) {
                    System.IO.File.WriteAllBytes("arm7wram.bin", Nds.Mem7.Arm7Wram);
                }


                ImGui.Checkbox("Enable Resampling", ref Nds.Audio.EnableBlipBufResampling);

                if (Nds.Audio.Record) {
                    if (ImGui.Button("Stop Recording")) {
                        Nds.Audio.Record = false;

                        Nds.Audio.WavWriter.Save("nds.wav");
                        Nds.Audio.WavWriterSinc.Save("nds-sinc.wav");
                    }
                } else {
                    if (ImGui.Button("Start Recording")) {
                        Nds.Audio.Record = true;
                    }
                }

                for (uint i = 0; i < 16; i++)
                {
                    var c = Nds.Audio.Channels[i];

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
                    uint crcColor = c.SOUNDSAD;
                    for (uint j = 0; j < 8; j++)
                    {
                        crcColor = Sse42.Crc32(j, crcColor);
                    }
                    var bgColor = c.Playing ? crcColor : ImGui.GetColorU32(ImGuiCol.Border);
                    drawList.AddRectFilled(pos, pos + size, bgColor); // fill BG

                    if (c.Playing)
                    {
                        drawList.AddRectFilled(new Vector2(pos.X + size.X * fillPortion - 2, pos.Y), new Vector2(pos.X + size.X * fillPortion, pos.Y + size.Y - 1), 0xFF00FF00);
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

                    uint startBoxColorInactive = 0xFF222222;
                    uint startBoxColorActive = 0xFF444444;
                    uint startBoxColor;
                    const uint startBoxFadeTicks = 8388608; // about 0.25 seconds
                    long ticksSinceStart = Nds.Scheduler.CurrentTicks - c.DebugStartTicks;
                    if (ticksSinceStart > 0 && ticksSinceStart < startBoxFadeTicks) {
                        startBoxColor = LerpColor(0xFFFFFFFF, startBoxColorActive, (float)ticksSinceStart / (float)startBoxFadeTicks);
                    // if (i == 0) Console.WriteLine((float)ticksSinceStart / (float)startBoxFadeTicks);
                    } else {
                        startBoxColor = startBoxColorInactive;
                    }
                    drawList.AddRectFilled(pos + new Vector2(size.X - 8, 0), pos + size, startBoxColor);
                    drawList.AddRect(pos, pos + size, ImGui.GetColorU32(ImGuiCol.Border)); // border for start box

                    ImGui.Checkbox("##soundvis-check" + i, ref c.DebugEnable); ImGui.SameLine();
                    if (ImGui.Button("Solo##soundvis" + i))
                    {
                        int numEnabled = 0;
                        for (uint chI = 0; chI < 16; chI++)
                        {
                            if (Nds.Audio.Channels[chI].DebugEnable)
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
                                    Nds.Audio.Channels[chI].DebugEnable = true;
                                }
                                else if (chI != i)
                                {
                                    Nds.Audio.Channels[chI].DebugEnable = false;
                                }
                            }
                            else
                            {
                                if (chI != i)
                                {
                                    Nds.Audio.Channels[chI].DebugEnable = false;
                                }
                                else
                                {
                                    Nds.Audio.Channels[chI].DebugEnable = true;
                                }
                            }
                        }
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Replay##soundvis" + i))
                    {
                        c.SamplePos = 0;
                        c.Playing = true;
                        c.Volume = 127;
                        c.VolumeDiv = 1;
                    }
                    ImGui.SameLine(); ImGui.Text("Interval: " + Hex(c.Interval, 4));
                    ImGui.SameLine(); ImGui.Text("Len: " + (totalSamples - loopSamples));
                    ImGui.SameLine(); ImGui.Text("Volume: " + c.Volume + SoundDivs[c.VolumeDiv]);

                    ImGui.Text("Source: " + HexN(c.SOUNDSAD, 7));
                    ImGui.SameLine(); ImGui.Text("Data: " + Hex(c.CurrentData, 8));
                    ImGui.SameLine(); ImGui.Text("Value: " + Hex((ushort)c.CurrentValue, 4));
                    float hz = 33513982F / (float)c.Interval;
                    ImGui.SameLine(); ImGui.Text("Hz: " + string.Format("{0:0.#}", hz));
                    // ImGui.SameLine(); ImGui.Text("Repeat: " + c.RepeatMode);
                    // ImGui.SameLine(); ImGui.Text("Format: " + c.Format);

                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 40);

                    ImGui.Dummy(size);

                }

                ImGui.Text("SOUNDBIAS: " + Hex(Nds.Audio.SOUNDBIAS, 8));

                ImGui.End();
            }
        }

        uint ColorBright(uint col, float mul)
        {
            byte r = (byte)((byte)(col >> 0) * mul);
            byte g = (byte)((byte)(col >> 8) * mul);
            byte b = (byte)((byte)(col >> 16) * mul);
            byte a = (byte)(col >> 24);

            return (uint)((a << 24) | (b << 16) | (g << 8) | r);
        }

        public static string[] PokemonGen4GameNames = {
            "<invalid Generation 4 Pokémon game>",
            "Pokémon Diamond",
            "Pokémon Pearl",
            "Pokémon Platinum",
        };

        public class PokemonGen4Details
        {
            public uint BasePtrPtr;
            public string LangCode;

            public PokemonGen4Details(uint basePtrPtr, string langCode)
            {
                BasePtrPtr = basePtrPtr;
                LangCode = langCode;
            }
        }

        public static uint[] MapColors = new uint[] {
            0xFFFFFFFF,
            0xFFCF831E,
            0xFF8E12E1,
            0xFF888888,
            0xFF00A5FF,
            0xFFAD0D6A,
            0xFF00FFFF,
            0xFF0000FF,
            0xFFBBFF66,
        };

        public static string[] MapColorsNames = new string[] {
            "Normal",
            "Highlight",
            "Highlight 2",
            "Mystery Zone",
            "Blackout",
            "Movement",
            "Void Exit",
            "BSOD",
            "Jubilife City",
        };

        public static Dictionary<uint, uint> PokemonGen4MapColors = new Dictionary<uint, uint>() {
            {253, 1}, // Highlight
            {523, 2}, // Highlight 2
            {0, 3}, // Mystery Zone
            {332, 4}, // Blackout
            {333, 4},
            {117, 5}, // Movement
            {177, 5},
            {179, 5},
            {181, 5},
            {183, 5},
            {192, 5},
            {393, 5},
            {474, 5},
            {475, 5},
            {476, 5},
            {477, 5},
            {478, 5},
            {479, 5},
            {480, 5},
            {481, 5},
            {482, 5},
            {483, 5},
            {484, 5},
            {485, 5},
            {486, 5},
            {487, 5},
            {488, 5},
            {489, 5},
            {490, 5},
            {496, 5},
            {105, 6}, // Void Exit
            {114, 6},
            {337, 6},
            {461, 6},
            {516, 6},
            {186, 6},
            {187, 6},
            {35, 7}, // BSOD
			{88, 7},
            {91, 7},
            {93, 7},
            {95, 7},
            {115, 7},
            {122, 7},
            {150, 7},
            {154, 7},
            {155, 7},
            {156, 7},
            {176, 7},
            {178, 7},
            {180, 7},
            {182, 7},
            {184, 7},
            {185, 7},
            {188, 7},
            {3, 8}, // Jubilife City 
        };

        public static Dictionary<uint, PokemonGen4Details>[] PokemonGen4BasePtrs = new Dictionary<uint, PokemonGen4Details>[] {
            // Diamond & Pearl Demo
            new Dictionary<uint, PokemonGen4Details>() {
                {0x45, new PokemonGen4Details(0x02106BAC, "US / EU") },
            },
            // Diamond & Pearl
            new Dictionary<uint, PokemonGen4Details>() {
                {0x44, new PokemonGen4Details(0x02107100, "DE")},
                {0x45, new PokemonGen4Details(0x02106FC0, "US / EU")},
                {0x46, new PokemonGen4Details(0x02107140, "FR")},
                {0x49, new PokemonGen4Details(0x021070A0, "IT")},
                {0x4A, new PokemonGen4Details(0x02108818, "JP")},
                {0x4B, new PokemonGen4Details(0x021045C0, "KS")},
                {0x53, new PokemonGen4Details(0x02107160, "ES")},
            },
            // Platinum
            new Dictionary<uint, PokemonGen4Details>() {
                {0x44, new PokemonGen4Details(0x02101EE0, "DE")},
                {0x45, new PokemonGen4Details(0x02101D40, "US / EU")},
                {0x46, new PokemonGen4Details(0x02101F20, "FR")},
                {0x49, new PokemonGen4Details(0x02101EA0, "IT")},
                {0x4A, new PokemonGen4Details(0x02101140, "JP")},
                {0x4B, new PokemonGen4Details(0x02102C40, "KS")},
                {0x53, new PokemonGen4Details(0x02101F40, "ES")},
            },
            // HeartGold & SoulSilver
            new Dictionary<uint, PokemonGen4Details>() {
                {0x44, new PokemonGen4Details(0x02111860, "DE")},
                {0x45, new PokemonGen4Details(0x02111880, "US / EU")},
                {0x46, new PokemonGen4Details(0x021118A0, "FR")},
                {0x49, new PokemonGen4Details(0x02111820, "IT")},
                {0x4A, new PokemonGen4Details(0x02110DC0, "JP")},
                {0x4B, new PokemonGen4Details(0x02112280, "KS")},
                {0x53, new PokemonGen4Details(0x021118C0, "ES")},
            },
        };

        public void DrawVoidTool()
        {

            byte version = 0;

            // Make sure ROM is actually long enough
            if (Nds.Provider.Rom.Length >= 0x10)
            {
                // "POKEMON"
                if (GetUlong(Nds.Provider.Rom, 0) == 0x204E4F4D454B4F50)
                {
                    // "D   " 
                    if (GetUint(Nds.Provider.Rom, 8) == 0x00000044) version = 1;
                    // "P   " 
                    if (GetUint(Nds.Provider.Rom, 8) == 0x00000050) version = 2;
                    // "PL  " 
                    if (GetUint(Nds.Provider.Rom, 8) == 0x00004C50) version = 3;
                }
            }

            if (version != 0)
            {
                if (ImGui.Begin("Void Tool", ImGuiWindowFlags.AlwaysAutoResize))
                {
                    uint verId = GetUint(Nds.Provider.Rom, 0xC);
                    uint id = verId & 0xFF;
                    uint lang = (verId >> 24) & 0xFF;

                    uint index = 0;
                    if (id == 0x59) index = 0;
                    if (id == 0x41) index = 1;
                    if (id == 0x43) index = 2;
                    if (id == 0x49) index = 3;

                    PokemonGen4Details details;
                    PokemonGen4BasePtrs[index].TryGetValue(lang, out details);
                    if (details != null)
                    {
                        uint basePtr = Nds.Mem9.ReadDebug32(details.BasePtrPtr);
                        bool isPlatinum = version == 3;

                        // constants from void.lua
                        uint mapAddrOffs = isPlatinum ? 0x218FEU : 0x22ADAU;
                        uint coordPtrOffs = isPlatinum ? 0x2371CU : 0x248D4U;
                        uint coordPtr = Nds.Mem9.ReadDebug32(basePtr + coordPtrOffs);
                        uint xPtr = coordPtr + 0x84;
                        uint yPtr = coordPtr + 0x8C;
                        uint zPtr = coordPtr + 0x94;
                        uint xPos = Nds.Mem9.ReadDebug32(xPtr);
                        uint yPos = Nds.Mem9.ReadDebug32(yPtr);
                        uint zPos = Nds.Mem9.ReadDebug32(zPtr);

                        var drawList = ImGui.GetWindowDrawList();

                        ImGui.Columns(2);

                        ImGui.Text($"{PokemonGen4GameNames[version]} ({details.LangCode})");
                        ImGui.Text("Base Pointer Pointer: " + Hex(details.BasePtrPtr, 8));
                        ImGui.Text("Base Pointer: " + Hex(basePtr, 8));
                        ImGui.Text("X: " + xPos);
                        ImGui.Text("Y: " + yPos);
                        ImGui.Text("Z: " + zPos);

                        ImGui.NextColumn();

                        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
                        for (uint i = 0; i < MapColorsNames.Length; i++)
                        {
                            var legendCursorPos = ImGui.GetCursorScreenPos();
                            var rectPos = legendCursorPos + new Vector2(0, 3);
                            drawList.AddRectFilled(rectPos, rectPos + new Vector2(8, 8), MapColors[i]);
                            ImGui.SetCursorScreenPos(legendCursorPos + new Vector2(10, 0));

                            ImGui.Text(MapColorsNames[i]);
                        }
                        ImGui.PopStyleVar();

                        ImGui.Spacing();
                        ImGui.Spacing();

                        ImGui.Columns(1);

                        int cols = 30;
                        int rows = 30;

                        int size = 16;
                        int gap = 1;

                        var cursorPos = ImGui.GetCursorScreenPos();

                        for (int y = 0; y < rows; y++)
                        {
                            for (int x = 0; x < cols; x++)
                            {
                                int xAdj = x;
                                int yAdj = y;
                                uint tile = Nds.Mem9.ReadDebug16((uint)(basePtr + mapAddrOffs + 2 * xAdj + 60 * yAdj));

                                // The game forces all invalid tiles to ID 3, which is Jubilife City in DPPt 
                                if (tile > 558)
                                {
                                    tile = 3;
                                }

                                uint color = 0xFFFFFFFF;
                                if (PokemonGen4MapColors.ContainsKey(tile))
                                {
                                    color = MapColors[PokemonGen4MapColors[tile]];
                                }
                                // drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(size, size), color);

                                ImGui.PushStyleColor(ImGuiCol.Button, color);
                                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorBright(color, 0.75f));
                                ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorBright(color, 0.5f));
                                ImGui.PushID(y * cols + x);
                                ImGui.SetCursorScreenPos(cursorPos);
                                // now use invisible buttons that fill the gap for better UX
                                if (ImGui.InvisibleButton("", new Vector2(size + gap, size + gap)))
                                {
                                    // Teleport to center of tile on click
                                    Nds.Mem9.Write32(xPtr, (uint)(x * 32 + 16));
                                    Nds.Mem9.Write32(yPtr, (uint)(y * 32 + 16));
                                }

                                // Write in Sandgem Town Pokemon Center for lulz
                                // Nds.Mem9.Write16(basePtr + mapAddrOffs, 420);

                                ImGui.SetCursorScreenPos(cursorPos);
                                // use this to draw the button
                                ImGui.Button("", new Vector2(size, size));
                                ImGui.PopID();
                                ImGui.PopStyleColor();
                                ImGui.PopStyleColor();
                                ImGui.PopStyleColor();

                                if (x == xPos / 32 && y == yPos / 32)
                                {
                                    drawList.AddRect(cursorPos, cursorPos + new Vector2(size, size), 0xFF800000, 0, ImDrawCornerFlags.None, 2);
                                }

                                cursorPos += new Vector2(gap + size, 0);
                            }

                            cursorPos += new Vector2(-(gap + size) * cols, gap + size);
                        }
                    }
                    else
                    {
                        ImGui.Text("Generation 4 Pokémon game detected, but no suitable base pointer found.");
                    }

                    ImGui.End();
                }
            }
        }
    }
}