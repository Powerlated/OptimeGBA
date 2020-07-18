using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System;
using System.IO;
using ImGuiNET;
using ImGuiUtils;
using static Util;
using OptimeGBA;

namespace OptimeGBAEmulator
{
    public class Game : GameWindow
    {
        int gbTexId;
        int tsTexId;
        ImGuiController _controller;
        int VertexBufferObject;
        int VertexArrayObject;

        GBA Gba;

        public Game(int width, int height, string title, GBA gba) : base(width, height, GraphicsMode.Default, title)
        {
            Gba = gba;
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

            if (input.IsKeyDown(Key.ControlLeft) && input.IsKeyDown(Key.R))
            {

            }

            if (input.IsKeyDown(Key.ControlLeft) && input.IsKeyDown(Key.D))
            {

            }

            base.OnUpdateFrame(e);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            Random r = new Random();

            byte[] image = new byte[256 * 160 * 3];
            for (var i = 0; i < image.Length; i++)
            {
                image[i] = (byte)r.Next();
            }

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
                image
            );

            // GL.ActiveTexture(TextureUnit.Texture0);
            // GL.BindTexture(TextureTarget.Texture2D, tsTexId);
            // GL.TexImage2D(
            //     TextureTarget.Texture2D,
            //     0,
            //     PixelInternalFormat.Rgb,
            //     256,
            //     96,
            //     0,
            //     PixelFormat.Rgb,
            //     PixelType.UnsignedByte,
            //     tsPixels
            // );

            #region Draw Window
            GL.ClearColor(1f, 1f, 1f, 1f);
            GL.Clear(ClearBufferMask.StencilBufferBit | ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            _controller.Update(this, (float)e.Time);

            ImGui.Begin("Display");
            ImGui.Text($"Pointer: {gbTexId}");
            ImGui.Image((IntPtr)gbTexId, new System.Numerics.Vector2(240 * 2, 160 * 2));
            ImGui.End();

            ImGui.Begin("It's a tileset");
            ImGui.Text($"Pointer: {tsTexId}");
            ImGui.Image((IntPtr)tsTexId, new System.Numerics.Vector2(256 * 2, 96 * 2));
            ImGui.End();

            ImGui.Begin("Debug");
            ImGui.Text($"R0: {Hex(Gba.Arm7.R0, 8)}");
            ImGui.Text($"R1: {Hex(Gba.Arm7.R1, 8)}");
            ImGui.Text($"R2: {Hex(Gba.Arm7.R2, 8)}");
            ImGui.Text($"R3: {Hex(Gba.Arm7.R3, 8)}");
            ImGui.Text($"R4: {Hex(Gba.Arm7.R4, 8)}");
            ImGui.Text($"R5: {Hex(Gba.Arm7.R5, 8)}");
            ImGui.Text($"R6: {Hex(Gba.Arm7.R6, 8)}");
            ImGui.Text($"R7: {Hex(Gba.Arm7.R7, 8)}");
            ImGui.Text($"R8: {Hex(Gba.Arm7.R8, 8)}");
            ImGui.Text($"R9: {Hex(Gba.Arm7.R9, 8)}");
            ImGui.Text($"R10: {Hex(Gba.Arm7.R10, 8)}");
            ImGui.Text($"R11: {Hex(Gba.Arm7.R11, 8)}");
            ImGui.Text($"R12: {Hex(Gba.Arm7.R12, 8)}");
            ImGui.Text($"R13: {Hex(Gba.Arm7.R13, 8)}");
            ImGui.Text($"R14: {Hex(Gba.Arm7.R14, 8)}");
            ImGui.Text($"R15: {Hex(Gba.Arm7.R15, 8)}");
            ImGui.Text($"CPSR: {Hex(Gba.Arm7.GetCPSR(), 8)}");
            ImGui.Text($"Instruction: {Hex(Gba.Arm7.LastIns, 8)}");

            if (ImGui.Button("Step"))
            {
                Gba.Step();
            }

            ImGui.End();

            ImGui.Begin("Instruction Info");
            ImGui.Text(Gba.Arm7.Debug);
            ImGui.End();

            DrawMemoryViewer();

            _controller.Render();
            GL.Flush();

            Context.SwapBuffers();
            #endregion

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
}