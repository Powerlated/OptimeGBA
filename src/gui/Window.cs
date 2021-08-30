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

namespace OptimeGBAEmulator
{
    public unsafe class Window : GameWindow
    {
        public WindowGba WindowGba;
        public WindowNds WindowNds;

        public static CapstoneArmDisassembler ArmDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Arm);
        public static CapstoneArmDisassembler ThumbDisassembler = CapstoneArmDisassembler.CreateArmDisassembler(ArmDisassembleMode.Thumb);

        ImGuiController _controller;
        int VertexBufferObject;
        int VertexArrayObject;

        static bool FrameNow = false;
        bool NdsMode = false;
        bool RomLoaded = false;

        public bool RunEmulator;

        public Window(int width, int height, string title) : base(GameWindowSettings.Default, new NativeWindowSettings() { Size = new Vector2i(width, height), Title = title })
        {
            WindowGba = new WindowGba(this);
            WindowNds = new WindowNds(this);

            SearchForRoms();
        }

        public void SearchForRoms()
        {
            var gbaRomList = Directory.GetFiles("roms", "*.nds");
            var ndsRomList = Directory.GetFiles("roms", "*.gba");
            RomList = gbaRomList.Concat(ndsRomList).ToArray();
        }

        string[] RomList;
        public void DrawRomSelector()
        {
            if (ImGui.Begin("ROMs"))
            {
                if (ImGui.Button("Refresh"))
                {
                    SearchForRoms();
                }
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
            byte[] rom = System.IO.File.ReadAllBytes(path);
            string savPath = path.Substring(0, path.Length - 3) + "sav";
            byte[] sav = new byte[0];
            if (System.IO.File.Exists(savPath))
            {
                Console.WriteLine(".sav exists, loading");
                try
                {
                    sav = System.IO.File.ReadAllBytes(savPath);
                }
                catch
                {
                    Console.WriteLine("Failed to load .sav file!");
                }
            }
            else
            {
                Console.WriteLine(".sav not available");
            }


            NdsMode = path.Substring(path.Length - 3).ToLower() == "nds";

            if (NdsMode)
            {
                WindowNds.OnLoad();
                WindowNds.LoadRomAndSave(rom, sav, savPath);
            }
            else
            {
                WindowGba.OnLoad();
                WindowGba.LoadRomAndSave(rom, sav, savPath);
            }

            RomLoaded = true;
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            _controller.WindowResized(ClientSize.X, ClientSize.Y);
            GL.Viewport(0, 0, ClientSize.X, ClientSize.Y);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            _controller.MouseScroll(e.Offset);
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            VertexArrayObject = GL.GenVertexArray();
            GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);
            VertexBufferObject = GL.GenBuffer();

            GL.Enable(EnableCap.Texture2D);
            // Disable texture filtering
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMagFilter.Nearest);
            _controller = new ImGuiController(ClientSize.X, ClientSize.Y);

            VSync = VSyncMode.Off;
            UpdateFrequency = 59.7275;

            WindowGba.OnLoad();
            WindowNds.OnLoad();

            FileDrop += (FileDropEventArgs args) =>
            {
                LoadRomFromPath(args.FileNames[0]);
            };
        }

        protected override void OnTextInput(TextInputEventArgs args)
        {
            ImGui.GetIO().AddInputCharacter((byte)args.Unicode);
            ImGui.GetIO().KeysDown[(byte)args.Unicode] = true;
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs args)
        {
            // keycode can be negative sometimes, so filter out by casting to uint
            if ((uint)args.Key < 512)
                ImGui.GetIO().KeysDown[(int)args.Key] = true;
        }

        protected override void OnKeyUp(KeyboardKeyEventArgs args)
        {
            if ((uint)args.Key < 512)
                ImGui.GetIO().KeysDown[(int)args.Key] = false;
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            base.OnUpdateFrame(e);

            if (NdsMode)
            {
                WindowNds.OnUpdateFrame(e);
            }
            else
            {
                WindowGba.OnUpdateFrame(e);
            }
        }

        const int FrameCycles = 70224 * 4;
        const int ScanlineCycles = 1232;

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);
            _controller.Update(this, (float)e.Time);

            DrawRomSelector();

            if (RomLoaded)
            {
                if (NdsMode)
                {
                    WindowNds.OnRenderFrame(e);
                }
                else
                {
                    WindowGba.OnRenderFrame(e);
                }
            }

            GL.ClearColor(1f, 1f, 1f, 1f);
            GL.Clear(ClearBufferMask.StencilBufferBit | ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _controller.Render();
            GL.Flush();

            Context.SwapBuffers();
        }

        public static void drawDisassembly(Arm7 cpu)
        {
            uint back = cpu.ThumbState ? 32U : 64U;

            int rows = 32;
            uint tempBase = cpu.R[15] - back;

            // Forcibly align addresses to avoid race condition
            for (int i = 0; i < rows; i++)
            {
                if (cpu.ThumbState)
                {
                    ushort val = cpu.Mem.ReadDebug16(tempBase & ~1U);
                    String disasm = disasmThumb(val);

                    String s = $"{Util.HexN(tempBase, 8)}: {HexN(val, 4)} {disasm}";
                    if (tempBase == cpu.R[15] - 4)
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
                    uint val = cpu.Mem.ReadDebug32(tempBase & ~3U);
                    String disasm = disasmArm(val);

                    String s = $"{Util.HexN(tempBase, 8)}: {HexN(val, 8)} {disasm}";
                    if (tempBase == cpu.R[15] - 8)
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

        public static String disasmThumb(ushort opcode)
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

        public static String disasmArm(uint opcode)
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

        public static void displayCheckbox(string label, bool v)
        {
            ImGui.Checkbox(label, ref v);
        }

        static StringBuilder b = new StringBuilder(1000);
        public static String BuildEmuFullText(Arm7 arm7)
        {
            String disasm = arm7.ThumbState ? disasmThumb((ushort)GetCurrentInstr(arm7)) : disasmArm(GetCurrentInstr(arm7));
            b.Clear();

            for (int i = 0; i < 15; i++)
            {
                b.Append(HexN(arm7.R[i], 8)).Append(" ");
            }
            b.Append(HexN(arm7.GetCurrentInstrAddr(), 8)).Append(" ");
            b.Append("cpsr: ");
            b.Append(HexN(arm7.GetCPSR(), 8));
            b.Append(" | ");

            if (arm7.ThumbState)
            {
                b.Append("    ");
                b.Append(HexN(GetCurrentInstr(arm7), 4));
            }
            else
            {
                b.Append(HexN(GetCurrentInstr(arm7), 8));
            }
            b.Append(": ").Append(disasm);
            return b.ToString();
        }

        public static uint GetCurrentInstr(Arm7 cpu)
        {
            if (cpu.ThumbState)
            {
                return cpu.Mem.Read16(cpu.GetCurrentInstrAddr());
            }
            else
            {
                return cpu.Mem.Read32(cpu.GetCurrentInstrAddr());
            }
        }


    }
}