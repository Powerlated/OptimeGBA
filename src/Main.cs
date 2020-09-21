using System;
using System.IO;
using System.Drawing;
using OpenGL;
using OptimeGBA;
using System.Threading;
using ImGuiNET;
using static Util;

namespace OptimeGBAEmulator
{
    class OptimeGBAEmulator
    {
        static IntPtr window = IntPtr.Zero;
        static IntPtr glcontext;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {
            using (Game game = new Game(1600, 900, "Optime GBA"))
            {
                game.Icon = new Icon("icon.ico", new Size(32, 32));

                // Run takes a double, which is how many frames per second it should strive to reach.
                // You can leave that out and it'll just update as fast as the hardware will allow it.
                game.VSync = OpenTK.VSyncMode.On;
                game.Run();
            }

            Environment.Exit(0);
        }
    }
}