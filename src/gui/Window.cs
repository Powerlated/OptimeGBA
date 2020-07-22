using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System;
using System.IO;
using ImGuiNET;
using ImGuiUtils;
using static Util;
using System.Collections.Generic;
using System.Numerics;
using OptimeGBA;
using Gee.External.Capstone.Arm;

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

        CapstoneArmDisassembler ArmDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
        CapstoneArmDisassembler ThumbDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Thumb);

        bool FrameStep = false;

        public Game(int width, int height, string title, GBA gba) : base(width, height, GraphicsMode.Default, title)
        {
            Gba = gba;

            string file = System.IO.File.ReadAllText("./mgba-fuzzer-log.txt");
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
            KeyboardState input = Keyboard.GetState();


            // if (input.IsKeyDown(Key.Escape))
            // {
            //     Exit();
            // }

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

            base.OnUpdateFrame(e);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            if (FrameStep)
            {
                int num = 20000;
                while (num > 0 && !Gba.Arm7.Errored)
                {
                    Gba.Step();
                    num--;
                }
            }

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
            // DrawMemoryViewer();

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

        static int MemoryViewerInit = 1;
        int MemoryViewerCurrent = MemoryViewerInit;
        uint MemoryViewerCurrentAddr = baseAddrs[MemoryViewerInit];
        uint MemoryViewerHoverAddr = 0;
        uint MemoryViewerHoverVal = 0;
        bool MemoryViewerHover = false;
        byte[] MemoryViewerGoToAddr = new byte[16];

        static String[] baseNames = {
                    "BIOS",
                    "ROM",
                };

        static uint[] baseAddrs = {
                0x00000000,
                0x08000000
            };

        public void DrawMemoryViewer()
        {

            int rows = 64;
            int cols = 16;

            ImGui.Begin("Memory Viewer");

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

        public String BuildLogText()
        {
            String logText;
            if (LogIndex < Log.Length)
            {
                logText = Log[LogIndex].Substring(0, 135) + Log[LogIndex].Substring(144, 14) + $" {LogIndex + 1}";
            }
            else
            {
                logText = "<log past end>";
            }

            return logText;
        }

        public String BuildEmuText()
        {
            String text = "";
            text += $"{HexN(Gba.Arm7.R0, 8)} ";
            text += $"{HexN(Gba.Arm7.R1, 8)} ";
            text += $"{HexN(Gba.Arm7.R2, 8)} ";
            text += $"{HexN(Gba.Arm7.R3, 8)} ";
            text += $"{HexN(Gba.Arm7.R4, 8)} ";
            text += $"{HexN(Gba.Arm7.R5, 8)} ";
            text += $"{HexN(Gba.Arm7.R6, 8)} ";
            text += $"{HexN(Gba.Arm7.R7, 8)} ";
            text += $"{HexN(Gba.Arm7.R8, 8)} ";
            text += $"{HexN(Gba.Arm7.R9, 8)} ";
            text += $"{HexN(Gba.Arm7.R10, 8)} ";
            text += $"{HexN(Gba.Arm7.R11, 8)} ";
            text += $"{HexN(Gba.Arm7.R12, 8)} ";
            text += $"{HexN(Gba.Arm7.R13, 8)} ";
            text += $"{HexN(Gba.Arm7.R14, 8)} ";
            text += $"{HexN(Gba.Arm7.R15, 8)} ";
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

            String text = "";
            text += $"{HexN(Gba.Arm7.R0, 8)} ";
            text += $"{HexN(Gba.Arm7.R1, 8)} ";
            text += $"{HexN(Gba.Arm7.R2, 8)} ";
            text += $"{HexN(Gba.Arm7.R3, 8)} ";
            text += $"{HexN(Gba.Arm7.R4, 8)} ";
            text += $"{HexN(Gba.Arm7.R5, 8)} ";
            text += $"{HexN(Gba.Arm7.R6, 8)} ";
            text += $"{HexN(Gba.Arm7.R7, 8)} ";
            text += $"{HexN(Gba.Arm7.R8, 8)} ";
            text += $"{HexN(Gba.Arm7.R9, 8)} ";
            text += $"{HexN(Gba.Arm7.R10, 8)} ";
            text += $"{HexN(Gba.Arm7.R11, 8)} ";
            text += $"{HexN(Gba.Arm7.R12, 8)} ";
            text += $"{HexN(Gba.Arm7.R13, 8)} ";
            text += $"{HexN(Gba.Arm7.R14, 8)} ";
            text += $"{HexN(Gba.Arm7.R15, 8)} ";
            text += $"cpsr: {HexN(Gba.Arm7.GetCPSR(), 8)} | ";
            text += $"{(Gba.Arm7.ThumbState ? "    " + HexN(Gba.Arm7.LastIns, 4) : HexN(Gba.Arm7.LastIns, 8))}: {disasm}";
            // text += $"> {LogIndex + 1}";
            return text;
        }


        int DebugStepFor = 0;
        byte[] text = new byte[4];

        public void DrawInstrInfo()
        {
            String logText = BuildLogText();
            String emuText = BuildEmuText();

            ImGui.Begin("Instruction Info");
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


        public void DrawDebug()
        {
            ImGui.Begin("Debug");

            ImGui.BeginGroup();

            ImGui.Columns(3);

            ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 200);
            ImGui.Text($"R0:  {Hex(Gba.Arm7.R0, 8)}");
            ImGui.Text($"R1:  {Hex(Gba.Arm7.R1, 8)}");
            ImGui.Text($"R2:  {Hex(Gba.Arm7.R2, 8)}");
            ImGui.Text($"R3:  {Hex(Gba.Arm7.R3, 8)}");
            ImGui.Text($"R4:  {Hex(Gba.Arm7.R4, 8)}");
            ImGui.Text($"R5:  {Hex(Gba.Arm7.R5, 8)}");
            ImGui.Text($"R6:  {Hex(Gba.Arm7.R6, 8)}");
            ImGui.Text($"R7:  {Hex(Gba.Arm7.R7, 8)}");
            ImGui.Text($"R8:  {Hex(Gba.Arm7.R8, 8)}");
            ImGui.Text($"R9:  {Hex(Gba.Arm7.R9, 8)}");
            ImGui.Text($"R10: {Hex(Gba.Arm7.R10, 8)}");
            ImGui.Text($"R11: {Hex(Gba.Arm7.R11, 8)}");
            ImGui.Text($"R12: {Hex(Gba.Arm7.R12, 8)}");
            ImGui.Text($"R13: {Hex(Gba.Arm7.R13, 8)}");
            ImGui.Text($"R14: {Hex(Gba.Arm7.R14, 8)}");
            ImGui.Text($"R15: {Hex(Gba.Arm7.R15, 8)}");
            ImGui.Text($"CPSR: {Hex(Gba.Arm7.GetCPSR(), 8)}");
            ImGui.Text($"Instruction: {Hex(Gba.Arm7.LastIns, Gba.Arm7.ThumbState ? 4 : 8)}");
            ImGui.Text($"Disasm: {(Gba.Arm7.ThumbState ? DisasmThumb((ushort)Gba.Arm7.LastIns) : DisasmArm(Gba.Arm7.LastIns))}");

            ImGui.Text($"");

            if (ImGui.Button("Un-error"))
            {
                Gba.Arm7.Errored = false;
            }
            if (ImGui.Button("Step"))
            {
                Gba.Step();
                LogIndex++;
            }
            if (ImGui.Button("Step Until Error"))
            {
                bool exit = false;
                while (!Gba.Arm7.Errored && !exit)
                {

                    Gba.Step();
                    LogIndex++;

                    if (BuildEmuText() != BuildLogText())
                    {
                        exit = true;
                    }
                }
            }

            ImGui.InputText("", text, 4);
            ImGui.InputInt("", ref DebugStepFor);
            if (ImGui.Button("Step For"))
            {
                using (StreamWriter file = new StreamWriter("log.txt"))
                {
                    int num = DebugStepFor;
                    while (num > 0 && !Gba.Arm7.Errored)
                    {
                        Gba.Step();

                        // file.WriteLine(BuildEmuFullText());

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

                        LogIndex++;
                        num--;
                    }
                }
            }

            ImGui.Checkbox("Frame Step", ref FrameStep);

            ImGui.NextColumn();
            ImGui.SetColumnWidth(ImGui.GetColumnIndex(), 150);

            bool negative = Gba.Arm7.Negative;
            bool zero = Gba.Arm7.Zero;
            bool carry = Gba.Arm7.Carry;
            bool overflow = Gba.Arm7.Overflow;
            bool sticky = Gba.Arm7.Sticky;
            bool irqDisable = Gba.Arm7.IRQDisable;
            bool fiqDisable = Gba.Arm7.FIQDisable;
            bool thumbState = Gba.Arm7.ThumbState;

            ImGui.Checkbox("Negative", ref negative);
            ImGui.Checkbox("Zero", ref zero);
            ImGui.Checkbox("Carry", ref carry);
            ImGui.Checkbox("Overflow", ref overflow);
            ImGui.Checkbox("Sticky", ref sticky);
            ImGui.Checkbox("IRQ Disable", ref irqDisable);
            ImGui.Checkbox("FIQ Disable", ref fiqDisable);
            ImGui.Checkbox("Thumb State", ref thumbState);

            ImGui.Text($"BIOS Reads: {Gba.Mem.BiosReads}");
            ImGui.Text($"EWRAM Reads: {Gba.Mem.EwramReads}");
            ImGui.Text($"IWRAM Reads: {Gba.Mem.IwramReads}");
            ImGui.Text($"ROM Reads: {Gba.Mem.RomReads}");
            ImGui.Text($"Palette Reads: {Gba.Mem.PaletteReads}");
            ImGui.Text($"VRAM Reads: {Gba.Mem.VramReads}");
            ImGui.Text($"OAM Reads: {Gba.Mem.OamReads}");

            ImGui.Text("");

            ImGui.Text($"EWRAM Writes: {Gba.Mem.EwramWrites}");
            ImGui.Text($"IWRAM Writes: {Gba.Mem.IwramWrites}");
            ImGui.Text($"Palette Writes: {Gba.Mem.PaletteWrites}");
            ImGui.Text($"VRAM Writes: {Gba.Mem.VramWrites}");
            ImGui.Text($"OAM Writes: {Gba.Mem.OamWrites}");

            ImGui.EndGroup();

            ImGui.NextColumn();

            ImGui.Text($"Total Frames: {Gba.Lcd.TotalFrames}");
            ImGui.Text($"VCOUNT: {Gba.Lcd.VCount}");
            ImGui.Text($"Scanline Cycles: {Gba.Lcd.CycleCount}");


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
            ImGui.Image((IntPtr)texId, new System.Numerics.Vector2(16 * 16, 16 * 16));

            ImGui.End();
        }

        public void DrawInstrViewer()
        {
            uint back = Gba.Arm7.ThumbState ? 16U : 32U;

            int rows = 32;
            uint tempBase = Gba.Arm7.R15 - back;

            ImGui.Begin("Instruction Viewer");
            for (int i = 0; i < rows; i++)
            {
                if (Gba.Arm7.ThumbState)
                {
                    ushort val = Gba.Mem.ReadDebug16(tempBase);
                    String disasm = DisasmThumb(val);

                    String s = $"{Util.HexN(tempBase, 8)}: {HexN(val, 4)} {disasm}";
                    if (tempBase == Gba.Arm7.R15 - 2)
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
                    if (tempBase == Gba.Arm7.R15 - 4)
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

        public void DrawDisplay()
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
                Gba.Lcd.Screen
            );


            ImGui.Begin("Display");
            ImGui.Image((IntPtr)gbTexId, new System.Numerics.Vector2(240 * 2, 160 * 2));
            ImGui.End();
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
                    new RegisterField[] {
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
                        new RegisterField("OBJ Window Display Flag", 15),
                    }
                ));

            Registers.Add(
                new Register("DISPSTAT - General LCD Status", 0x4000004,
                    new RegisterField[] {
                        new RegisterField("V-Blank flag", 0),
                        new RegisterField("H-Blank flag", 1),
                        new RegisterField("V-Counter flag", 2),
                        new RegisterField("V-Blank IRQ Enable", 3),
                        new RegisterField("H-Blank IRQ Enable", 4),
                        new RegisterField("V-Counter IRQ Enable", 5),
                        new RegisterField("V-Count Setting", 8, 15),
               }
            ));

            Registers.Add(
                new Register("KEYINPUT - Key Status", 0x4000130,
                    new RegisterField[] {
                        new RegisterField("Button A", 0),
                        new RegisterField("Button B", 1),
                        new RegisterField("Select", 2),
                        new RegisterField("Start", 3),
                        new RegisterField("Right", 4),
                        new RegisterField("Left", 5),
                        new RegisterField("Up", 6),
                        new RegisterField("Down", 7),
                        new RegisterField("Button R", 8),
                        new RegisterField("Button L", 9),
               }
            ));

            RegViewerSelected = Registers[0];
        }

        Register RegViewerSelected;

        public void DrawRegViewer()
        {
            ImGui.Begin("Register Viewer");
            if (ImGui.BeginCombo("", RegViewerSelected.Name))
            {
                foreach (Register r in Registers)
                {
                    bool selected = r == RegViewerSelected;
                    if (ImGui.Selectable(r.Name, selected))
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
}