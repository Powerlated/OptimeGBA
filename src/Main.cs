using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Drawing;
using static SDL2.SDL;
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
        static SDL_AudioSpec want, have;
        static uint AudioDevice;
        static ImGuiIOPtr ImGuiIO;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {
            using (Game game = new Game(1600, 900, "Optime GBA", AudioReady))
            {
                SetupSDL();

                game.Icon = new Icon("icon.ico", new Size(32, 32));

                // Run takes a double, which is how many frames per second it should strive to reach.
                // You can leave that out and it'll just update as fast as the hardware will allow it.
                game.VSync = OpenTK.VSyncMode.Adaptive;
                game.Run();
            }

            Environment.Exit(0);
        }

        public static void SetupSDL()
        {

            SDL_Init(SDL_INIT_AUDIO);

            want.channels = 2;
            want.freq = 32768;
            want.format = AUDIO_S16LSB;
            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);
        }

        static void AudioReady(short[] data)
        {
            int bytes = sizeof(short) * data.Length;

            IntPtr ptr = Marshal.AllocHGlobal(bytes);

            Marshal.Copy(data, 0, ptr, data.Length);

            // Console.WriteLine("Outputting samples to SDL");

            SDL_QueueAudio(AudioDevice, ptr, (uint)bytes);
            Marshal.FreeHGlobal(ptr);
        }

        public static uint GetAudioSamplesInQueue()
        {
            return SDL_GetQueuedAudioSize(AudioDevice) / sizeof(short);
        }
    }
}