using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.IO;
using ImGuiNET;
using System.Threading;
using ImGuiUtils;
using static Util;
using System.Collections.Generic;
using System.Runtime;
using System.Numerics;
using OptimeGBA;
using Gee.External.Capstone.Arm;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static SDL2.SDL;
using static OptimeGBAEmulator.Window;
using System.Linq;

namespace OptimeGBAEmulator
{
    public unsafe class WindowNds
    {
        GameWindow Window;

        int gbTexId;
        int bgPalTexId;
        int objPalTexId;
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

                int cyclesLeft = 70224 * 4;
                while (cyclesLeft > 0 && !Nds.Nds7.Cpu.Errored)
                {
                    cyclesLeft -= (int)Nds.Step();
                }

                while (!SyncToAudio && !Nds.Nds7.Cpu.Errored && RunEmulator)
                {
                    Nds.Step();
                    ThreadCyclesQueued = 0;
                }
            }
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
            while (cycles > 0 && !Nds.Nds7.Cpu.Errored && RunEmulator)
            {
                cycles -= (int)Nds.Step();
            }
        }

        int CyclesLeft;
        public void RunFrame()
        {
            CyclesLeft += FrameCycles;
            while (CyclesLeft > 0 && !Nds.Nds7.Cpu.Errored)
            {
                CyclesLeft -= (int)Nds.Step();
            }
        }

        public void RunScanline()
        {
            CyclesLeft += ScanlineCycles;
            while (CyclesLeft > 0 && !Nds.Nds7.Cpu.Errored)
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
            Nds = new Nds(new ProviderNds(bios7, bios9, new byte[0], "", AudioReady));

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
            gbTexId = GL.GenTexture();
            bgPalTexId = GL.GenTexture();
            objPalTexId = GL.GenTexture();

            Window.VSync = VSyncMode.Off;
            Window.UpdateFrequency = 59.7275;
        }

        public void LoadRomAndSave(byte[] rom, byte[] sav, string savPath)
        {
            var bios7 = Nds.Provider.Bios7;
            var bios9 = Nds.Provider.Bios9;
            Nds = new Nds(new ProviderNds(bios7, bios9, rom, savPath, AudioReady));
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
            DrawMemoryViewer();
            DrawHwioLog();
            DrawBankedRegisters();
            DrawCpuProfiler();
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
                    "BIOS",
                    "EWRAM",
                    "IWRAM",
                    "VRAM",
                    "ROM",
                };

        static uint[] baseAddrs = {
                0x00000000,
                0x02000000,
                0x03000000,
                0x06000000,
                0x08000000
            };

        public void DrawMemoryViewer()
        {

            int rows = 2048;
            int cols = 16;

            if (ImGui.Begin("Memory Viewer"))
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
            String logText;
            try
            {
                if (LogIndex < Log.Length)
                {
                    logText = Log[LogIndex].Substring(0, 135) + Log[LogIndex].Substring(144, 14) + $" {LogIndex + 1}";
                }
                else
                {
                    logText = "<log past end>";
                }
            }
            catch
            {
                logText = "<log exception>";
            }

            return logText;
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

        public String BuildEmuFullText()
        {
            String disasm = Nds.Nds7.Cpu.ThumbState ? disasmThumb((ushort)Nds.Nds7.Cpu.LastIns) : disasmArm(Nds.Nds7.Cpu.LastIns);

            StringBuilder builder = new StringBuilder();
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[0], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[1], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[2], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[3], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[4], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[5], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[6], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[7], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[8], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[9], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[10], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[11], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[12], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[13], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[14], 8)} ");
            builder.Append($"{HexN(Nds.Nds7.Cpu.R[15], 8)} ");
            builder.Append($"cpsr: {HexN(Nds.Nds7.Cpu.GetCPSR(), 8)} | ");
            builder.Append($"{(Nds.Nds7.Cpu.ThumbState ? "    " + HexN(Nds.Nds7.Cpu.LastIns, 4) : HexN(Nds.Nds7.Cpu.LastIns, 8))}: {disasm}");
            // text += $"> {LogIndex + 1}";
            return builder.ToString();
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
            ImGui.Text($"Instruction: {Hex(arm7.LastIns, arm7.ThumbState ? 4 : 8)}");
            ImGui.Text($"Prev. Ins.: {Hex(arm7.LastLastIns, arm7.ThumbState ? 4 : 8)}");
            ImGui.Text($"Disasm: {(arm7.ThumbState ? disasmThumb((ushort)arm7.LastIns) : disasmArm(arm7.LastIns))}");

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
                ImGui.Columns(4);
                ImGui.Text("ARM9");
                drawCpuInfo(Nds.Nds9.Cpu);
                var Ime9 = Nds.Nds9.HwControl.IME;
                ImGui.Checkbox("IME", ref Ime9);
                ImGui.NextColumn();
                ImGui.Text("ARM7");
                drawCpuInfo(Nds.Nds7.Cpu);
                var Ime7 = Nds.Nds7.HwControl.IME;
                ImGui.Checkbox("IME", ref Ime7);
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                // ImGui.Text($"Ins Next Up: {(Nds.Nds7.Cpu.ThumbState ? Hex(Nds.Nds7.Cpu.THUMBDecode, 4) : Hex(Nds.Nds7.Cpu.ARMDecode, 8))}");

                ImGui.Text($"");

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
                }
                if (ImGui.Button("Step"))
                {
                    Nds.Step();
                    LogIndex++;
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
                    using (StreamWriter file = new StreamWriter("log.txt"))
                    {
                        int num = DebugStepFor;
                        while (num > 0 && !Nds.Nds7.Cpu.Errored)
                        {

                            // file.WriteLine(BuildEmuFullText());
                            Nds.Step();

                            if (Nds.Nds7.Cpu.InstructionsRanInterrupt == Nds.Nds7.Cpu.InstructionsRan)
                            {
                                file.WriteLine("---------------- INTERRUPT ----------------");
                            }

                            LogIndex++;
                            num--;
                        }
                    }
                }

                if (ImGui.Button("Step 250000"))
                {
                    using (StreamWriter file = new StreamWriter("log.txt"))
                    {
                        int num = 250000;
                        while (num > 0 && !Nds.Nds7.Cpu.Errored)
                        {
                            Nds.Step();
                            file.WriteLine(BuildEmuFullText());

                            if (Nds.Nds7.Cpu.InstructionsRanInterrupt == Nds.Nds7.Cpu.InstructionsRan)
                            {
                                file.WriteLine("---------------- INTERRUPT ----------------");
                            }

                            LogIndex++;
                            num--;
                        }
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

                // ImGui.Text($"DMA 0 Src: {Hex(Nds.Dma.Ch[0].DmaSource, 8)}");
                // ImGui.Text($"DMA 1 Src: {Hex(Nds.Dma.Ch[1].DmaSource, 8)}");
                // ImGui.Text($"DMA 2 Src: {Hex(Nds.Dma.Ch[2].DmaSource, 8)}");
                // ImGui.Text($"DMA 3 Src: {Hex(Nds.Dma.Ch[3].DmaSource, 8)}");
                // ImGui.Text("");
                // ImGui.Text($"DMA 0 Dest: {Hex(Nds.Dma.Ch[0].DmaDest, 8)}");
                // ImGui.Text($"DMA 1 Dest: {Hex(Nds.Dma.Ch[1].DmaDest, 8)}");
                // ImGui.Text($"DMA 2 Dest: {Hex(Nds.Dma.Ch[2].DmaDest, 8)}");
                // ImGui.Text($"DMA 3 Dest: {Hex(Nds.Dma.Ch[3].DmaDest, 8)}");
                // ImGui.Text("");
                // ImGui.Text($"DMA 0 Words: {Hex(Nds.Dma.Ch[0].DmaLength, 4)}");
                // ImGui.Text($"DMA 1 Words: {Hex(Nds.Dma.Ch[1].DmaLength, 4)}");
                // ImGui.Text($"DMA 2 Words: {Hex(Nds.Dma.Ch[2].DmaLength, 4)}");
                // ImGui.Text($"DMA 3 Words: {Hex(Nds.Dma.Ch[3].DmaLength, 4)}");

                ImGuiColumnSeparator();

                // ImGui.Text($"Timer 0 Counter: {Hex(Nds.Timers.T[0].CalculateCounter(), 4)}");
                // ImGui.Text($"Timer 1 Counter: {Hex(Nds.Timers.T[1].CalculateCounter(), 4)}");
                // ImGui.Text($"Timer 2 Counter: {Hex(Nds.Timers.T[2].CalculateCounter(), 4)}");
                // ImGui.Text($"Timer 3 Counter: {Hex(Nds.Timers.T[3].CalculateCounter(), 4)}");
                // ImGui.Text("");
                // ImGui.Text($"Timer 0 Reload: {Hex(Nds.Timers.T[0].ReloadVal, 4)}");
                // ImGui.Text($"Timer 1 Reload: {Hex(Nds.Timers.T[1].ReloadVal, 4)}");
                // ImGui.Text($"Timer 2 Reload: {Hex(Nds.Timers.T[2].ReloadVal, 4)}");
                // ImGui.Text($"Timer 3 Reload: {Hex(Nds.Timers.T[3].ReloadVal, 4)}");
                // ImGui.Text("");

                // String[] prescalerCodes = { "F/1", "F/64", "F/256", "F/1024" };

                // ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Nds.Timers.T[0].PrescalerSel]}");
                // ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Nds.Timers.T[1].PrescalerSel]}");
                // ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Nds.Timers.T[2].PrescalerSel]}");
                // ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Nds.Timers.T[3].PrescalerSel]}");

                ImGui.NextColumn();
                // ImGui.Text($"FIFO A Current Bytes: {Nds.NdsAudio.A.Bytes}");
                // ImGui.Text($"FIFO B Current Bytes: {Nds.NdsAudio.B.Bytes}");
                // ImGui.Text($"FIFO A Collisions: {Nds.NdsAudio.A.Collisions}");
                // ImGui.Text($"FIFO B Collisions: {Nds.NdsAudio.B.Collisions}");
                // ImGui.Text($"FIFO A Total Pops: {Nds.NdsAudio.A.TotalPops}");
                // ImGui.Text($"FIFO B Total Pops: {Nds.NdsAudio.B.TotalPops}");
                // ImGui.Text($"FIFO A Empty Pops: {Nds.NdsAudio.A.EmptyPops}");
                // ImGui.Text($"FIFO B Empty Pops: {Nds.NdsAudio.B.EmptyPops}");
                // ImGui.Text($"FIFO A Full Inserts: {Nds.NdsAudio.A.FullInserts}");
                // ImGui.Text($"FIFO B Full Inserts: {Nds.NdsAudio.B.FullInserts}");
                // ImGui.Text("");
                // ImGui.Text($"PSG A Output Value: {Nds.NdsAudio.Ndsudio.Out1}");
                // ImGui.Text($"PSG B Output Value: {Nds.NdsAudio.Ndsudio.Out2}");
                // ImGui.Text("");
                // ImGui.Text($"Left Master Volume: {Nds.NdsAudio.Ndsudio.leftMasterVol}");
                // ImGui.Text($"Right Master Volume: {Nds.NdsAudio.Ndsudio.rightMasterVol}");
                // ImGui.Text("");
                // ImGui.Text($"Pulse 1 Current Value: {Nds.NdsAudio.Ndsudio.pulse1Val}");
                // ImGui.Text($"Pulse 2 Current Value: {Nds.NdsAudio.Ndsudio.pulse2Val}");
                // ImGui.Text($"Wave Current Value: {Nds.NdsAudio.Ndsudio.waveVal}");
                // ImGui.Text($"Noise Current Value: {Nds.NdsAudio.Ndsudio.noiseVal}");
                // ImGui.Text("");
                // ImGui.Text($"Pulse 1 Enabled: {Nds.NdsAudio.Ndsudio.pulse1_enabled}");
                // ImGui.Text($"Pulse 1 Width: {Nds.NdsAudio.Ndsudio.pulse1_width}");
                // ImGui.Text($"Pulse 1 DAC Enabled: {Nds.NdsAudio.Ndsudio.pulse1_dacEnabled}");
                // ImGui.Text($"Pulse 1 Length Enable: {Nds.NdsAudio.Ndsudio.pulse1_lengthEnable}");
                // ImGui.Text($"Pulse 1 Length Counter: {Nds.NdsAudio.Ndsudio.pulse1_lengthCounter}");
                // ImGui.Text($"Pulse 1 Frequency Upper: {Nds.NdsAudio.Ndsudio.pulse1_frequencyUpper}");
                // ImGui.Text($"Pulse 1 Frequency Lower: {Nds.NdsAudio.Ndsudio.pulse1_frequencyLower}");
                // ImGui.Text($"Pulse 1 Volume: {Nds.NdsAudio.Ndsudio.pulse1_volume}");
                // ImGui.Text($"Pulse 1 Volume Envelope Up: {Nds.NdsAudio.Ndsudio.pulse1_volumeEnvelopeUp}");
                // ImGui.Text($"Pulse 1 Volume Envelope Sweep: {Nds.NdsAudio.Ndsudio.pulse1_volumeEnvelopeSweep}");
                // ImGui.Text($"Pulse 1 Volume Envelope Start: {Nds.NdsAudio.Ndsudio.pulse1_volumeEnvelopeStart}");
                // ImGui.Text($"Pulse 1 Output Left: {Nds.NdsAudio.Ndsudio.pulse1_outputLeft}");
                // ImGui.Text($"Pulse 1 Output Right: {Nds.NdsAudio.Ndsudio.pulse1_outputRight}");
                // ImGui.Text($"Pulse 1 Freq Sweep Period: {Nds.NdsAudio.Ndsudio.pulse1_freqSweepPeriod}");
                // ImGui.Text($"Pulse 1 Freq Sweep Up: {Nds.NdsAudio.Ndsudio.pulse1_freqSweepUp}");
                // ImGui.Text($"Pulse 1 Freq Sweep Shift: {Nds.NdsAudio.Ndsudio.pulse1_freqSweepShift}");
                // ImGui.Text($"Pulse 1 Updated: {Nds.NdsAudio.Ndsudio.pulse1_updated}");
                // ImGui.Text("");
                // ImGui.Text($"Wave Bank: {Nds.NdsAudio.Ndsudio.wave_bank}");
                // ImGui.Text($"Wave Dimension: {Nds.NdsAudio.Ndsudio.wave_dimension}");
                // ImGui.Text($"Wave Enabled: {Nds.NdsAudio.Ndsudio.wave_enabled}");
                // ImGui.Text($"Wave DAC Enabled: {Nds.NdsAudio.Ndsudio.wave_dacEnabled}");
                // ImGui.Text($"Wave Length Enable: {Nds.NdsAudio.Ndsudio.wave_lengthEnable}");
                // ImGui.Text($"Wave Length Counter: {Nds.NdsAudio.Ndsudio.wave_lengthCounter}");
                // ImGui.Text($"Wave Frequency Upper: {Nds.NdsAudio.Ndsudio.wave_frequencyUpper}");
                // ImGui.Text($"Wave Frequency Lower: {Nds.NdsAudio.Ndsudio.wave_frequencyLower}");
                // ImGui.Text($"Wave Volume: {Nds.NdsAudio.Ndsudio.wave_volume}");
                // ImGui.Text($"Wavetable 0: {string.Join(" ", Nds.NdsAudio.Ndsudio.wave_waveTable0)}");
                // ImGui.Text($"Wavetable 1: {string.Join(" ", Nds.NdsAudio.Ndsudio.wave_waveTable1)}");

                // ImGui.Text($"Buffer Samples: {Nds.NdsAudio.SampleBuffer.Entries / 2}");
                // ImGui.Checkbox("Enable PSGs", ref Nds.NdsAudio.EnablePsg);
                // ImGui.Checkbox("Enable FIFOs", ref Nds.NdsAudio.EnableFifo);

                // ImGui.Text($"PSG Factor: {Nds.NdsAudio.Ndsudio.PsgFactor}");
                // if (ImGui.Button("-##psg"))
                // {
                //     if (Nds.NdsAudio.Ndsudio.PsgFactor > 0)
                //     {
                //         Nds.NdsAudio.Ndsudio.PsgFactor--;
                //     }
                // }
                // ImGui.SameLine();
                // if (ImGui.Button("+##psg"))
                // {
                //     Nds.NdsAudio.Ndsudio.PsgFactor++;
                // }

                // ImGui.Text($"BG0 Size X/Y: {Ppu.CharWidthTable[Nds.Ppu.Backgrounds[0].ScreenSize]}/{Ppu.CharHeightTable[Nds.Ppu.Backgrounds[0].ScreenSize]}");
                // ImGui.Text($"BG0 Scroll X: {Nds.Ppu.Backgrounds[0].HorizontalOffset}");
                // ImGui.Text($"BG0 Scroll Y: {Nds.Ppu.Backgrounds[0].VerticalOffset}");
                // ImGui.Text($"BG1 Size X/Y: {Ppu.CharWidthTable[Nds.Ppu.Backgrounds[1].ScreenSize]}/{Ppu.CharHeightTable[Nds.Ppu.Backgrounds[1].ScreenSize]}");
                // ImGui.Text($"BG1 Scroll X: {Nds.Ppu.Backgrounds[1].HorizontalOffset}");
                // ImGui.Text($"BG1 Scroll Y: {Nds.Ppu.Backgrounds[1].VerticalOffset}");
                // ImGui.Text($"BG2 Size X/Y: {Ppu.CharWidthTable[Nds.Ppu.Backgrounds[2].ScreenSize]}/{Ppu.CharHeightTable[Nds.Ppu.Backgrounds[2].ScreenSize]}");
                // ImGui.Text($"BG2 Affine Size: {Ppu.AffineSizeTable[Nds.Ppu.Backgrounds[2].ScreenSize]}/{Ppu.AffineSizeTable[Nds.Ppu.Backgrounds[2].ScreenSize]}");
                // ImGui.Text($"BG2 Scroll X: {Nds.Ppu.Backgrounds[2].HorizontalOffset}");
                // ImGui.Text($"BG2 Scroll Y: {Nds.Ppu.Backgrounds[2].VerticalOffset}");
                // ImGui.Text($"BG3 Size X/Y: {Ppu.CharWidthTable[Nds.Ppu.Backgrounds[3].ScreenSize]}/{Ppu.CharHeightTable[Nds.Ppu.Backgrounds[3].ScreenSize]}");
                // ImGui.Text($"BG3 Affine Size: {Ppu.AffineSizeTable[Nds.Ppu.Backgrounds[3].ScreenSize]}/{Ppu.AffineSizeTable[Nds.Ppu.Backgrounds[3].ScreenSize]}");
                // ImGui.Text($"BG3 Scroll X: {Nds.Ppu.Backgrounds[3].HorizontalOffset}");
                // ImGui.Text($"BG3 Scroll Y: {Nds.Ppu.Backgrounds[3].VerticalOffset}");
                // ImGui.Checkbox("Debug BG0", ref Nds.Ppu.DebugEnableBg[0]);
                // ImGui.Checkbox("Debug BG1", ref Nds.Ppu.DebugEnableBg[1]);
                // ImGui.Checkbox("Debug BG2", ref Nds.Ppu.DebugEnableBg[2]);
                // ImGui.Checkbox("Debug BG3", ref Nds.Ppu.DebugEnableBg[3]);
                // ImGui.Checkbox("Debug OBJ", ref Nds.Ppu.DebugEnableObj);

                // ImGui.Text($"Window 0 Left..: {Nds.Ppu.Win0HLeft}");
                // ImGui.Text($"Window 0 Right.: {Nds.Ppu.Win0HRight}");
                // ImGui.Text($"Window 0 Top...: {Nds.Ppu.Win0VTop}");
                // ImGui.Text($"Window 0 Bottom: {Nds.Ppu.Win0VBottom}");
                // ImGui.Text($"Window 1 Left..: {Nds.Ppu.Win1HLeft}");
                // ImGui.Text($"Window 1 Right.: {Nds.Ppu.Win1HRight}");
                // ImGui.Text($"Window 1 Top...: {Nds.Ppu.Win1VTop}");
                // ImGui.Text($"Window 1 Bottom: {Nds.Ppu.Win1VBottom}");

                ImGui.Columns(1);
                ImGui.Separator();

                ImGui.Text("Palettes");

                for (int i = 0; i < 4; i++)
                {
                    int paletteBase = i * 256;
                    for (int p = 0; p < 256; p++)
                    {
                        PaletteImageBuffer[p] = Nds.Ppu.Renderer.ProcessedPalettes[paletteBase + p];
                    }

                    GL.BindTexture(TextureTarget.Texture2D, bgPalTexId);

                    // TexParameter needed for something to display :)
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);

                    GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
                    GL.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        PixelInternalFormat.Rgb,
                        16,
                        16,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
                        PaletteImageBuffer
                    );

                    // ImGui.Text($"Pointer: {texId}");
                    ImGui.Image((IntPtr)bgPalTexId, new System.Numerics.Vector2(16 * 8, 16 * 8)); ImGui.SameLine();
                }

                ImGui.End();
            }
        }

        public void ImGuiColumnSeparator()
        {
            ImGui.Dummy(new System.Numerics.Vector2(0.0f, 0.5f));

            // Draw separator within column
            ImDrawListPtr drawList = ImGui.GetWindowDrawList();
            System.Numerics.Vector2 pos = ImGui.GetCursorScreenPos();
            drawList.AddLine(new System.Numerics.Vector2(pos.X - 9999, pos.Y), new System.Numerics.Vector2(pos.X + 9999, pos.Y), ImGui.GetColorU32(ImGuiCol.Border));

            ImGui.Dummy(new System.Numerics.Vector2(0.0f, 1f));
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

        public bool BigScreen = false;
        public bool ShowBackBuf = false;
        public unsafe void DrawDisplay()
        {
            if (ImGui.Begin("Display", ImGuiWindowFlags.NoResize))
            {
                gbTexId = 0;

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, gbTexId);
                if (!ShowBackBuf)
                {
                    GL.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        PixelInternalFormat.Rgba,
                        256,
                        192,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
#if UNSAFE
                        (IntPtr)Nds.Ppu.Renderer.ScreenFront
#else
                        Nds.Ppu.Renderer.ScreenFront
#endif
                    );

                }
                else
                {
                    GL.TexImage2D(
                        TextureTarget.Texture2D,
                        0,
                        PixelInternalFormat.Rgba,
                        256,
                        192,
                        0,
                        PixelFormat.Rgba,
                        PixelType.UnsignedByte,
#if UNSAFE
                        (IntPtr)Nds.Ppu.Renderer.ScreenFront
#else   
                        Nds.Ppu.Renderer.ScreenFront
#endif
                    );
                }

                float height = BigScreen ? 256 * 5 : 256 * 2;
                float width = BigScreen ? 192 * 5 : 192 * 2;

                ImGui.Image((IntPtr)gbTexId, new System.Numerics.Vector2(height, width));
                ImGui.SetWindowSize(new System.Numerics.Vector2(height + 16, width + 36));
                ImGui.End();
            }
        }

        public List<Register> Registers = new List<Register>();
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
            Registers.Add(
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

            Registers.Add(
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
                Registers.Add(
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

            Registers.Add(
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

            Registers.Add(
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

            Registers.Add(
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

            Registers.Add(
                new Register($"BLDALPHA - Blending Coefficients", 0x4000052,
                    new RegisterField("EVA Coefficient", 0, 4),
                    new RegisterField("EVB Coefficient", 8, 12)
                ));

            Registers.Add(
                new Register($"BLDY - Blending Brightness", 0x4000054,
                    new RegisterField("EVY Coefficient", 0, 4)
                ));

            Registers.Add(
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
                Registers.Add(
                    new Register($"DMA{r}CNT_H - DMA {r} Control", dmaAddrs[r],
                        new RegisterField("Dest Addr Control", 5, 6),
                        new RegisterField("Source Addr Control", 7, 8),
                        new RegisterField("DMA Repeat", 9),
                        new RegisterField("DMA 32-bit Mode", 10),
                        new RegisterField("Game Pak DRQ", 11),
                        new RegisterField("DMA Start Timing", 12, 13),
                        new RegisterField("IRQ on Word Count Drain", 14),
                        new RegisterField("DMA Enable", 15)
                ));
            }

            uint[] timerAddrs = { 0x4000102, 0x4000106, 0x400010A, 0x400010E };
            for (uint r = 0; r < 4; r++)
            {
                Registers.Add(
                    new Register($"TM{r}CNT_L - Timer {r} Control", timerAddrs[r],
                        new RegisterField("Prescaler Selection", 0, 1),
                        new RegisterField("Timer Cascade", 2),
                        new RegisterField("Timer IRQ Enable", 6),
                        new RegisterField("Timer Start / Stop", 7)
                ));
            }

            Registers.Add(
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

            uint[] ieIfAddrs = { 0x4000200, 0x4000202 };
            String[] ieIfStrings = { "IE - Interrupt Enable", "IF - Interrupt Request" };
            for (uint r = 0; r < 2; r++)
            {
                Registers.Add(
                    new Register(ieIfStrings[r], ieIfAddrs[r],
                        new RegisterField("PPU V-Blank", 0),
                        new RegisterField("PPU H-Blank", 1),
                        new RegisterField("PPU V-Counter Match", 2),
                        new RegisterField("PPU Timer 0 Overflow", 3),
                        new RegisterField("PPU Timer 1 Overflow", 4),
                        new RegisterField("PPU Timer 2 Overflow", 5),
                        new RegisterField("PPU Timer 3 Overflow", 6),
                        new RegisterField("Serial", 7),
                        new RegisterField("DMA 0", 8),
                        new RegisterField("DMA 1", 9),
                        new RegisterField("DMA 2", 10),
                        new RegisterField("DMA 3", 11),
                        new RegisterField("Keypad", 13),
                        new RegisterField("Game Pak", 014
                )));
            }

            RegViewerSelected = Registers[0];
        }

        Register RegViewerSelected;

        public void DrawRegViewer()
        {
            if (ImGui.Begin("Register Viewer"))
            {
                if (ImGui.BeginCombo("", $"{Hex(RegViewerSelected.Address, 8)} {RegViewerSelected.Name}"))
                {
                    foreach (Register r in Registers)
                    {
                        bool selected = r == RegViewerSelected;
                        if (ImGui.Selectable($"{Hex(r.Address, 8)} {r.Name}", selected))
                        {
                            RegViewerSelected = r;
                        }
                        if (selected)
                        {
                            ImGui.SetItemDefaultFocus();
                        }
                    }
                    ImGui.EndCombo();
                }

                uint value = Nds.Nds9.Mem.ReadDebug32(RegViewerSelected.Address);
                foreach (RegisterField f in RegViewerSelected.Fields)
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
                ImGui.Columns(2);

                ImGui.Text("ARM9");
                foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioReadLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                }
                ImGui.Text("");
                foreach (KeyValuePair<uint, uint> entry in Nds.Nds9.Mem.HwioWriteLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                }

                ImGui.NextColumn();

                ImGui.Text("ARM7");
                foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioReadLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                }
                ImGui.Text("");
                foreach (KeyValuePair<uint, uint> entry in Nds.Nds7.Mem.HwioWriteLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
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

    }
}