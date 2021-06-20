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
using System.Linq;
using static OptimeGBAEmulator.Window;

namespace OptimeGBAEmulator
{
    public unsafe class WindowGba
    {
        public GameWindow Window;
        int gbTexId;
        int bgPalTexId;
        int objPalTexId;

        string[] Log;
        int LogIndex = -0;

        Gba Gba;
        Thread EmulationThread;
        AutoResetEvent ThreadSync = new AutoResetEvent(false);

        static bool SyncToAudio = true;

        const uint AUDIO_SAMPLE_THRESHOLD = 1024;
        const uint AUDIO_SAMPLE_FULL_THRESHOLD = 1024;
        const int SAMPLES_PER_CALLBACK = 32;

        static SDL_AudioSpec want, have;
        static uint AudioDevice;

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
                while (cyclesLeft > 0 && !Gba.Cpu.Errored)
                {
                    cyclesLeft -= (int)Gba.Step();
                }

                while (!SyncToAudio && !Gba.Cpu.Errored && RunEmulator)
                {
                    Gba.Step();
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
                // if (Gba.GbaAudio.SampleBuffer.Entries / 2 < 4096)
                // {
                //     ThreadCyclesQueued += (int)(CyclesPerSample * SAMPLES_PER_CALLBACK * 4);
                // }

                // ThreadCyclesQueued += (int)(CyclesPerSample * SAMPLES_PER_CALLBACK * 4);
                // ThreadSync.Set();

                for (uint i = 0; i < SAMPLES_PER_CALLBACK * 2; i++)
                {
                    AudioArray[i] = Gba.GbaAudio.SampleBuffer.Pop();
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
            while (cycles > 0 && !Gba.Cpu.Errored && RunEmulator)
            {
                cycles -= (int)Gba.Step();
            }
        }

        int CyclesLeft;
        public void RunFrame()
        {
            CyclesLeft += FrameCycles;
            while (CyclesLeft > 0 && !Gba.Cpu.Errored)
            {
                CyclesLeft -= (int)Gba.Step();
            }
        }

        public void RunScanline()
        {
            CyclesLeft += ScanlineCycles;
            while (CyclesLeft > 0 && !Gba.Cpu.Errored)
            {
                CyclesLeft -= (int)Gba.Step();
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

        public WindowGba(GameWindow window)
        {
            Window = window;

            byte[] bios = System.IO.File.ReadAllBytes("gba_bios.bin");
            Gba = new Gba(new ProviderGba(bios, new byte[0], "", AudioReady) { BootBios = true });

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
            var bios = Gba.Provider.Bios;
            Gba = new Gba(new ProviderGba(bios, rom, savPath, AudioReady) { BootBios = true });
            Gba.Mem.SaveProvider.LoadSave(sav);
        }

        public double Time;
        public bool RecordTime;
        public uint RecordStartFrames;

        public void OnUpdateFrame(FrameEventArgs e)
        {
            Gba.Keypad.B = Window.KeyboardState.IsKeyDown(Keys.Z);
            Gba.Keypad.A = Window.KeyboardState.IsKeyDown(Keys.X);
            Gba.Keypad.Left = Window.KeyboardState.IsKeyDown(Keys.Left);
            Gba.Keypad.Up = Window.KeyboardState.IsKeyDown(Keys.Up);
            Gba.Keypad.Right = Window.KeyboardState.IsKeyDown(Keys.Right);
            Gba.Keypad.Down = Window.KeyboardState.IsKeyDown(Keys.Down);
            Gba.Keypad.Start = Window.KeyboardState.IsKeyDown(Keys.Enter) || Window.KeyboardState.IsKeyDown(Keys.KeyPadEnter);
            Gba.Keypad.Select = Window.KeyboardState.IsKeyDown(Keys.Backspace);
            Gba.Keypad.L = Window.KeyboardState.IsKeyDown(Keys.Q);
            Gba.Keypad.R = Window.KeyboardState.IsKeyDown(Keys.E);

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

            if (Gba.Mem.SaveProvider.Dirty)
            {
                DumpSav();
            }
        }

        const int FrameCycles = 70224 * 4;
        const int ScanlineCycles = 1232;

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
            DrawSoundVisualizer();
            DrawCpuProfiler();
        }

        public void ResetGba()
        {
            byte[] save = Gba.Mem.SaveProvider.GetSave();
            ProviderGba p = Gba.Provider;
            Gba = new Gba(p);
            Gba.Mem.SaveProvider.LoadSave(save);
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
                ImGui.Text("R13: " + Hex(Gba.Cpu.R13usr, 8));
                ImGui.Text("R14: " + Hex(Gba.Cpu.R14usr, 8));

                ImGui.NextColumn();

                ImGui.Text("Supervisor");
                ImGui.Text("R13: " + Hex(Gba.Cpu.R13svc, 8));
                ImGui.Text("R14: " + Hex(Gba.Cpu.R14svc, 8));

                ImGui.NextColumn();

                ImGui.Text("Abort");
                ImGui.Text("R13: " + Hex(Gba.Cpu.R13abt, 8));
                ImGui.Text("R14: " + Hex(Gba.Cpu.R14abt, 8));

                ImGui.NextColumn();

                ImGui.Text("IRQ");
                ImGui.Text("R13: " + Hex(Gba.Cpu.R13irq, 8));
                ImGui.Text("R14: " + Hex(Gba.Cpu.R14irq, 8));

                ImGui.NextColumn();

                ImGui.Text("Undefined");
                ImGui.Text("R13: " + Hex(Gba.Cpu.R13und, 8));
                ImGui.Text("R14: " + Hex(Gba.Cpu.R14und, 8));

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
                        uint val = Gba.Mem.Read8(tempBase);

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
            text += $"{HexN(Gba.Cpu.R[0], 8)} ";
            text += $"{HexN(Gba.Cpu.R[1], 8)} ";
            text += $"{HexN(Gba.Cpu.R[2], 8)} ";
            text += $"{HexN(Gba.Cpu.R[3], 8)} ";
            text += $"{HexN(Gba.Cpu.R[4], 8)} ";
            text += $"{HexN(Gba.Cpu.R[5], 8)} ";
            text += $"{HexN(Gba.Cpu.R[6], 8)} ";
            text += $"{HexN(Gba.Cpu.R[7], 8)} ";
            text += $"{HexN(Gba.Cpu.R[8], 8)} ";
            text += $"{HexN(Gba.Cpu.R[9], 8)} ";
            text += $"{HexN(Gba.Cpu.R[10], 8)} ";
            text += $"{HexN(Gba.Cpu.R[11], 8)} ";
            text += $"{HexN(Gba.Cpu.R[12], 8)} ";
            text += $"{HexN(Gba.Cpu.R[13], 8)} ";
            text += $"{HexN(Gba.Cpu.R[14], 8)} ";
            text += $"{HexN(Gba.Cpu.R[15], 8)} ";
            text += $"cpsr: {HexN(Gba.Cpu.GetCPSR(), 8)} ";
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
                ImGui.Text(Gba.Cpu.Debug);
                ImGui.End();
            }
        }

        uint[] PaletteImageBuffer = new uint[16 * 16];

        public void DrawDebug()
        {
            if (ImGui.Begin("Debug"))
            {
                ImGui.Columns(4);

                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);
                ImGui.Text($"R0:  {Hex(Gba.Cpu.R[0], 8)}");
                ImGui.Text($"R1:  {Hex(Gba.Cpu.R[1], 8)}");
                ImGui.Text($"R2:  {Hex(Gba.Cpu.R[2], 8)}");
                ImGui.Text($"R3:  {Hex(Gba.Cpu.R[3], 8)}");
                ImGui.Text($"R4:  {Hex(Gba.Cpu.R[4], 8)}");
                ImGui.Text($"R5:  {Hex(Gba.Cpu.R[5], 8)}");
                ImGui.Text($"R6:  {Hex(Gba.Cpu.R[6], 8)}");
                ImGui.Text($"R7:  {Hex(Gba.Cpu.R[7], 8)}");
                ImGui.Text($"R8:  {Hex(Gba.Cpu.R[8], 8)}");
                ImGui.Text($"R9:  {Hex(Gba.Cpu.R[9], 8)}");
                ImGui.Text($"R10: {Hex(Gba.Cpu.R[10], 8)}");
                ImGui.Text($"R11: {Hex(Gba.Cpu.R[11], 8)}");
                ImGui.Text($"R12: {Hex(Gba.Cpu.R[12], 8)}");
                ImGui.Text($"R13: {Hex(Gba.Cpu.R[13], 8)}");
                ImGui.Text($"R14: {Hex(Gba.Cpu.R[14], 8)}");
                ImGui.Text($"R15: {Hex(Gba.Cpu.R[15], 8)}");
                ImGui.Text($"CPSR: {Hex(Gba.Cpu.GetCPSR(), 8)}");
                ImGui.Text($"Instruction: {Hex(Gba.Cpu.LastIns, Gba.Cpu.ThumbState ? 4 : 8)}");
                ImGui.Text($"Prev. Ins.: {Hex(Gba.Cpu.LastLastIns, Gba.Cpu.ThumbState ? 4 : 8)}");
                ImGui.Text($"Disasm: {(Gba.Cpu.ThumbState ? disasmThumb((ushort)Gba.Cpu.LastIns) : disasmArm(Gba.Cpu.LastIns))}");
                ImGui.Text($"Decoded as: {(Gba.Cpu.LastThumbState ? Gba.Cpu.GetInstructionThumb((ushort)Gba.Cpu.LastIns).Method.Name : Gba.Cpu.GetInstructionArm(Gba.Cpu.LastIns).Method.Name)}");

                ImGui.Text($"Mode: {Gba.Cpu.Mode}");
                ImGui.Text($"Last Cycles: {Gba.Cpu.InstructionCycles}");
                ImGui.Text($"Total Instrs.: {Gba.Cpu.InstructionsRan}");

                // ImGui.Text($"Ins Next Up: {(Gba.Cpu.ThumbState ? Hex(Gba.Cpu.THUMBDecode, 4) : Hex(Gba.Cpu.ARMDecode, 8))}");

                ImGui.Text($"");

                if (ImGui.Button("Reset"))
                {
                    ResetGba();
                }

                if (ImGui.Button("Frame Advance"))
                {
                    RunFrame();
                }

                if (ImGui.Button("Scanline Advance"))
                {
                    RunScanline();
                }


                if (ImGui.Button("Start Time"))
                {
                    RecordTime = true;
                    Time = 0;
                    RecordStartFrames = Gba.Ppu.Renderer.TotalFrames;
                }

                if (ImGui.Button("Stop Time"))
                {
                    RecordTime = false;
                }

                if (ImGui.Button("Un-error"))
                {
                    Gba.Cpu.Errored = false;
                }
                if (ImGui.Button("Step"))
                {
                    Gba.Step();
                    LogIndex++;
                }
                // if (ImGui.Button("Step Until Error"))
                // {
                //     bool exit = false;
                //     while (!Gba.Cpu.Errored && !exit)
                //     {

                //         Gba.Step();
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
                    while (num > 0 && !Gba.Cpu.Errored)
                    {

                        // file.WriteLine(BuildEmuFullText());
                        Gba.Step();

                        LogIndex++;
                        num--;
                    }
                }

                if (ImGui.Button("Step 250000"))
                {
                    using (StreamWriter file = new StreamWriter("log.txt"))
                    {
                        int num = 250000;
                        while (num > 0 && !Gba.Cpu.Errored)
                        {
                            Gba.Step();
                            file.WriteLine(BuildEmuFullText(Gba.Cpu));

                            if (Gba.Cpu.InterruptServiced)
                            {
                                Gba.Cpu.InterruptServiced = false;
                                file.WriteLine("---------------- INTERRUPT ----------------");
                            }

                            LogIndex++;
                            num--;
                        }
                    }
                }


                ImGui.Checkbox("Run Emulator", ref RunEmulator);
                // ImGui.Checkbox("Log HWIO Access", ref Gba.Mem.LogHWIOAccess);

                ImGui.NextColumn();
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 150);

                displayCheckbox("Negative", Gba.Cpu.Negative);
                displayCheckbox("Zero", Gba.Cpu.Zero);
                displayCheckbox("Carry", Gba.Cpu.Carry);
                displayCheckbox("Overflow", Gba.Cpu.Overflow);
                displayCheckbox("Sticky", Gba.Cpu.Sticky);
                displayCheckbox("IRQ Disable", Gba.Cpu.IRQDisable);
                displayCheckbox("FIQ Disable", Gba.Cpu.FIQDisable);
                displayCheckbox("Thumb State", Gba.Cpu.ThumbState);

                // ImGui.Text($"BIOS Reads: {Gba.Mem.BiosReads}");
                // ImGui.Text($"EWRAM Reads: {Gba.Mem.EwramReads}");
                // ImGui.Text($"IWRAM Reads: {Gba.Mem.IwramReads}");
                // ImGui.Text($"ROM Reads: {Gba.Mem.RomReads}");
                // ImGui.Text($"HWIO Reads: {Gba.Mem.HwioReads}");
                // ImGui.Text($"Palette Reads: {Gba.Mem.PaletteReads}");
                // ImGui.Text($"VRAM Reads: {Gba.Mem.VramReads}");
                // ImGui.Text($"OAM Reads: {Gba.Mem.OamReads}");
                ImGui.Text("");
                // ImGui.Text($"EWRAM Writes: {Gba.Mem.EwramWrites}");
                // ImGui.Text($"IWRAM Writes: {Gba.Mem.IwramWrites}");
                // ImGui.Text($"HWIO Writes: {Gba.Mem.HwioWrites}");
                // ImGui.Text($"Palette Writes: {Gba.Mem.PaletteWrites}");
                // ImGui.Text($"VRAM Writes: {Gba.Mem.VramWrites}");
                // ImGui.Text($"OAM Writes: {Gba.Mem.OamWrites}");
                ImGui.Text("");
                bool ticked = Gba.HwControl.IME;
                ImGui.Checkbox("IME", ref ticked);

                ImGui.Checkbox("Log HWIO", ref Gba.Mem.LogHwioAccesses);
                ImGui.Checkbox("Boot BIOS", ref Gba.Provider.BootBios);
                ImGui.Checkbox("Big Screen", ref BigScreen);
                ImGui.Checkbox("Back Buffer", ref ShowBackBuf);

                ImGui.NextColumn();

                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                ImGui.Text($"Total Frames: {Gba.Ppu.Renderer.TotalFrames}");
                if (RecordTime)
                {
                    ImGui.Text($"Timed Frames: {Gba.Ppu.Renderer.TotalFrames - RecordStartFrames}");
                    ImGui.Text($"Timed Seconds: {Time}");
                    ImGui.Text($"Timed FPS: {(uint)(Gba.Ppu.Renderer.TotalFrames - RecordStartFrames) / Time}");
                }

                ImGui.Text($"VCOUNT: {Gba.Ppu.VCount}");
                ImGui.Text($"Scanline Cycles: {Gba.Ppu.GetScanlineCycles()}");

                ImGuiColumnSeparator();

                ImGui.Text($"DMA 0 Src: {Hex(Gba.Dma.Ch[0].DmaSource, 8)}");
                ImGui.Text($"DMA 1 Src: {Hex(Gba.Dma.Ch[1].DmaSource, 8)}");
                ImGui.Text($"DMA 2 Src: {Hex(Gba.Dma.Ch[2].DmaSource, 8)}");
                ImGui.Text($"DMA 3 Src: {Hex(Gba.Dma.Ch[3].DmaSource, 8)}");
                ImGui.Text("");
                ImGui.Text($"DMA 0 Dest: {Hex(Gba.Dma.Ch[0].DmaDest, 8)}");
                ImGui.Text($"DMA 1 Dest: {Hex(Gba.Dma.Ch[1].DmaDest, 8)}");
                ImGui.Text($"DMA 2 Dest: {Hex(Gba.Dma.Ch[2].DmaDest, 8)}");
                ImGui.Text($"DMA 3 Dest: {Hex(Gba.Dma.Ch[3].DmaDest, 8)}");
                ImGui.Text("");
                ImGui.Text($"DMA 0 Words: {Hex(Gba.Dma.Ch[0].DmaLength, 4)}");
                ImGui.Text($"DMA 1 Words: {Hex(Gba.Dma.Ch[1].DmaLength, 4)}");
                ImGui.Text($"DMA 2 Words: {Hex(Gba.Dma.Ch[2].DmaLength, 4)}");
                ImGui.Text($"DMA 3 Words: {Hex(Gba.Dma.Ch[3].DmaLength, 4)}");

                ImGuiColumnSeparator();

                ImGui.Text($"Timer 0 Counter: {Hex(Gba.Timers.T[0].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Gba.Timers.T[1].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Gba.Timers.T[2].CalculateCounter(), 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Gba.Timers.T[3].CalculateCounter(), 4)}");
                ImGui.Text("");
                ImGui.Text($"Timer 0 Reload: {Hex(Gba.Timers.T[0].ReloadVal, 4)}");
                ImGui.Text($"Timer 1 Reload: {Hex(Gba.Timers.T[1].ReloadVal, 4)}");
                ImGui.Text($"Timer 2 Reload: {Hex(Gba.Timers.T[2].ReloadVal, 4)}");
                ImGui.Text($"Timer 3 Reload: {Hex(Gba.Timers.T[3].ReloadVal, 4)}");
                ImGui.Text("");

                String[] prescalerCodes = { "F/1", "F/64", "F/256", "F/1024" };

                ImGui.Text($"Timer 0 Prescaler: {prescalerCodes[Gba.Timers.T[0].PrescalerSel]}");
                ImGui.Text($"Timer 1 Prescaler: {prescalerCodes[Gba.Timers.T[1].PrescalerSel]}");
                ImGui.Text($"Timer 2 Prescaler: {prescalerCodes[Gba.Timers.T[2].PrescalerSel]}");
                ImGui.Text($"Timer 3 Prescaler: {prescalerCodes[Gba.Timers.T[3].PrescalerSel]}");

                ImGui.NextColumn();
                // ImGui.Text($"FIFO A Current Bytes: {Gba.GbaAudio.A.Bytes}");
                // ImGui.Text($"FIFO B Current Bytes: {Gba.GbaAudio.B.Bytes}");
                // ImGui.Text($"FIFO A Collisions: {Gba.GbaAudio.A.Collisions}");
                // ImGui.Text($"FIFO B Collisions: {Gba.GbaAudio.B.Collisions}");
                // ImGui.Text($"FIFO A Total Pops: {Gba.GbaAudio.A.TotalPops}");
                // ImGui.Text($"FIFO B Total Pops: {Gba.GbaAudio.B.TotalPops}");
                // ImGui.Text($"FIFO A Empty Pops: {Gba.GbaAudio.A.EmptyPops}");
                // ImGui.Text($"FIFO B Empty Pops: {Gba.GbaAudio.B.EmptyPops}");
                // ImGui.Text($"FIFO A Full Inserts: {Gba.GbaAudio.A.FullInserts}");
                // ImGui.Text($"FIFO B Full Inserts: {Gba.GbaAudio.B.FullInserts}");
                // ImGui.Text("");
                // ImGui.Text($"PSG A Output Value: {Gba.GbaAudio.GbAudio.Out1}");
                // ImGui.Text($"PSG B Output Value: {Gba.GbaAudio.GbAudio.Out2}");
                // ImGui.Text("");
                // ImGui.Text($"Left Master Volume: {Gba.GbaAudio.GbAudio.leftMasterVol}");
                // ImGui.Text($"Right Master Volume: {Gba.GbaAudio.GbAudio.rightMasterVol}");
                // ImGui.Text("");
                // ImGui.Text($"Pulse 1 Current Value: {Gba.GbaAudio.GbAudio.pulse1Val}");
                // ImGui.Text($"Pulse 2 Current Value: {Gba.GbaAudio.GbAudio.pulse2Val}");
                // ImGui.Text($"Wave Current Value: {Gba.GbaAudio.GbAudio.waveVal}");
                // ImGui.Text($"Noise Current Value: {Gba.GbaAudio.GbAudio.noiseVal}");
                // ImGui.Text("");
                // ImGui.Text($"Pulse 1 Enabled: {Gba.GbaAudio.GbAudio.pulse1_enabled}");
                // ImGui.Text($"Pulse 1 Width: {Gba.GbaAudio.GbAudio.pulse1_width}");
                // ImGui.Text($"Pulse 1 DAC Enabled: {Gba.GbaAudio.GbAudio.pulse1_dacEnabled}");
                // ImGui.Text($"Pulse 1 Length Enable: {Gba.GbaAudio.GbAudio.pulse1_lengthEnable}");
                // ImGui.Text($"Pulse 1 Length Counter: {Gba.GbaAudio.GbAudio.pulse1_lengthCounter}");
                // ImGui.Text($"Pulse 1 Frequency Upper: {Gba.GbaAudio.GbAudio.pulse1_frequencyUpper}");
                // ImGui.Text($"Pulse 1 Frequency Lower: {Gba.GbaAudio.GbAudio.pulse1_frequencyLower}");
                // ImGui.Text($"Pulse 1 Volume: {Gba.GbaAudio.GbAudio.pulse1_volume}");
                // ImGui.Text($"Pulse 1 Volume Envelope Up: {Gba.GbaAudio.GbAudio.pulse1_volumeEnvelopeUp}");
                // ImGui.Text($"Pulse 1 Volume Envelope Sweep: {Gba.GbaAudio.GbAudio.pulse1_volumeEnvelopeSweep}");
                // ImGui.Text($"Pulse 1 Volume Envelope Start: {Gba.GbaAudio.GbAudio.pulse1_volumeEnvelopeStart}");
                // ImGui.Text($"Pulse 1 Output Left: {Gba.GbaAudio.GbAudio.pulse1_outputLeft}");
                // ImGui.Text($"Pulse 1 Output Right: {Gba.GbaAudio.GbAudio.pulse1_outputRight}");
                // ImGui.Text($"Pulse 1 Freq Sweep Period: {Gba.GbaAudio.GbAudio.pulse1_freqSweepPeriod}");
                // ImGui.Text($"Pulse 1 Freq Sweep Up: {Gba.GbaAudio.GbAudio.pulse1_freqSweepUp}");
                // ImGui.Text($"Pulse 1 Freq Sweep Shift: {Gba.GbaAudio.GbAudio.pulse1_freqSweepShift}");
                // ImGui.Text($"Pulse 1 Updated: {Gba.GbaAudio.GbAudio.pulse1_updated}");
                // ImGui.Text("");
                // ImGui.Text($"Wave Bank: {Gba.GbaAudio.GbAudio.wave_bank}");
                // ImGui.Text($"Wave Dimension: {Gba.GbaAudio.GbAudio.wave_dimension}");
                // ImGui.Text($"Wave Enabled: {Gba.GbaAudio.GbAudio.wave_enabled}");
                // ImGui.Text($"Wave DAC Enabled: {Gba.GbaAudio.GbAudio.wave_dacEnabled}");
                // ImGui.Text($"Wave Length Enable: {Gba.GbaAudio.GbAudio.wave_lengthEnable}");
                // ImGui.Text($"Wave Length Counter: {Gba.GbaAudio.GbAudio.wave_lengthCounter}");
                // ImGui.Text($"Wave Frequency Upper: {Gba.GbaAudio.GbAudio.wave_frequencyUpper}");
                // ImGui.Text($"Wave Frequency Lower: {Gba.GbaAudio.GbAudio.wave_frequencyLower}");
                // ImGui.Text($"Wave Volume: {Gba.GbaAudio.GbAudio.wave_volume}");
                // ImGui.Text($"Wavetable 0: {string.Join(" ", Gba.GbaAudio.GbAudio.wave_waveTable0)}");
                // ImGui.Text($"Wavetable 1: {string.Join(" ", Gba.GbaAudio.GbAudio.wave_waveTable1)}");

                // ImGui.Text($"Buffer Samples: {Gba.GbaAudio.SampleBuffer.Entries / 2}");
                ImGui.Checkbox("Enable PSGs", ref Gba.GbaAudio.EnablePsg);
                ImGui.Checkbox("Enable FIFOs", ref Gba.GbaAudio.EnableFifo);

                ImGui.Text($"PSG Factor: {Gba.GbaAudio.GbAudio.PsgFactor}");
                if (ImGui.Button("-##psg"))
                {
                    if (Gba.GbaAudio.GbAudio.PsgFactor > 0)
                    {
                        Gba.GbaAudio.GbAudio.PsgFactor--;
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("+##psg"))
                {
                    Gba.GbaAudio.GbAudio.PsgFactor++;
                }

                var rend = Gba.Ppu.Renderer;
                ImGui.Text($"BG0 Size X/Y: {PpuRenderer.CharWidthTable[rend.Backgrounds[0].ScreenSize]}/{PpuRenderer.CharHeightTable[rend.Backgrounds[0].ScreenSize]}");
                ImGui.Text($"BG0 Scroll X: {rend.Backgrounds[0].HorizontalOffset}");
                ImGui.Text($"BG0 Scroll Y: {rend.Backgrounds[0].VerticalOffset}");
                ImGui.Text($"BG1 Size X/Y: {PpuRenderer.CharWidthTable[rend.Backgrounds[1].ScreenSize]}/{PpuRenderer.CharHeightTable[rend.Backgrounds[1].ScreenSize]}");
                ImGui.Text($"BG1 Scroll X: {rend.Backgrounds[1].HorizontalOffset}");
                ImGui.Text($"BG1 Scroll Y: {rend.Backgrounds[1].VerticalOffset}");
                ImGui.Text($"BG2 Size X/Y: {PpuRenderer.CharWidthTable[rend.Backgrounds[2].ScreenSize]}/{PpuRenderer.CharHeightTable[rend.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Affine Size: {PpuRenderer.AffineSizeTable[rend.Backgrounds[2].ScreenSize]}/{PpuRenderer.AffineSizeTable[rend.Backgrounds[2].ScreenSize]}");
                ImGui.Text($"BG2 Scroll X: {rend.Backgrounds[2].HorizontalOffset}");
                ImGui.Text($"BG2 Scroll Y: {rend.Backgrounds[2].VerticalOffset}");
                ImGui.Text($"BG3 Size X/Y: {PpuRenderer.CharWidthTable[rend.Backgrounds[3].ScreenSize]}/{PpuRenderer.CharHeightTable[rend.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Affine Size: {PpuRenderer.AffineSizeTable[rend.Backgrounds[3].ScreenSize]}/{PpuRenderer.AffineSizeTable[rend.Backgrounds[3].ScreenSize]}");
                ImGui.Text($"BG3 Scroll X: {rend.Backgrounds[3].HorizontalOffset}");
                ImGui.Text($"BG3 Scroll Y: {rend.Backgrounds[3].VerticalOffset}");
                ImGui.Checkbox("Debug BG0", ref rend.DebugEnableBg[0]);
                ImGui.Checkbox("Debug BG1", ref rend.DebugEnableBg[1]);
                ImGui.Checkbox("Debug BG2", ref rend.DebugEnableBg[2]);
                ImGui.Checkbox("Debug BG3", ref rend.DebugEnableBg[3]);
                ImGui.Checkbox("Debug OBJ", ref rend.DebugEnableObj);

                ImGui.Text($"Window 0 Left..: {rend.Win0HLeft}");
                ImGui.Text($"Window 0 Right.: {rend.Win0HRight}");
                ImGui.Text($"Window 0 Top...: {rend.Win0VTop}");
                ImGui.Text($"Window 0 Bottom: {rend.Win0VBottom}");
                ImGui.Text($"Window 1 Left..: {rend.Win1HLeft}");
                ImGui.Text($"Window 1 Right.: {rend.Win1HRight}");
                ImGui.Text($"Window 1 Top...: {rend.Win1VTop}");
                ImGui.Text($"Window 1 Bottom: {rend.Win1VBottom}");

                ImGui.Columns(1);
                ImGui.Separator();

                ImGui.Text("Palettes");

                for (uint p = 0; p < 256; p++)
                {
                    PaletteImageBuffer[p] = PpuRenderer.Rgb555To888(Gba.Ppu.Renderer.LookupPalette(p), false);
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
                ImGui.Image((IntPtr)bgPalTexId, new System.Numerics.Vector2(16 * 8, 16 * 8));

                for (uint p = 0; p < 256; p++)
                {
                    PaletteImageBuffer[p] = PpuRenderer.Rgb555To888(Gba.Ppu.Renderer.LookupPalette(p + 256), false);
                }

                GL.BindTexture(TextureTarget.Texture2D, objPalTexId);

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

                ImGui.SameLine(); ImGui.Image((IntPtr)objPalTexId, new System.Numerics.Vector2(16 * 8, 16 * 8));

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
                drawDisassembly(Gba);
            }
        }

        public uint[] DisplayBuffer = new uint[240 * 160];

        public bool BigScreen = false;
        public bool ShowBackBuf = false;
        public unsafe void DrawDisplay()
        {
            if (ImGui.Begin("Display", ImGuiWindowFlags.AlwaysAutoResize))
            {
                gbTexId = 0;

                var buf = ShowBackBuf ? Gba.Ppu.Renderer.ScreenBack : Gba.Ppu.Renderer.ScreenFront;
                for (uint i = 0; i < 240 * 160; i++)
                {
                    DisplayBuffer[i] = PpuRenderer.ColorLutCorrected[buf[i] & 0x7FFF];
                }

                // GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, gbTexId);
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    240,
                    160,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    DisplayBuffer
                );

                float height = BigScreen ? 240 * 5 : 240 * 2;
                float width = BigScreen ? 160 * 5 : 160 * 2;

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
                    new RegisterField("Reserved / CGB Mode", 3),
                    new RegisterField("Display Frame Select", 4),
                    new RegisterField("H-Blank Interval Form", 5),
                    new RegisterField("OBJ Character VRAM Mapping", 6),
                    new RegisterField("Forced Blank", 7),
                    new RegisterField("Screen Display BG0", 8),
                    new RegisterField("Screen Display BG1", 9),
                    new RegisterField("Screen Display BG2", 10),
                    new RegisterField("Screen Display BG3", 11),
                    new RegisterField("Screen Display OBJ", 12),
                    new RegisterField("Window 0 Display Flag", 13),
                    new RegisterField("Window 1 Display Flag", 14),
                    new RegisterField("OBJ Window Display Flag", 15)
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

                uint value = Gba.Mem.ReadDebug32(RegViewerSelected.Address);
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
                    ThumbExecutor k = Gba.Cpu.ThumbDispatch[ti];
                    if (!CpuProfilerDictThumb.TryGetValue(k, out uint val))
                    {
                        CpuProfilerDictThumb[k] = 0;
                    }
                    CpuProfilerDictThumb[k] += Arm7.ThumbExecutorProfile[ti];
                }

                for (int ai = 0; ai < 4096; ai++)
                {
                    ArmExecutor k = Gba.Cpu.ArmDispatch[ai];
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
                foreach (KeyValuePair<uint, uint> entry in Gba.Mem.HwioReadLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} reads");
                }
                ImGui.Separator();
                foreach (KeyValuePair<uint, uint> entry in Gba.Mem.HwioWriteLog)
                {
                    ImGui.Text($"{Hex(entry.Key, 8)}: {entry.Value} writes");
                }

                ImGui.End();
            }
        }

        public void DumpSav()
        {
            try
            {
                System.IO.File.WriteAllBytesAsync(Gba.Provider.SavPath, Gba.Mem.SaveProvider.GetSave());
            }
            catch
            {
                Console.WriteLine("Failed to write .sav file!");
            }
        }

        public void DrawPulseBox(int duty, float widthMul, float heightMul)
        {
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            System.Numerics.Vector2 pos = ImGui.GetCursorScreenPos();
            float width = ImGui.GetWindowContentRegionWidth();

            ImGui.Dummy(new System.Numerics.Vector2(0, 128));
            dl.AddRectFilled(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Button));
            dl.AddRect(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Border));

            uint lineCol = ImGui.GetColorU32(ImGuiCol.PlotLines);

            float init = 0;
            float xPerUnit = ((width) / 8) * widthMul;
            float valX = pos.X;

            float yCenter = (pos.Y + 64);
            float yHigh = (yCenter - (heightMul * 56));
            float yLow = (yCenter + (heightMul * 56));

            for (uint i = 0; i < 2048; i++)
            {
                float val = PulseDuty[duty][i & 7];
                val = (val * -1) + 1;

                float newX = valX + xPerUnit;
                if (newX > pos.X + width) newX = pos.X + width;
                if (valX > pos.X + width) valX = pos.X + width;
                if (val != init)
                {
                    // Make sure vertical line isn't off the edge of the box
                    if (valX > pos.X && valX < pos.X + width)
                    {
                        dl.AddLine(new System.Numerics.Vector2(valX, yHigh), new System.Numerics.Vector2(valX, yLow), lineCol, 2);
                    }
                }
                if (val != 0)
                {
                    dl.AddLine(new System.Numerics.Vector2(valX, yHigh), new System.Numerics.Vector2(newX, yHigh), lineCol, 2);
                }
                else
                {
                    dl.AddLine(new System.Numerics.Vector2(valX, yLow), new System.Numerics.Vector2(newX, yLow), lineCol, 2);
                }
                valX += xPerUnit;

                init = val;

                if (valX > pos.X + width) return;
            }
        }

        public void DrawWaveBox(byte[] waveTable, float widthMul, int waveShift)
        {
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            System.Numerics.Vector2 pos = ImGui.GetCursorScreenPos();
            float width = ImGui.GetWindowContentRegionWidth();

            ImGui.Dummy(new System.Numerics.Vector2(0, 128));
            dl.AddRectFilled(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Button));
            dl.AddRect(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Border));

            uint lineCol = ImGui.GetColorU32(ImGuiCol.PlotLines);

            float prev = 0;
            float xPerUnit = ((width) / 32) * widthMul;
            float valX = pos.X;

            float yCenter = (pos.Y + 64);
            float yHigh = yCenter - 56;
            float yLow = yCenter + 56;

            for (uint i = 0; i < 2048; i++)
            {
                float val = waveTable[i & 31] >> waveShift;

                float y = yLow - ((val / 15) * 112);
                float yPrev = yLow - ((prev / 15) * 112);

                float newX = valX + xPerUnit;
                if (newX > pos.X + width) newX = pos.X + width;
                if (valX > pos.X + width) valX = pos.X + width;
                if (val != prev)
                {
                    // Make sure vertical line isn't off the edge of the box
                    if (valX > pos.X && valX < pos.X + width)
                    {
                        dl.AddLine(new System.Numerics.Vector2(valX, y), new System.Numerics.Vector2(valX, yPrev), lineCol, 2);
                    }
                }
                dl.AddLine(new System.Numerics.Vector2(valX, y), new System.Numerics.Vector2(newX, y), lineCol, 2);
                valX += xPerUnit;

                prev = val;

                if (valX > pos.X + width) return;
            }
        }

        public void DrawNoiseBox(byte[] noiseArray, float widthMul, float heightMul)
        {
            ImDrawListPtr dl = ImGui.GetWindowDrawList();
            System.Numerics.Vector2 pos = ImGui.GetCursorScreenPos();
            float width = ImGui.GetWindowContentRegionWidth();

            ImGui.Dummy(new System.Numerics.Vector2(0, 128));
            dl.AddRectFilled(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Button));
            dl.AddRect(pos, new System.Numerics.Vector2(pos.X + width, pos.Y + 128), ImGui.GetColorU32(ImGuiCol.Border));

            uint lineCol = ImGui.GetColorU32(ImGuiCol.PlotLines);

            float init = 0;
            float xPerUnit = ((width) / 8) * widthMul;
            float valX = pos.X;

            float yCenter = (pos.Y + 64);
            float yHigh = (yCenter - (heightMul * 56));
            float yLow = (yCenter + (heightMul * 56));

            for (uint i = 0; i < 2048; i++)
            {
                float val = noiseArray[NoisePos++ & 32767];
                val = ((val * -1) + 1);

                float newX = valX + xPerUnit;
                if (newX > pos.X + width) newX = pos.X + width;
                if (valX > pos.X + width) valX = pos.X + width;
                if (val != init)
                {
                    // Make sure vertical line isn't off the edge of the box
                    if (valX > pos.X && valX < pos.X + width)
                    {
                        dl.AddLine(new System.Numerics.Vector2(valX, yHigh), new System.Numerics.Vector2(valX, yLow), lineCol, 2);
                    }
                }
                if (val != 0)
                {
                    dl.AddLine(new System.Numerics.Vector2(valX, yHigh), new System.Numerics.Vector2(newX, yHigh), lineCol, 2);
                }
                else
                {
                    dl.AddLine(new System.Numerics.Vector2(valX, yLow), new System.Numerics.Vector2(newX, yLow), lineCol, 2);
                }
                valX += xPerUnit;

                init = val;

                if (valX > pos.X + width) return;
            }
        }

        public void DrawSoundVisualizer()
        {
            if (ImGui.Begin("Sound Visualizer"))
            {
                // ImGui.Columns(2);
                GbAudio gbAudio = Gba.GbaAudio.GbAudio;

                ImGui.Text("Pulse 1");

                float pulse1Hz = gbAudio.pulse1_getFrequencyHz();
                bool pulse1Active = gbAudio.pulse1_enabled && gbAudio.pulse1_dacEnabled;
                DrawPulseBox(gbAudio.pulse1_width, 64 / pulse1Hz, pulse1Active ? gbAudio.pulse1_volume / 15f : 0);
                int pulse1Note = NoteFromFrequency(pulse1Hz);
                float pulse1CentsOff = (float)CentsOffFromPitch(pulse1Hz, pulse1Note);
                ImGui.Text($"Active: {pulse1Active}");
                ImGui.Text($"Volume: {gbAudio.pulse1_volume}");
                ImGui.Text($"Pitch: {pulse1Hz} hz");
                ImGui.Text($"Note: {NoteNameFromFrequency(pulse1Hz)} {OctaveFromFrequency(pulse1Hz)} {(pulse1CentsOff < 0 ? "" : "+") + pulse1CentsOff}");

                ImGuiColumnSeparator();

                ImGui.Text("Pulse 2");

                float pulse2Hz = gbAudio.pulse2_getFrequencyHz();
                bool pulse2Active = gbAudio.pulse2_enabled && gbAudio.pulse2_dacEnabled;
                DrawPulseBox(gbAudio.pulse2_width, 64 / pulse2Hz, pulse2Active ? gbAudio.pulse2_volume / 15f : 0);
                int pulse2Note = NoteFromFrequency(pulse2Hz);
                double pulse2CentsOff = CentsOffFromPitch(pulse2Hz, pulse2Note);
                ImGui.Text($"Active: {pulse2Active}");
                ImGui.Text($"Volume: {gbAudio.pulse2_volume}");
                ImGui.Text($"Pitch: {pulse2Hz} hz");
                ImGui.Text($"Note: {NoteNameFromFrequency(pulse2Hz)} {OctaveFromFrequency(pulse2Hz)} {(pulse2CentsOff < 0 ? "" : "+") + pulse2CentsOff}");

                ImGuiColumnSeparator();

                ImGui.Text("Wave");
                float waveHz = Gba.GbaAudio.GbAudio.wave_getFrequencyHz();
                bool waveActive = gbAudio.wave_enabled && gbAudio.wave_dacEnabled && (gbAudio.wave_outputLeft || gbAudio.wave_outputRight) && gbAudio.wave_volume != 0;
                DrawWaveBox(gbAudio.wave_bank ? gbAudio.wave_waveTable1 : gbAudio.wave_waveTable0, 32 / waveHz, waveActive ? WaveShiftCodes[gbAudio.wave_volume] : 4);
                int waveNote = NoteFromFrequency(waveHz);
                double waveCentsOff = CentsOffFromPitch(waveHz, waveNote);
                ImGui.Text($"Pitch: {waveHz} hz");
                ImGui.Text($"Note: {NoteNameFromFrequency(waveHz)} {OctaveFromFrequency(waveHz)} {(waveCentsOff < 0 ? "" : "+") + waveCentsOff}");

                ImGuiColumnSeparator();

                ImGui.Text("Noise");

                long noiseHz = 524288 / NoiseDivisors[gbAudio.noise_divisorCode] / 2 ^ (gbAudio.noise_shiftClockFrequency + 1);
                bool noiseActive = gbAudio.noise_enabled && gbAudio.noise_dacEnabled && (gbAudio.noise_outputLeft || gbAudio.noise_outputRight);
                DrawNoiseBox(gbAudio.noise_counterStep ? GbAudio.SEVEN_BIT_NOISE : GbAudio.FIFTEEN_BIT_NOISE, 0.025f, noiseActive ? gbAudio.noise_volume / 15f : 0);

                // ImGui.NextColumn();

                // uint basePtr = 0x03006380;
                // ImGui.Text("Base Pointer: " + Hex(basePtr, 8));

                // ImGui.Text("ID: " + Gba.Mem.Read32(basePtr)); basePtr += 4;
                // ImGui.Text("PCM DMA Counter: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Reverb: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Max Channels: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Master Volume: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Frequency: " + Gba.Mem.Read8(basePtr)); basePtr += 1;

                // ImGui.Text("Mode: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Transpose: " + Gba.Mem.Read8(basePtr)); basePtr += 1;
                // ImGui.Text("Transpose: " + Gba.Mem.Read8(basePtr)); basePtr += 1;

                // const uint maxChans = 12;
                // uint soundChannelPtr = basePtr + 0x90;
                // for (int i = 0; i < maxChans; i++)
                // {
                //     ImGui.Text($"Ch{i} Left Vol: " + Gba.Mem.Read8(soundChannelPtr + 2));
                //     ImGui.Text($"Ch{i} Right Vol: " + Gba.Mem.Read8(soundChannelPtr + 3));
                //     soundChannelPtr += 0x10;
                // }


                ImGui.End();

            }
        }

        uint NoisePos = 0;

        public uint[][] PulseDuty = new uint[][] {
            new uint[] {0, 0, 0, 0, 0, 0, 0, 1},
            new uint[] {1, 0, 0, 0, 0, 0, 0, 1},
            new uint[] {1, 0, 0, 0, 0, 1, 1, 1},
            new uint[] {0, 1, 1, 1, 1, 1, 1, 0},
        };

        public uint[] NoiseDivisors = { 8, 16, 32, 48, 63, 80, 96, 112 };

        public int[] WaveShiftCodes = { 4, 0, 1, 2 };

        string[] noteStrings = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        int NoteFromFrequency(float frequency)
        {
            var noteNum = 12 * (Math.Log(frequency / 440) / Math.Log(2));
            return (int)(Math.Round(noteNum) + 69);
        }

        double FrequencyFromNote(int note)
        {
            return Math.Pow(2, (double)(note - 69) / 12) * 440;
        }

        string NoteNameFromFrequency(float frequency)
        {
            return noteStrings[(uint)(NoteFromFrequency(frequency)) % 12];
        }

        double OctaveFromFrequency(float frequency)
        {
            // Use octave 0 as base
            return Math.Floor(Math.Log2(frequency / 27.50));
        }

        double CentsOffFromPitch(float frequency, int note)
        {
            return Math.Floor(1200 * Math.Log(frequency / FrequencyFromNote(note)) / Math.Log(2));
        }

        public void DrawSchedulerInfo()
        {
            if (ImGui.Begin("Scheduler"))
            {
                // if (ImGui.Button("Pop First Event")) Gba.Scheduler.PopFirstEvent();
                // if (ImGui.Button("Add 0")) Gba.Scheduler.AddEventRelative(SchedulerId.None, 0, (long cyclesLate) => { });
                // if (ImGui.Button("Add 100")) Gba.Scheduler.AddEventRelative(SchedulerId.None, 100, (long cyclesLate) => { });
                // if (ImGui.Button("Add 500")) Gba.Scheduler.AddEventRelative(SchedulerId.None, 500, (long cyclesLate) => { });
                // if (ImGui.Button("Add 42069")) Gba.Scheduler.AddEventRelative(SchedulerId.None, 42069, (long cyclesLate) => { });

                ImGui.Text($"Current Ticks: {Gba.Scheduler.CurrentTicks}");
                ImGui.Text($"Next event at: {Gba.Scheduler.NextEventTicks}");
                ImGui.Text($"Events queued: {Gba.Scheduler.EventsQueued}");

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

                SchedulerEvent evt = Gba.Scheduler.RootEvent.NextEvent;
                int index = 0;
                while (evt != null)
                {
                    ImGui.Text(index.ToString());
                    ImGui.NextColumn();
                    ImGui.Text((evt.Ticks - Gba.Scheduler.CurrentTicks).ToString());
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