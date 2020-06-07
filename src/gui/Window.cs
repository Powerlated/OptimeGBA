using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using System;
using System.IO;
using ImGuiNET;

namespace OptimeGBAEmulator
{
    public class Game : GameWindow
    {
        int gbTexId;
        int tsTexId;
        ImGuiController _controller;
        int VertexBufferObject;
        int VertexArrayObject;

        public Game(int width, int height, string title) : base(width, height, GraphicsMode.Default, title)
        {

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

            ImGui.Begin("--- Debug ---");
            ImGui.End();


            debugOam();

            ImGui.Begin("Display");
            ImGui.Text($"Pointer: {gbTexId}");
            ImGui.Image((IntPtr)gbTexId, new System.Numerics.Vector2(240 * 2, 160 * 2));
            ImGui.End();

            ImGui.Begin("It's a tileset");
            ImGui.Text($"Pointer: {tsTexId}");
            ImGui.Image((IntPtr)tsTexId, new System.Numerics.Vector2(256 * 2, 96 * 2));
            ImGui.End();

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

        void debugOam()
        {
            ImGui.Begin("--- Debug ---");
            // ImGui.Text("ScrollX: " + gb.gpu.scrX);
            // ImGui.Text("ScrollY: " + gb.gpu.scrY);
            // ImGui.Text("Debug");
            ImGui.End();
        }
    }
}