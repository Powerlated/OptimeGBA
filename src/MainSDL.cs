using static SDL2.SDL;
using System;
using System.Runtime.InteropServices;
using OptimeGBA;

namespace OptimeGBAEmulator
{
    public unsafe class MainSDL
    {
        const uint AUDIO_SAMPLE_THRESHOLD = 1024;
        const uint AUDIO_SAMPLE_FULL_THRESHOLD = 1024;
        const int SAMPLES_PER_CALLBACK = 32;

        static SDL_AudioSpec want, have;
        static uint AudioDevice;

        static IntPtr Window;
        static IntPtr Renderer;

        static GBA Gba;

        static bool Sync = true;

        public static void Main(string[] args)
        {
            SDL_Init(SDL_INIT_AUDIO | SDL_INIT_VIDEO);

            if (args.Length == 0)
            {
                Log("Please provide the path to a ROM file.");
                return;
            }

            Window = SDL_CreateWindow("Optime GBA", 0, 0, 960, 640, SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
            SDL_SetWindowPosition(Window, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED);
            Renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED);

            SDL_SetWindowMinimumSize(Window, LCD.WIDTH, LCD.HEIGHT);

            // SDL_GL_SetSwapInterval()

            want.channels = 2;
            want.freq = 32768;
            want.samples = SAMPLES_PER_CALLBACK;
            want.format = AUDIO_S16LSB;
            // want.callback = NeedMoreAudioCallback;
            AudioDevice = SDL_OpenAudioDevice(null, 0, ref want, out have, (int)SDL_AUDIO_ALLOW_FORMAT_CHANGE);
            SDL_PauseAudioDevice(AudioDevice, 0);

            string romPath = args[0];
            byte[] rom;
            if (!System.IO.File.Exists(romPath))
            {
                Log("The ROM file you provided does not exist.");
                return;
            }
            else
            {
                try
                {
                    rom = System.IO.File.ReadAllBytes(romPath);
                }
                catch
                {
                    Log("The ROM file you provided exists, but there was an issue loading it.");
                    return;
                }
            }

            byte[] bios;
            const string biosPath = "gba_bios.bin";
            if (!System.IO.File.Exists(biosPath))
            {
                SdlMessage("Error", "Please place a valid GBA BIOS in the same directory as OptimeGBA.exe named \"gba_bios.bin\"");
                return;
            }
            else
            {
                try
                {
                    bios = System.IO.File.ReadAllBytes(biosPath);
                }
                catch
                {
                    Log("A GBA BIOS was provided, but there was an issue loading it.");
                    return;
                }
            }

            string savPath = romPath.Substring(0, romPath.Length - 3) + "sav";
            byte[] sav = new byte[0];
            if (System.IO.File.Exists(savPath))
            {
                Log(".sav exists, loading");
                try
                {
                    sav = System.IO.File.ReadAllBytes(savPath);
                }
                catch
                {
                    Log("Failed to load .sav file!");
                }
            }
            else
            {
                Log(".sav not available");
            }

            IntPtr texture = SDL_CreateTexture(Renderer, SDL_PIXELFORMAT_ABGR8888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, LCD.WIDTH, LCD.HEIGHT);

            var provider = new GbaProvider(bios, rom, savPath, AudioReady);
            provider.BootBios = true;
            Gba = new GBA(provider);

            Gba.Mem.SaveProvider.LoadSave(sav);

            bool quit = false;
            double nextFrameAt = 0;
            double fpsEvalTimer = 0;

            double getTime()
            {
                return (double)SDL_GetPerformanceCounter() / (double)SDL_GetPerformanceFrequency();
            }

            void resetTimers()
            {
                var time = getTime();
                nextFrameAt = time;
                fpsEvalTimer = time;
            }
            resetTimers();

            while (!quit)
            {
                SDL_Event evt;
                while (SDL_PollEvent(out evt) != 0)
                {
                    switch (evt.type)
                    {
                        case SDL_EventType.SDL_QUIT:
                            quit = true;
                            break;
                        case SDL_EventType.SDL_KEYUP:
                        case SDL_EventType.SDL_KEYDOWN:
                            KeyEvent(evt.key);
                            break;
                    }
                }

                if (ResetDue)
                {
                    ResetDue = false;
                    byte[] save = Gba.Mem.SaveProvider.GetSave();
                    GbaProvider p = Gba.Provider;
                    Gba = new GBA(p);
                    Gba.Mem.SaveProvider.LoadSave(save);

                    nextFrameAt = getTime();
                }

                double currentSec = getTime();
                if (Sync)
                {

                    if (currentSec >= nextFrameAt)
                    {
                        nextFrameAt += 1D / (4194304D / 70224D);

                        RunFrame();
                    }
                }
                else
                {
                    nextFrameAt = getTime();
                    RunFrame();
                }

                if (currentSec >= fpsEvalTimer)
                {

                    double diff = currentSec - fpsEvalTimer + 1;
                    double frames = CyclesRan / 280896;
                    CyclesRan = 0;

                    double fps = frames / diff;
                    // Use Math.Floor to truncate to 2 decimal places
                    SDL_SetWindowTitle(Window, "Optime GBA - " + Math.Floor(fps * 100) / 100 + " fps");

                    fpsEvalTimer += 1;
                }

                fixed (byte* pixels = &Gba.Lcd.ScreenFront[0])
                {
                    SDL_UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixels, LCD.WIDTH * LCD.BYTES_PER_PIXEL);
                }

                SDL_Rect dest = new SDL_Rect();
                SDL_GetWindowSize(Window, out int w, out int h);
                double ratio = Math.Min((double)h / (double)LCD.HEIGHT, (double)w / (double)LCD.WIDTH);
                int fillWidth = (int)(ratio * LCD.WIDTH);
                int fillHeight = (int)(ratio * LCD.HEIGHT);
                dest.w = fillWidth;
                dest.h = fillHeight;
                dest.x = (int)((w - fillWidth) / 2);
                dest.y = (int)((h - fillHeight) / 2);

                SDL_RenderClear(Renderer);
                SDL_RenderCopy(Renderer, texture, IntPtr.Zero, ref dest);
                SDL_RenderPresent(Renderer);

                if (Gba.Mem.SaveProvider.Dirty)
                {
                    Gba.Mem.SaveProvider.Dirty = false;
                    try
                    {
                        System.IO.File.WriteAllBytesAsync(Gba.Provider.SavPath, Gba.Mem.SaveProvider.GetSave());
                    }
                    catch
                    {
                        Console.WriteLine("Failed to write .sav file!");
                    }
                }
            }

            SDL_DestroyRenderer(Renderer);
            SDL_DestroyWindow(Window);
            SDL_AudioQuit();
            SDL_VideoQuit();
            SDL_Quit();
            Environment.Exit(0);
        }

        public static void SdlMessage(string title, string msg)
        {
            SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION, title, msg, Window);
        }

        static bool LCtrl;
        static bool Tab;
        static bool Space;

        static bool ResetDue;

        public static void KeyEvent(SDL_KeyboardEvent kb)
        {
            bool pressed = kb.state == SDL_PRESSED;
            switch (kb.keysym.sym)
            {
                case SDL_Keycode.SDLK_z:
                    Gba.Keypad.B = pressed;
                    break;
                case SDL_Keycode.SDLK_x:
                    Gba.Keypad.A = pressed;
                    break;

                case SDL_Keycode.SDLK_BACKSPACE:
                    Gba.Keypad.Select = pressed;
                    break;
                case SDL_Keycode.SDLK_KP_ENTER:
                    Gba.Keypad.Start = pressed;
                    break;

                case SDL_Keycode.SDLK_LEFT:
                    Gba.Keypad.Left = pressed;
                    break;
                case SDL_Keycode.SDLK_RIGHT:
                    Gba.Keypad.Right = pressed;
                    break;
                case SDL_Keycode.SDLK_UP:
                    Gba.Keypad.Up = pressed;
                    break;
                case SDL_Keycode.SDLK_DOWN:
                    Gba.Keypad.Down = pressed;
                    break;

                case SDL_Keycode.SDLK_SPACE:
                    Space = pressed;
                    Sync = !(Space || Tab);
                    break;
                case SDL_Keycode.SDLK_TAB:
                    Tab = pressed;
                    Sync = !(Space || Tab);
                    break;

                case SDL_Keycode.SDLK_LCTRL:
                    LCtrl = pressed;
                    break;

                case SDL_Keycode.SDLK_r:
                    if (LCtrl)
                    {
                        ResetDue = true;
                    }
                    break;
            }
        }

        const int FrameCycles = 70224 * 4;
        static int CyclesLeft;
        static long CyclesRan;
        public static void RunFrame()
        {
            CyclesLeft += FrameCycles;
            CyclesRan += FrameCycles;
            while (CyclesLeft > 0)
            {
                CyclesLeft -= (int)Gba.Step();
            }
        }

        public static void RunCycles(int cycles)
        {
            CyclesLeft += cycles;
            CyclesRan += cycles;
            while (CyclesLeft > 0)
            {
                CyclesLeft -= (int)Gba.Step();
            }
        }

        public static void Log(string msg)
        {
            Console.WriteLine("[Optime GBA] " + msg);
        }

        static IntPtr AudioTempBufPtr = Marshal.AllocHGlobal(16384);
        static void AudioReady(short[] data)
        {
            // Don't queue audio if too much is in buffer
            if (Sync || GetAudioSamplesInQueue() < AUDIO_SAMPLE_FULL_THRESHOLD)
            {
                int bytes = sizeof(short) * data.Length;

                Marshal.Copy(data, 0, AudioTempBufPtr, data.Length);

                // Console.WriteLine("Outputting samples to SDL");

                SDL_QueueAudio(AudioDevice, AudioTempBufPtr, (uint)bytes);
            }
        }

        public static uint GetAudioSamplesInQueue()
        {
            return SDL_GetQueuedAudioSize(AudioDevice) / sizeof(short);
        }
    }
}
