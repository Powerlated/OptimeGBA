using static SDL2.SDL;
using System;
using System.IO;
using System.Text;
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

        static double Fps;

        const double SecondsPerFrame = 1D / (16777216D / 280896D);

        static IntPtr Window;
        static IntPtr Renderer;

        static GBA Gba;

        static bool Sync = true;

        static bool IntegerScaling = false;
        static bool IsFullscreen = false;
        static bool Stretched = false;

        public static void Main(string[] args)
        {
            SDL_Init(SDL_INIT_AUDIO | SDL_INIT_VIDEO);

            bool GuiMode = args.Length == 0;

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

            string romPath;
            byte[] rom;
            byte[] bios;
            const string biosPath = "gba_bios.bin";

            if (!GuiMode)
            {
                romPath = args[0];
            }
            else
            {
                Stream img = typeof(MainSDL).Assembly.GetManifestResourceStream("OptimeGBA-SDL.icon.raw");
                byte[] streamBytes = ReadFully(img);

                const int logoWidth = 34;
                const int logoHeight = 21;
                const int logoBpp = 4;

                IntPtr iconTexture = SDL_CreateTexture(Renderer, SDL_PIXELFORMAT_ABGR8888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, logoWidth, logoHeight);
                IntPtr data = Marshal.AllocHGlobal(streamBytes.Length);
                Marshal.Copy(streamBytes, 0, data, streamBytes.Length);
                SDL_UpdateTexture(iconTexture, IntPtr.Zero, data, logoWidth * logoBpp);
                Marshal.FreeHGlobal(data);

                SDL_EventState(SDL_EventType.SDL_DROPFILE, SDL_ENABLE);
                bool done = false;

                String filename = "";
                while (!done)
                {
                    SDL_Event evt;
                    while (SDL_PollEvent(out evt) != 0)
                    {
                        switch (evt.type)
                        {
                            case SDL_EventType.SDL_QUIT:
                                Environment.Exit(0);
                                break;

                            case SDL_EventType.SDL_DROPFILE:
                                filename = Marshal.PtrToStringUTF8(evt.drop.file);
                                done = true;
                                break;
                        }

                        SDL_Rect dest = new SDL_Rect();
                        SDL_GetWindowSize(Window, out int w, out int h);

                        double ratio = Math.Min((double)h / (double)logoHeight, (double)w / (double)logoWidth);
                        int fillWidth;
                        int fillHeight;

                        fillWidth = (int)(ratio * logoWidth);
                        fillHeight = (int)(ratio * logoHeight);

                        dest.w = fillWidth;
                        dest.h = fillHeight;
                        dest.x = (int)((w - fillWidth) / 2);
                        dest.y = (int)((h - fillHeight) / 2);

                        SDL_RenderClear(Renderer);
                        SDL_RenderCopy(Renderer, iconTexture, IntPtr.Zero, ref dest);
                        SDL_RenderPresent(Renderer);

                    }
                }
                romPath = filename;
            }

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
                    SdlMessage("Error", "A GBA BIOS was provided, but there was an issue loading it.");
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

                        case SDL_EventType.SDL_DROPFILE:
                            var filename = Marshal.PtrToStringUTF8(evt.drop.file);
                            try
                            {
                                Gba.Provider.Rom = System.IO.File.ReadAllBytes(filename);
                                ResetDue = true;
                            }
                            catch
                            {
                                Log("An error occurred loading the dropped ROM file.");
                                return;
                            }
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
                    // Reset time if behind schedule
                    if (currentSec - nextFrameAt >= SecondsPerFrame)
                    {
                        double diff = currentSec - nextFrameAt;
                        Log("Can't keep up! Skipping " + (int)(diff * 1000) + " milliseconds");
                        nextFrameAt = currentSec;
                    }

                    if (currentSec >= nextFrameAt)
                    {
                        nextFrameAt += SecondsPerFrame;

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

                    // Use Math.Floor to truncate to 2 decimal places
                    Fps = Math.Floor((frames / diff) * 100) / 100;
                    UpdateTitle();

                    fpsEvalTimer += 1;
                }

                fixed (byte* pixels = &Gba.Lcd.ScreenFront[0])
                {
                    SDL_UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixels, LCD.WIDTH * LCD.BYTES_PER_PIXEL);
                }

                SDL_Rect dest = new SDL_Rect();
                SDL_GetWindowSize(Window, out int w, out int h);
                double ratio = Math.Min((double)h / (double)LCD.HEIGHT, (double)w / (double)LCD.WIDTH);
                int fillWidth;
                int fillHeight;
                if (!Stretched)
                {
                    if (IntegerScaling)
                    {
                        fillWidth = ((int)(ratio * LCD.WIDTH) / LCD.WIDTH) * LCD.WIDTH;
                        fillHeight = ((int)(ratio * LCD.HEIGHT) / LCD.HEIGHT) * LCD.HEIGHT;
                    }
                    else
                    {
                        fillWidth = (int)(ratio * LCD.WIDTH);
                        fillHeight = (int)(ratio * LCD.HEIGHT);
                    }
                    dest.w = fillWidth;
                    dest.h = fillHeight;
                    dest.x = (int)((w - fillWidth) / 2);
                    dest.y = (int)((h - fillHeight) / 2);
                }
                else
                {
                    dest.w = w;
                    dest.h = h;
                    dest.x = 0;
                    dest.y = 0;
                }

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

        public static byte[] ReadFully(Stream input)
        {
            byte[] buffer = new byte[16 * 1024];
            using (MemoryStream ms = new MemoryStream())
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ms.Write(buffer, 0, read);
                }
                return ms.ToArray();
            }
        }

        static bool LCtrl;
        static bool LAlt;
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
                case SDL_Keycode.SDLK_RETURN:
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
            }

            switch (kb.keysym.sym)
            {
                case SDL_Keycode.SDLK_LALT:
                    LAlt = pressed;
                    break;

                case SDL_Keycode.SDLK_r:
                    if (LCtrl)
                    {
                        ResetDue = true;
                    }
                    break;

                case SDL_Keycode.SDLK_i:
                    if (pressed)
                    {
                        IntegerScaling = !IntegerScaling;
                    }
                    break;

                case SDL_Keycode.SDLK_u:
                    if (pressed)
                    {
                        Stretched = !Stretched;
                    }
                    break;

                case SDL_Keycode.SDLK_RETURN:
                case SDL_Keycode.SDLK_KP_ENTER:
                    if (pressed && LAlt)
                    {
                        ToggleFullscreen();
                    }
                    break;

                case SDL_Keycode.SDLK_F11:
                    if (pressed)
                    {
                        ToggleFullscreen();
                    }
                    break;
            }
        }

        public static void UpdateTitle()
        {
            SDL_SetWindowTitle(Window, "Optime GBA - " + Fps + " fps - " + GetAudioSamplesInQueue() + " samples queued");
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

        public static void ToggleFullscreen()
        {
            if (IsFullscreen)
            {
                SDL_SetWindowFullscreen(Window, 0);
                IsFullscreen = false;
            }
            else
            {
                SDL_SetWindowFullscreen(Window, (uint)SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP);
                IsFullscreen = true;
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