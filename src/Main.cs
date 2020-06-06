using System;
using System.Runtime.InteropServices;
using System.IO;
using static SDL2.SDL;
using OptimeGBA;

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
        static SDL_AudioSpec want, have;
        static uint AudioDevice;

        public static void Main(string[] args)
        {
            // Setup SDL -----------

            rect.x = 0;
            rect.y = 0;
            rect.w = width;
            rect.h = height;

            SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO);
            SDL_SetHint(SDL_HINT_RENDER_VSYNC, "1");
            SDL_CreateWindowAndRenderer(width, height, 0, out window, out Renderer);

            want.freq = 65536;
            want.format = AUDIO_F32SYS;
            want.channels = 2;
            want.samples = 4096;

            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);

            // Initialize Emulator ----

            Gba = new GBA(AudioReady);

            Console.WriteLine(Directory.GetCurrentDirectory());

            byte[] bios = System.IO.File.ReadAllBytes("roms/GBA.BIOS");
            bios.CopyTo(Gba.Mem.Bios, 0);

            byte[] rom = System.IO.File.ReadAllBytes("roms/Pokemon - Emerald Version (U).gba");
            rom.CopyTo(Gba.Mem.Rom, 0);

            // Enter Loop ---------------

            var exit = false;
            while (!exit)
            {
                SDL_Event e;
                SDL_PollEvent(out e);

                if (e.type == SDL_EventType.SDL_KEYDOWN)
                {
                    switch (e.key.keysym.sym)
                    {
                        case SDL_Keycode.SDLK_SPACE:

                            break;
                    }
                }

                // Render();


                if (e.type == SDL_EventType.SDL_QUIT)
                {
                    Quit();
                }

                RunEmulator();
            }
        }

        static void Quit()
        {
            SDL_DestroyWindow(window);
            SDL_Quit();

            System.Environment.Exit(0);
        }

        static void RunEmulator()
        {
            int max = 16777216 / 60;
            while (max > 0)
            {
                max -= (int)Gba.Run();
            }
        }

        static void AudioReady()
        {

            int bytes = sizeof(float) * Gba.Audio.AudioQueue.Length;

            IntPtr ptr = Marshal.AllocHGlobal(bytes + 2);

            Marshal.Copy(Gba.Audio.AudioQueue, 0, ptr, Gba.Audio.AudioQueue.Length);

            // Console.WriteLine("Outputting samples to SDL");

            SDL_QueueAudio(AudioDevice, ptr, (uint)bytes);
            Marshal.FreeHGlobal(ptr);
        }

        static void Render()
        {
            IntPtr ptr = Marshal.AllocHGlobal(image.Length);

            Random r = new Random();
            for (var i = 0; i < image.Length; i++)
            {
                image[i] = (byte)r.Next();
            }

            Marshal.Copy(image, 0, ptr, image.Length);
            var surface = SDL_CreateRGBSurfaceFrom(ptr, width, height, channels * 8, width * channels, 0x0000FF, 0x00FF00, 0xFF0000, 0);
            Marshal.FreeHGlobal(ptr);

            var texture = SDL_CreateTextureFromSurface(Renderer, surface);

            SDL_RenderClear(Renderer);
            SDL_RenderCopy(Renderer, texture, ref rect, ref rect);
            SDL_RenderPresent(Renderer);
            SDL_Delay(1000 / 60);
        }
    }
}