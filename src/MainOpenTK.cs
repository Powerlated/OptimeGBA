using System;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Common.Input;

namespace OptimeGBAEmulator
{
    class MainOpenTK
    {
        static IntPtr window = IntPtr.Zero;
        static IntPtr glcontext;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {
            using (Window game = new Window(1600, 900, "Optime GBA"))
            {
                game.Icon = new WindowIcon();
                if (args.Length > 0)
                {
                    game.LoadRomFromPath(args[0]);
                }
                game.Run();
            }

            Environment.Exit(0);
        }
    }
}