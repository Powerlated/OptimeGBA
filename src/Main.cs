using System;
using System.Runtime.InteropServices;
using System.IO;
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
        static GBA Gba;

        static private System.Numerics.Vector2 _scaleFactor = System.Numerics.Vector2.One;

        public static void Main(string[] args)
        {
            GbaRomProvider provider = new GbaRomProvider();
            
            byte[] bios = System.IO.File.ReadAllBytes("roms/GBA.BIOS");
            bios.CopyTo(provider.Bios, 0);

            // byte[] rom = System.IO.File.ReadAllBytes("roms/fuzzarm.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/fuzzarm-262144.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/armwrestler-gba-fixed.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/arm.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/tonc/swi_demo.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/tonc/swi_vsync.gba");
            // byte[] rom = System.IO.File.ReadAllBytes("roms/Pokemon Pinball - Ruby & Sapphire (USA).gba");
            byte[] rom = System.IO.File.ReadAllBytes("roms/Pokemon - Emerald Version (U).gba");
            rom.CopyTo(provider.Rom, 0);

            Gba = new GBA(provider, AudioReady);

            using (Game game = new Game(1600, 900, "Optime GBA", Gba))
            {
                //Run takes a double, which is how many frames per second it should strive to reach.
                //You can leave that out and it'll just update as fast as the hardware will allow it.
                game.VSync = OpenTK.VSyncMode.On;
                game.Run(60.0, 0.0);
            }

            SetupSDL();
        }

        public static void SetupSDL()
        {

            SDL_Init(SDL_INIT_AUDIO);

            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);
        }

        static void AudioReady()
        {

            // int bytes = sizeof(float) * Gba.Audio.AudioQueue.Length;

            // IntPtr ptr = Marshal.AllocHGlobal(bytes);

            // Marshal.Copy(Gba.Audio.AudioQueue, 0, ptr, Gba.Audio.AudioQueue.Length);

            // Console.WriteLine("Outputting samples to SDL");

            // SDL_QueueAudio(AudioDevice, ptr, (uint)bytes);
            // Marshal.FreeHGlobal(ptr);
        }
    }
}