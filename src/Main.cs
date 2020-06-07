using System;
using System.Runtime.InteropServices;
using System.IO;
using static SDL2.SDL;
using OpenGL;
using OptimeGBA;
using System.Threading;
using ImGuiNET;

namespace OptimeGBAEmulator
{
    class OptimeGBAEmulator
    {
        static int width = 240;
        static int height = 160;
        static int channels = 3;
        static byte[] image = new byte[width * height * channels];
        static IntPtr Renderer;
        static SDL_Rect rect = new SDL_Rect();

        static IntPtr window = IntPtr.Zero; static GBA Gba;
        static IntPtr glcontext;
        static SDL_AudioSpec want, have;
        static uint AudioDevice;
        static ImGuiIOPtr ImGuiIO;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {

            new Thread(() =>
            {
                using (Game game = new Game(1600, 900, "Optime GBA"))
                {
                    //Run takes a double, which is how many frames per second it should strive to reach.
                    //You can leave that out and it'll just update as fast as the hardware will allow it.
                    game.VSync = OpenTK.VSyncMode.On;
                    game.Run(60.0, 60.0);
                }
            }).Start();

        }
    }
}