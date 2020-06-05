using System;
using System.Runtime.InteropServices;
using static SDL2.SDL;

namespace DMSharpEmulator
{
    class DMSharpEmulator
    {
        static int width = 240;
        static int height = 160;
        static int channels = 3;

        static IntPtr window = IntPtr.Zero;

        public static void Main(string[] args)
        {

            var renderer = SDL_CreateRenderer(window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            SDL_Init(SDL_INIT_VIDEO | SDL_INIT_AUDIO);
            SDL_SetHint(SDL_HINT_RENDER_VSYNC, "1");
            SDL_CreateWindowAndRenderer(width, height, 0, out window, out renderer);

            var image = new byte[width * height * channels];
            var exit = false;
            while (!exit)
            {
                SDL_Event e;
                while (SDL_PollEvent(out e) != 0)
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

                    var texture = SDL_CreateTextureFromSurface(renderer, surface);

                    SDL_Rect rect = new SDL_Rect();
                    rect.x = 0;
                    rect.y = 0;
                    rect.w = width;
                    rect.h = height;

                    SDL_RenderClear(renderer);
                    SDL_RenderCopy(renderer, texture, ref rect, ref rect);
                    SDL_RenderPresent(renderer);
                    SDL_Delay(1000 / 60);

                    if (e.type == SDL_EventType.SDL_QUIT)
                    {
                        quit();
                    }
                }
            }
        }

        static void quit()
        {
            SDL_DestroyWindow(window);
            SDL_Quit();

            System.Environment.Exit(0);
        }
    }
}