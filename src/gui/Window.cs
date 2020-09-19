using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using System.Drawing;
using OpenTK.Input;
using System;
using System.IO;
using ImGuiNET;
using System.Threading;
using ImGuiUtils;
using static Util;
using System.Collections.Generic;
using System.Numerics;
using OptimeGBA;
using Gee.External.Capstone.Arm;
using System.Text;
using System.Diagnostics;

namespace OptimeGBAEmulator
{
    public class Game : GameWindow
    {
        int gbTexId;
        int tsTexId;
        ImGuiController _controller;
        int VertexBufferObject;
        int VertexArrayObject;

        string[] Log;
        int LogIndex = -0;

        GBA Gba;
        Thread EmulationThread;
        ManualResetEvent ThreadSync = new ManualResetEvent(false);

        bool SyncToAudio = true;
        double FrameRate;

        const uint AUDIO_SAMPLE_THRESHOLD = 8192;

        public void EmulationThreadHandler()
        {
            Stopwatch stopwatch = new Stopwatch();
            while (true)
            {
                ThreadSync.Reset();
                ThreadSync.WaitOne();
                while ((OptimeGBAEmulator.GetAudioSamplesInQueue() < AUDIO_SAMPLE_THRESHOLD || !SyncToAudio) && !Gba.Arm7.Errored && FrameStep)
                {
                    stopwatch.Reset();
                    stopwatch.Start();
                    RunFrame();
                    stopwatch.Stop();
                    long ticks = stopwatch.ElapsedTicks;
                    double seconds = (double)ticks / (double)Stopwatch.Frequency;

                    FrameRate = 1 / seconds;
                }
            }
        }

        public void RunFrame()
        {
            int num = FrameIns;
            while (num > 0 && !Gba.Arm7.Errored && FrameStep)
            {
                num -= (int)Gba.Step();
            }
        }

        public void RunAudioSync()
        {
            if (OptimeGBAEmulator.GetAudioSamplesInQueue() < AUDIO_SAMPLE_THRESHOLD || !SyncToAudio)
            {
                RunFrame();
            }
        }

        CapstoneArmDisassembler ArmDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
        CapstoneArmDisassembler ThumbDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Thumb);

        bool FrameStep = false;

        public Game(int width, int height, string title, AudioCallback audioCallback) : base(width, height, GraphicsMode.Default, title)
        {
            byte[] bios = System.IO.File.ReadAllBytes("roms/GBA.BIOS");
            // byte[] bios = System.IO.File.ReadAllBytes("roms/NormattBIOS.bin");
            byte[] rom = System.IO.File.ReadAllBytes("roms/Pokemon - FireRed Version (USA).gba");

            RomList = Directory.GetFiles("roms", "*.gba");

            Gba = new GBA(new GbaProvider(bios, rom, audioCallback));

            EmulationThread = new Thread(EmulationThreadHandler);
            EmulationThread.Name = "Emulation Core";
            EmulationThread.Start();

            string file = System.IO.File.ReadAllText("./swi_demo.txt");
            Log = file.Split('\n');

            SetupRegViewer();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _controller._windowHeight = Height;
            _controller._windowWidth = Width;
            GL.Viewport(0, 0, Width, Height);
        }

        float[] vertices = {
            1f,  1f, 0.0f, 1.0f, 0.0f, // top right
            1f, -1f, 0.0f, 1.0f, 1.0f, // bottom right
            -1f, -1f, 0.0f, 0.0f, 1.0f, // bottom left
            -1f,  1f, 0.0f, 0.0f, 0.0f  // top left
        };
        protected override void OnLoad(EventArgs e)
        {
            VertexArrayObject = GL.GenVertexArray();
            GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
            VertexBufferObject = GL.GenBuffer();

            GL.Enable(EnableCap.Texture2D);
            // Disable texture filtering
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
            _controller = new ImGuiController(Width, Height);

            gbTexId = GL.GenTexture();
            tsTexId = GL.GenTexture();

            base.OnLoad(e);
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);


            KeyboardState input = Keyboard.GetState();

            if (FrameStep)
            {
                // RunAudioSync(); // Same Thread
                ThreadSync.Set(); // Multi-Thread
            }

            Gba.Keypad.B = input.IsKeyDown(Key.Z);
            Gba.Keypad.A = input.IsKeyDown(Key.X);
            Gba.Keypad.Left = input.IsKeyDown(Key.Left);
            Gba.Keypad.Up = input.IsKeyDown(Key.Up);
            Gba.Keypad.Right = input.IsKeyDown(Key.Right);
            Gba.Keypad.Down = input.IsKeyDown(Key.Down);
            Gba.Keypad.Start = input.IsKeyDown(Key.Enter) || input.IsKeyDown(Key.KeypadEnter);
            Gba.Keypad.Select = input.IsKeyDown(Key.BackSpace);
            Gba.Keypad.L = input.IsKeyDown(Key.Q);
            Gba.Keypad.R = input.IsKeyDown(Key.E);

        }

        const int FrameIns = 70224;

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            _controller.Update(this, (float)e.Time);


            // ImGui.Begin("It's a tileset");
            // ImGui.Text($"Pointer: {tsTexId}");
            // ImGui.Image((IntPtr)tsTexId, new System.Numerics.Vector2(256 * 2, 96 * 2));
            // ImGui.End();

            DrawDisplay();
            DrawDebug();
            DrawInstrViewer();
            DrawInstrInfo();
            DrawRegViewer();
            DrawMemoryViewer();
            DrawRomSelector();
            DrawHwioLog();
            DrawBankedRegisters();

            GL.ClearColor(1f, 1f, 1f, 1f);
            GL.Clear(ClearBufferMask.StencilBufferBit | ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _controller.Render();
            GL.Flush();

            Context.SwapBuffers();

            // shader.Use();


            // GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            // GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            // GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            // int texCoordLocation = shader.GetAttribLocation("aTexCoord");
            // GL.EnableVertexAttribArray(texCoordLocation);
            // GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            // GL.EnableVertexAttribArray(0);

            // GL.BindVertexArray(VertexArrayObject);
            // GL.DrawArrays(PrimitiveType.Quads, 0, 4);

            // GL.Flush();
            // Context.SwapBuffers();

        }

        public void ResetGba()
        {
            GbaProvider p = Gba.Provider;
            Gba = new GBA(p);
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
                ImGui.Text("R13: " + Hex(Gba.Arm7.R13usr, 8));
                ImGui.Text("R14: " + Hex(Gba.Arm7.R14usr, 8));

                ImGui.NextColumn();

                ImGui.Text("Supervisor");
                ImGui.Text("R13: " + Hex(Gba.Arm7.R13svc, 8));
                ImGui.Text("R14: " + Hex(Gba.Arm7.R14svc, 8));

                ImGui.NextColumn();

                ImGui.Text("Abort");
                ImGui.Text("R13: " + Hex(Gba.Arm7.R13abt, 8));
                ImGui.Text("R14: " + Hex(Gba.Arm7.R14abt, 8));

                ImGui.NextColumn();

                ImGui.Text("IRQ");
                ImGui.Text("R13: " + Hex(Gba.Arm7.R13irq, 8));
                ImGui.Text("R14: " + Hex(Gba.Arm7.R14irq, 8));

                ImGui.NextColumn();

                ImGui.Text("Undefined");
                ImGui.Text("R13: " + Hex(Gba.Arm7.R13und, 8));
                ImGui.Text("R14: " + Hex(Gba.Arm7.R14und, 8));

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
                        uint val = Gba.Mem.ReadDebug8(tempBase);

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
            text += $"{HexN(Gba.Arm7.R[0], 8)} ";
            text += $"{HexN(Gba.Arm7.R[1], 8)} ";
            text += $"{HexN(Gba.Arm7.R[2], 8)} ";
            text += $"{HexN(Gba.Arm7.R[3], 8)} ";
            text += $"{HexN(Gba.Arm7.R[4], 8)} ";
            text += $"{HexN(Gba.Arm7.R[5], 8)} ";
            text += $"{HexN(Gba.Arm7.R[6], 8)} ";
            text += $"{HexN(Gba.Arm7.R[7], 8)} ";
            text += $"{HexN(Gba.Arm7.R[8], 8)} ";
            text += $"{HexN(Gba.Arm7.R[9], 8)} ";
            text += $"{HexN(Gba.Arm7.R[10], 8)} ";
            text += $"{HexN(Gba.Arm7.R[11], 8)} ";
            text += $"{HexN(Gba.Arm7.R[12], 8)} ";
            text += $"{HexN(Gba.Arm7.R[13], 8)} ";
            text += $"{HexN(Gba.Arm7.R[14], 8)} ";
            text += $"{HexN(Gba.Arm7.R[15], 8)} ";
            text += $"cpsr: {HexN(Gba.Arm7.GetCPSR(), 8)} ";
            String emuText = text.Substring(0, 135) + text.Substring(144, 14) + $" {LogIndex + 1}";
            return emuText;
        }

        public String DisasmThumb(ushort opcode)
        {
            ThumbDisassembler.EnableInstructionDetails = true;

            byte[] code = new byte[] {
                            (byte)((opcode >> 0) & 0xFF),
                            (byte)((opcode >> 8) & 0xFF),
                        };

            String disasm = "";

            ArmInstruction[] instructions = ThumbDisassembler.Disassemble(code);
            foreach (ArmInstruction ins in instructions)
            {
                disasm = $"{ins.Mnemonic} {ins.Operand}";
            }
            return disasm;
        }

        public String DisasmArm(uint opcode)
        {
            ArmDisassembler.EnableInstructionDetails = true;

            byte[] code = new byte[] {
                            (byte)((opcode >> 0) & 0xFF),
                            (byte)((opcode >> 8) & 0xFF),
                            (byte)((opcode >> 16) & 0xFF),
                            (byte)((opcode >> 24) & 0xFF),
                        };

            String disasm = "";

            ArmInstruction[] instructions = ArmDisassembler.Disassemble(code);
            foreach (ArmInstruction ins in instructions)
            {
                disasm = $"{ins.Mnemonic} {ins.Operand}";
            }
            return disasm;
        }

        public String BuildEmuFullText()
        {
            String disasm = Gba.Arm7.ThumbState ? DisasmThumb((ushort)Gba.Arm7.LastIns) : DisasmArm(Gba.Arm7.LastIns);

            StringBuilder builder = new StringBuilder();
            builder.Append($"{HexN(Gba.Arm7.R[0], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[1], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[2], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[3], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[4], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[5], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[6], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[7], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[8], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[9], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[10], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[11], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[12], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[13], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[14], 8)} ");
            builder.Append($"{HexN(Gba.Arm7.R[15], 8)} ");
            builder.Append($"cpsr: {HexN(Gba.Arm7.GetCPSR(), 8)} | ");
            builder.Append($"{(Gba.Arm7.ThumbState ? "    " + HexN(Gba.Arm7.LastIns, 4) : HexN(Gba.Arm7.LastIns, 8))}: {disasm}");
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
                ImGui.Text(Gba.Arm7.Debug);
                ImGui.End();
            }
        }

        public void DrawDebug()
        {

            if (ImGui.Begin("Debug"))
            {
                ImGui.Columns(4);

                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);
                ImGui.Text($"R0:  {Hex(Gba.Arm7.R[0], 8)}");
                ImGui.Text($"R1:  {Hex(Gba.Arm7.R[1], 8)}");
                ImGui.Text($"R2:  {Hex(Gba.Arm7.R[2], 8)}");
                ImGui.Text($"R3:  {Hex(Gba.Arm7.R[3], 8)}");
                ImGui.Text($"R4:  {Hex(Gba.Arm7.R[4], 8)}");
                ImGui.Text($"R5:  {Hex(Gba.Arm7.R[5], 8)}");
                ImGui.Text($"R6:  {Hex(Gba.Arm7.R[6], 8)}");
                ImGui.Text($"R7:  {Hex(Gba.Arm7.R[7], 8)}");
                ImGui.Text($"R8:  {Hex(Gba.Arm7.R[8], 8)}");
                ImGui.Text($"R9:  {Hex(Gba.Arm7.R[9], 8)}");
                ImGui.Text($"R10: {Hex(Gba.Arm7.R[10], 8)}");
                ImGui.Text($"R11: {Hex(Gba.Arm7.R[11], 8)}");
                ImGui.Text($"R12: {Hex(Gba.Arm7.R[12], 8)}");
                ImGui.Text($"R13: {Hex(Gba.Arm7.R[13], 8)}");
                ImGui.Text($"R14: {Hex(Gba.Arm7.R[14], 8)}");
                ImGui.Text($"R15: {Hex(Gba.Arm7.R[15], 8)}");
                ImGui.Text($"CPSR: {Hex(Gba.Arm7.GetCPSR(), 8)}");
                ImGui.Text($"Instruction: {Hex(Gba.Arm7.LastIns, Gba.Arm7.ThumbState ? 4 : 8)}");
                ImGui.Text($"Prev. Ins.: {Hex(Gba.Arm7.LastLastIns, Gba.Arm7.ThumbState ? 4 : 8)}");
                ImGui.Text($"Disasm: {(Gba.Arm7.ThumbState ? DisasmThumb((ushort)Gba.Arm7.LastIns) : DisasmArm(Gba.Arm7.LastIns))}");

                ImGui.Text($"Mode: {Gba.Arm7.Mode}");
                ImGui.Text($"Last Cycles: {Gba.Arm7.LastPendingCycles}");
                ImGui.Text($"Total Instrs.: {Gba.Arm7.InstructionsRan}");
                ImGui.Text($"Pipeline: {Gba.Arm7.Pipeline}");

                // ImGui.Text($"Ins Next Up: {(Gba.Arm7.ThumbState ? Hex(Gba.Arm7.THUMBDecode, 4) : Hex(Gba.Arm7.ARMDecode, 8))}");

                ImGui.Text($"");

                if (ImGui.Button("Reset"))
                {
                    ResetGba();
                }

                if (ImGui.Button("Frame Advance"))
                {
                    int num = 280896;
                    while (num > 0 && !Gba.Arm7.Errored)
                    {
                        Gba.Step();
                        num--;
                    }
                }

                if (ImGui.Button("Un-error"))
                {
                    Gba.Arm7.Errored = false;
                }
                if (ImGui.Button("Step"))
                {
                    Gba.Step();
                    LogIndex++;
                }
                // if (ImGui.Button("Step Until Error"))
                // {
                //     bool exit = false;
                //     while (!Gba.Arm7.Errored && !exit)
                //     {

                //         Gba.Step();
                //         LogIndex++;

                //         if (BuildEmuText() != BuildLogText())
                //         {
                //             exit = true;
                //         }
                //     }
                // }

                ImGui.InputText("", text, 4);
                ImGui.InputInt("", ref DebugStepFor);
                if (ImGui.Button("Step For"))
                {
                    using (StreamWriter file = new StreamWriter("log.txt"))
                    {
                        int num = DebugStepFor;
                        while (num > 0 && !Gba.Arm7.Errored)
                        {

                            // file.WriteLine(BuildEmuFullText());
                            Gba.Step();

                            if (Gba.Arm7.InstructionsRanInterrupt == Gba.Arm7.InstructionsRan)
                            {
                                file.WriteLine("---------------- INTERRUPT ----------------");
                            }

                            LogIndex++;
                            num--;
                        }
                    }
                }

                if (ImGui.Button("Step 100000"))
                {
                    using (StreamWriter file = new StreamWriter("log.txt"))
                    {
                        int num = 100000;
                        while (num > 0 && !Gba.Arm7.Errored)
                        {
                            Gba.Step();
                            file.WriteLine(BuildEmuFullText());

                            if (Gba.Arm7.InstructionsRanInterrupt == Gba.Arm7.InstructionsRan)
                            {
                                file.WriteLine("---------------- INTERRUPT ----------------");
                            }

                            LogIndex++;
                            num--;
                        }
                    }
                }


                ImGui.Checkbox("Frame Step", ref FrameStep);
                // ImGui.Checkbox("Log HWIO Access", ref Gba.Mem.LogHWIOAccess);

                ImGui.NextColumn();
                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 150);

                bool Negative = Gba.Arm7.Negative;
                bool Zero = Gba.Arm7.Zero;
                bool Carry = Gba.Arm7.Carry;
                bool Overflow = Gba.Arm7.Overflow;
                bool Sticky = Gba.Arm7.Sticky;
                bool IRQDisable = Gba.Arm7.IRQDisable;
                bool FIQDisable = Gba.Arm7.FIQDisable;
                bool ThumbState = Gba.Arm7.ThumbState;

                ImGui.Checkbox("Negative", ref Negative);
                ImGui.Checkbox("Zero", ref Zero);
                ImGui.Checkbox("Carry", ref Carry);
                ImGui.Checkbox("Overflow", ref Overflow);
                ImGui.Checkbox("Sticky", ref Sticky);
                ImGui.Checkbox("IRQ Disable", ref IRQDisable);
                ImGui.Checkbox("FIQ Disable", ref FIQDisable);
                ImGui.Checkbox("Thumb State", ref ThumbState);

                ImGui.Text($"BIOS Reads: {Gba.Mem.BiosReads}");
                ImGui.Text($"EWRAM Reads: {Gba.Mem.EwramReads}");
                ImGui.Text($"IWRAM Reads: {Gba.Mem.IwramReads}");
                ImGui.Text($"ROM Reads: {Gba.Mem.RomReads}");
                ImGui.Text($"HWIO Reads: {Gba.Mem.HwioReads}");
                ImGui.Text($"Palette Reads: {Gba.Mem.PaletteReads}");
                ImGui.Text($"VRAM Reads: {Gba.Mem.VramReads}");
                ImGui.Text($"OAM Reads: {Gba.Mem.OamReads}");
                ImGui.Text("");
                ImGui.Text($"EWRAM Writes: {Gba.Mem.EwramWrites}");
                ImGui.Text($"IWRAM Writes: {Gba.Mem.IwramWrites}");
                ImGui.Text($"HWIO Writes: {Gba.Mem.HwioWrites}");
                ImGui.Text($"Palette Writes: {Gba.Mem.PaletteWrites}");
                ImGui.Text($"VRAM Writes: {Gba.Mem.VramWrites}");
                ImGui.Text($"OAM Writes: {Gba.Mem.OamWrites}");
                ImGui.Text("");
                bool ticked = Gba.HwControl.IME;
                ImGui.Checkbox("IME", ref ticked);

                ImGui.Checkbox("Log HWIO", ref Gba.Mem.LogHwioAccesses);

                ImGui.NextColumn();

                ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);

                ImGui.Text($"Total Frames: {Gba.Lcd.TotalFrames}");
                ImGui.Text($"Frame rate: {FrameRate}");
                ImGui.Text($"VCOUNT: {Gba.Lcd.VCount}");
                ImGui.Text($"Scanline Cycles: {Gba.Lcd.CycleCount}");

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

                ImGui.Text($"Timer 0 Counter: {Hex(Gba.Timers.T[0].CounterVal, 4)}");
                ImGui.Text($"Timer 1 Counter: {Hex(Gba.Timers.T[1].CounterVal, 4)}");
                ImGui.Text($"Timer 2 Counter: {Hex(Gba.Timers.T[2].CounterVal, 4)}");
                ImGui.Text($"Timer 3 Counter: {Hex(Gba.Timers.T[3].CounterVal, 4)}");
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
                ImGui.Text($"Wave Bank: {Gba.GbaAudio.GbAudio.wave_bank}");
                ImGui.Text($"Wave Dimension: {Gba.GbaAudio.GbAudio.wave_dimension}");
                ImGui.Text($"Wave Enabled: {Gba.GbaAudio.GbAudio.wave_enabled}");
                ImGui.Text($"Wave DAC Enabled: {Gba.GbaAudio.GbAudio.wave_dacEnabled}");
                ImGui.Text($"Wave Length Enable: {Gba.GbaAudio.GbAudio.wave_lengthEnable}");
                ImGui.Text($"Wave Length Counter: {Gba.GbaAudio.GbAudio.wave_lengthCounter}");
                ImGui.Text($"Wave Frequency Upper: {Gba.GbaAudio.GbAudio.wave_frequencyUpper}");
                ImGui.Text($"Wave Frequency Lower: {Gba.GbaAudio.GbAudio.wave_frequencyLower}");
                ImGui.Text($"Wave Volume: {Gba.GbaAudio.GbAudio.wave_volume}");
                ImGui.Text($"Wavetable 0: {string.Join(" ", Gba.GbaAudio.GbAudio.wave_waveTable0)}");
                ImGui.Text($"Wavetable 1: {string.Join(" ", Gba.GbaAudio.GbAudio.wave_waveTable1)}");

                ImGui.Checkbox("Enable PSGs", ref Gba.GbaAudio.EnablePsg);
                ImGui.Checkbox("Enable FIFOs", ref Gba.GbaAudio.EnableFifo);

                ImGui.Columns(1);
                ImGui.Separator();

                ImGui.Text("Palettes");

                byte[] image = new byte[16 * 16 * 3];

                for (int p = 0; p < 256; p++)
                {
                    int imgBase = p * 3;
                    image[imgBase + 0] = Gba.Lcd.ProcessedPalettes[p, 0];
                    image[imgBase + 1] = Gba.Lcd.ProcessedPalettes[p, 1];
                    image[imgBase + 2] = Gba.Lcd.ProcessedPalettes[p, 2];
                }

                int texId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, texId);

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
                    PixelFormat.Rgb,
                    PixelType.UnsignedByte,
                    image
                );

                // ImGui.Text($"Pointer: {texId}");
                ImGui.Image((IntPtr)texId, new System.Numerics.Vector2(16 * 8, 16 * 8));

                for (int p = 0; p < 256; p++)
                {
                    int imgBase = p * 3;
                    image[imgBase + 0] = Gba.Lcd.ProcessedPalettes[p + 256, 0];
                    image[imgBase + 1] = Gba.Lcd.ProcessedPalettes[p + 256, 1];
                    image[imgBase + 2] = Gba.Lcd.ProcessedPalettes[p + 256, 2];
                }

                texId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, texId);

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
                    PixelFormat.Rgb,
                    PixelType.UnsignedByte,
                    image
                );

                ImGui.SameLine(); ImGui.Image((IntPtr)texId, new System.Numerics.Vector2(16 * 8, 16 * 8));

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
                uint back = Gba.Arm7.ThumbState ? 16U : 32U;

                int rows = 32;
                uint tempBase = Gba.Arm7.R[15] - back;


                for (int i = 0; i < rows; i++)
                {
                    if (Gba.Arm7.ThumbState)
                    {
                        ushort val = Gba.Mem.ReadDebug16(tempBase);
                        String disasm = DisasmThumb(val);

                        String s = $"{Util.HexN(tempBase, 8)}: {HexN(val, 4)} {disasm}";
                        if (tempBase == Gba.Arm7.R[15] - (Gba.Arm7.Pipeline * 2))
                        {
                            ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), s);
                        }
                        else
                        {
                            ImGui.Text(s);
                        }
                        tempBase += 2;
                    }
                    else
                    {
                        uint val = Gba.Mem.ReadDebug32(tempBase);
                        String disasm = DisasmArm(val);

                        String s = $"{Util.HexN(tempBase, 8)}: {HexN(val, 8)} {disasm}";
                        if (tempBase == Gba.Arm7.R[15] - (Gba.Arm7.Pipeline * 4))
                        {
                            ImGui.TextColored(new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f), s);
                        }
                        else
                        {
                            ImGui.Text(s);
                        }
                        tempBase += 4;
                    }
                }
            }
        }

        public void DrawDisplay()
        {
            if (ImGui.Begin("Display"))
            {
                Random r = new Random();

                gbTexId = 0;

                // GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, gbTexId);
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgb,
                    240,
                    160,
                    0,
                    PixelFormat.Rgb,
                    PixelType.UnsignedByte,
                    Gba.Lcd.ScreenFront
                );

                ImGui.Image((IntPtr)gbTexId, new System.Numerics.Vector2(240 * 2, 160 * 2));
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
                new Register("DISPCNT - LCD Control", 0x4000000,
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
                new Register("DISPSTAT - General LCD Status", 0x4000004,
                    new RegisterField("V-Blank flag", 0),
                    new RegisterField("H-Blank flag", 1),
                    new RegisterField("V-Counter flag", 2),
                    new RegisterField("V-Blank IRQ Enable", 3),
                    new RegisterField("H-Blank IRQ Enable", 4),
                    new RegisterField("V-Counter IRQ Enable", 5),
                    new RegisterField("V-Count Setting", 8, 15)
            ));

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

            uint[] ieIfAddrs = { 0x4000200, 0x4000202 };
            String[] ieIfStrings = { "IE - Interrupt Enable", "IF - Interrupt Request" };
            for (uint r = 0; r < 2; r++)
            {
                Registers.Add(
                    new Register(ieIfStrings[r], ieIfAddrs[r],
                        new RegisterField("LCD V-Blank", 0),
                        new RegisterField("LCD H-Blank", 1),
                        new RegisterField("LCD V-Counter Match", 2),
                        new RegisterField("LCD Timer 0 Overflow", 3),
                        new RegisterField("LCD Timer 1 Overflow", 4),
                        new RegisterField("LCD Timer 2 Overflow", 5),
                        new RegisterField("LCD Timer 3 Overflow", 6),
                        new RegisterField("Serial", 7),
                        new RegisterField("DMA 0", 8),
                        new RegisterField("DMA 1", 9),
                        new RegisterField("DMA 2", 10),
                        new RegisterField("DMA 3", 11),
                        new RegisterField("Keypad", 13),
                        new RegisterField("Game Pak", 014
                )));
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
        string[] RomList;
        public void DrawRomSelector()
        {
            if (ImGui.Begin("ROMs"))
            {
                for (int i = 0; i < RomList.Length; i++)
                {
                    string s = RomList[i];
                    if (ImGui.Button($"Load##{s}"))
                    {
                        Console.WriteLine(s);
                        LoadRomFromPath(s);
                    }
                    ImGui.SameLine();
                    ImGui.Text(s);
                }
                ImGui.End();
            }
        }

        public void LoadRomFromPath(string path)
        {
            byte[] bios = Gba.Provider.Bios;
            byte[] rom = System.IO.File.ReadAllBytes(path);
            AudioCallback audioCallback = Gba.Provider.AudioCallback;

            Gba = new GBA(new GbaProvider(bios, rom, audioCallback));
        }
    }
}