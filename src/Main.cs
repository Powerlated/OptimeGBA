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
        static IntPtr window = IntPtr.Zero;
        static GBA Gba;
        static IntPtr glcontext;
        static SDL_AudioSpec want, have;
        static uint AudioDevice;
        static ImGuiIOPtr ImGuiIO;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {


            using (Game game = new Game(1600, 900, "Optime GBA"))
            {
                //Run takes a double, which is how many frames per second it should strive to reach.
                //You can leave that out and it'll just update as fast as the hardware will allow it.
                game.VSync = OpenTK.VSyncMode.On;
                game.Run(60.0, 60.0);
            }

            SetupSDL();
        }

        public static void SetupSDL()
        {

            SDL_Init(SDL_INIT_AUDIO);

            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);
        }

        static void AudioReady(float[] audioQueue)
        {

            int bytes = sizeof(float) * audioQueue.Length;

            IntPtr ptr = Marshal.AllocHGlobal(bytes + 2);

            Marshal.Copy(audioQueue, 0, ptr, audioQueue.Length);

            // Console.WriteLine("Outputting samples to SDL");

            SDL_QueueAudio(AudioDevice, ptr, (uint)bytes);
            Marshal.FreeHGlobal(ptr);
        }
    }
}