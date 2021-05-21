using static SDL2.SDL;
using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using System.Runtime.InteropServices;
using OptimeGBA;
using DiscordRPC;

namespace OptimeGBAEmulator
{
    public sealed unsafe class MainSDL
    {
        const uint AUDIO_SAMPLE_THRESHOLD = 1024;
        const uint AUDIO_SAMPLE_FULL_THRESHOLD = 1024;
        const int SAMPLES_PER_CALLBACK = 32;

        const int CyclesPerFrameGba = 280896;
        const double SecondsPerFrameGba = 1D / (16777216D / 280896D);
        const int CyclesPerFrameNds = 560190;
        const double SecondsPerFrameNds = 1D / (33513982D / 560190D);
        const double SecondsPerFrameAnimation = 0.1D;

        const int LogoWidth = 34;
        const int LogoHeight = 21;
        const int LogoBpp = 4;
        const int LogoFrames = 8;

        static SDL_AudioSpec want, have;
        static uint AudioDevice;

        static IntPtr Texture;

        static double Fps;
        static double Mips;

        static IntPtr Window;
        static IntPtr Renderer;

        static Gba Gba;
        static Nds Nds;

        static bool NdsMode;

        static Dictionary<string, string> GameNameDictionary = new Dictionary<string, string>();

        static string RomName;
        static DiscordRpcClient Client;
        static Timestamps Timestamp;
        static Assets RpcAssets;

        static bool Sync = true;

        static long Seconds;

        static bool IntegerScaling = false;
        static bool IsFullscreen = false;
        static bool Stretched = false;

        const int GBA_WIDTH = 240;
        const int GBA_HEIGHT = 160;

        const int NDS_WIDTH = 256;
        const int NDS_HEIGHT = 192;

        static bool Excepted = false;
        static string ExceptionMessage = "";

        static Thread EmulationThread;
        static AutoResetEvent ThreadSync = new AutoResetEvent(false);

        public static void Main(string[] args)
        {
            // Parse No-Intro database
            var stream = typeof(MainSDL).Assembly.GetManifestResourceStream("OptimeGBA-SDL.resources.no-intro.dat");
            var doc = new XmlDocument();
            doc.Load(stream);
            foreach (XmlNode node in doc.GetElementsByTagName("game"))
            {
                var romNode = node.SelectNodes("rom")[0];
                if (romNode != null)
                {
                    var name = node.Attributes["name"].Value;
                    var serialNode = romNode.Attributes["serial"];
                    if (serialNode != null)
                    {
                        GameNameDictionary[serialNode.Value] = name;
                    }
                }
            }

#if DISCORD_RPC
            Client = new DiscordRpcClient("794391124000243742");
            Client.Initialize();
            RpcAssets = new Assets()
            {
                LargeImageKey = "icon-square",
                LargeImageText = "Hi!",
            };
            Client.SetPresence(new RichPresence()
            {
                State = "Standby",
                Assets = RpcAssets,
            });
#endif

            EmulationThread = new Thread(EmulationThreadHandler);
            EmulationThread.Name = "Emulation Core";
            EmulationThread.Start();

            SDL_Init(SDL_INIT_AUDIO | SDL_INIT_VIDEO);

            bool GuiMode = args.Length == 0;

            Window = SDL_CreateWindow("Optime GBA", 0, 0, GBA_WIDTH * 4, GBA_HEIGHT * 4, SDL_WindowFlags.SDL_WINDOW_SHOWN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
            SDL_SetWindowPosition(Window, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED);
            Renderer = SDL_CreateRenderer(Window, -1, SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

            SDL_SetWindowMinimumSize(Window, GBA_WIDTH, GBA_HEIGHT);

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
            byte[] gbaBios;
            byte[] ndsBios7;
            byte[] ndsBios9;

            const string gbaBiosPath = "gba_bios.bin";
            const string ndsBios7Path = "bios7.bin";
            const string ndsBios9Path = "bios9.bin";

            if (!GuiMode)
            {
                romPath = args[0];
            }
            else
            {
                byte[][] streamData = ReadAnimationFrames();

                IntPtr iconTexture = SDL_CreateTexture(Renderer, SDL_PIXELFORMAT_ABGR8888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STATIC, LogoWidth, LogoHeight);
                IntPtr data = Marshal.AllocHGlobal(streamData[0].Length);
                double timeNextFrame = 0;

                int frame = 0;

                SDL_EventState(SDL_EventType.SDL_DROPFILE, SDL_ENABLE);
                bool done = false;
                bool fileLoaded = false;

                SDL_Rect dest = new SDL_Rect();

                String filename = "";
                while (!done)
                {
                    Marshal.Copy(streamData[frame], 0, data, streamData[0].Length);
                    SDL_UpdateTexture(iconTexture, IntPtr.Zero, data, LogoWidth * LogoBpp);

                    SDL_Event evt;
                    while (SDL_PollEvent(out evt) != 0)
                    {
                        switch (evt.type)
                        {
                            case SDL_EventType.SDL_QUIT:
                                Cleanup();
                                break;

                            case SDL_EventType.SDL_DROPFILE:
                                filename = Marshal.PtrToStringUTF8(evt.drop.file);
                                fileLoaded = true;
                                break;
                        }

                        SDL_GetWindowSize(Window, out int w, out int h);

                        double ratio = Math.Min((double)h / (double)LogoHeight, (double)w / (double)LogoWidth);
                        int fillWidth;
                        int fillHeight;

                        fillWidth = (int)(ratio * LogoWidth);
                        fillHeight = (int)(ratio * LogoHeight);

                        dest.w = fillWidth;
                        dest.h = fillHeight;
                        dest.x = (int)((w - fillWidth) / 2);
                        dest.y = (int)((h - fillHeight) / 2);
                    }


                    if (fileLoaded)
                    {
                        double timeCurrent = GetTime();

                        // Reset time if behind schedule
                        if (timeCurrent - timeNextFrame >= SecondsPerFrameAnimation)
                        {
                            double diff = timeCurrent - timeNextFrame;
                            timeNextFrame = timeCurrent;
                        }

                        if (timeCurrent >= timeNextFrame)
                        {
                            timeNextFrame += SecondsPerFrameAnimation;

                            frame++;
                            if (frame == LogoFrames)
                            {
                                done = true;
                            }
                        }
                    }

                    SDL_RenderClear(Renderer);
                    SDL_RenderCopy(Renderer, iconTexture, IntPtr.Zero, ref dest);
                    SDL_RenderPresent(Renderer);
                }
                romPath = filename;

                Marshal.FreeHGlobal(data);
            }

        reload:

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

            NdsMode = romPath.Substring(romPath.Length - 3).ToLower() == "nds";

            if (NdsMode)
            {
                Console.WriteLine("Loading NDS file");

                if (!System.IO.File.Exists(ndsBios7Path) || !System.IO.File.Exists(ndsBios9Path))
                {
                    SdlMessage("Error", "Please place valid NDS BIOSes in the same directory as OptimeGBA.exe named \"bios7.bin\" and \"bios9.bin\"");
                    return;
                }
                else
                {
                    try
                    {
                        ndsBios7 = System.IO.File.ReadAllBytes(ndsBios7Path);
                        ndsBios9 = System.IO.File.ReadAllBytes(ndsBios9Path);
                    }
                    catch
                    {
                        SdlMessage("Error", "NDS BIOSes were provided, but there was an issue loading them.");
                        return;
                    }
                }

                var provider = new ProviderNds(ndsBios7, ndsBios9, rom, savPath, AudioReady);
                Nds = new Nds(provider);

                Texture = SDL_CreateTexture(Renderer, SDL_PIXELFORMAT_ABGR8888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, NDS_WIDTH, NDS_HEIGHT);
                SDL_SetWindowMinimumSize(Window, NDS_WIDTH, NDS_HEIGHT);
                SDL_SetWindowSize(Window, NDS_WIDTH * 4, NDS_HEIGHT * 4);
            }
            else
            {
                Console.WriteLine("Loading GBA file");

                if (!System.IO.File.Exists(gbaBiosPath))
                {
                    SdlMessage("Error", "Please place a valid GBA BIOS in the same directory as OptimeGBA.exe named \"gba_bios.bin\"");
                    return;
                }
                else
                {
                    try
                    {
                        gbaBios = System.IO.File.ReadAllBytes(gbaBiosPath);
                    }
                    catch
                    {
                        SdlMessage("Error", "A GBA BIOS was provided, but there was an issue loading it.");
                        return;
                    }
                }

                var provider = new ProviderGba(gbaBios, rom, savPath, AudioReady);
                provider.BootBios = true;
                Gba = new Gba(provider);

                UpdateRomName(romPath);

                Gba.Mem.SaveProvider.LoadSave(sav);

                Texture = SDL_CreateTexture(Renderer, SDL_PIXELFORMAT_ABGR8888, (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, GBA_WIDTH, GBA_HEIGHT);
                SDL_SetWindowMinimumSize(Window, GBA_WIDTH, GBA_HEIGHT);
                SDL_SetWindowSize(Window, GBA_WIDTH * 4, GBA_HEIGHT * 4);
            }

            bool quit = false;
            double nextFrameAt = 0;
            double fpsEvalTimer = 0;

            void resetTimers()
            {
                var time = GetTime();
                nextFrameAt = time;
                fpsEvalTimer = time;
            }
            resetTimers();

            // Actually start the game
            UpdatePlayingRpc();

            Timestamp = Timestamps.Now;

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
                                romPath = filename;
                                goto reload;
                            }
                            catch
                            {
                                Log("An error occurred loading the dropped ROM file.");
                                return;
                            }
                    }
                }

                if (NdsMode)
                {
                    if (ResetDue)
                    {
                        ResetDue = false;
                        // TODO: NDS Save memory
                        ProviderNds p = Nds.Provider;
                        Nds = new Nds(p);
                        nextFrameAt = GetTime();
                    }

                    double currentSec = GetTime();

                    // Reset time if behind schedule
                    if (currentSec - nextFrameAt >= SecondsPerFrameNds)
                    {
                        double diff = currentSec - nextFrameAt;
                        Log("Can't keep up! Skipping " + (int)(diff * 1000) + " milliseconds");
                        nextFrameAt = currentSec;
                    }

                    if (currentSec >= nextFrameAt)
                    {
                        nextFrameAt += SecondsPerFrameNds;

                        ThreadSync.Set();
                    }

                    if (currentSec >= fpsEvalTimer)
                    {
                        double diff = currentSec - fpsEvalTimer + 1;
                        double frames = CyclesRan / CyclesPerFrameNds;
                        CyclesRan = 0;

                        long ran =
                            Nds.Nds7.Cpu.InstructionsRan +
                            Nds.Nds9.Cpu.InstructionsRan;
                        Nds.Nds7.Cpu.InstructionsRan = 0;
                        Nds.Nds9.Cpu.InstructionsRan = 0;
                        double mips = (double)ran / 1000000D;

                        // Use Math.Floor to truncate to 2 decimal places
                        Fps = Math.Floor((frames / diff) * 100) / 100;
                        Mips = Math.Floor((mips / diff) * 100) / 100;
                        UpdateTitle();
                        Seconds++;
                        UpdatePlayingRpc();

                        fpsEvalTimer += 1;
                    }

#if UNSAFE
                    SDL_UpdateTexture(Texture, IntPtr.Zero, (IntPtr)Nds.Ppu.Renderer.ScreenFront, NDS_WIDTH * PpuRenderer.BYTES_PER_PIXEL);
#else
                fixed (uint* pixels = &Nds.Ppu.ScreenFront[0])
                {
                    SDL_UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixels, NDS_WIDTH * Ppu.BYTES_PER_PIXEL);
#endif

                    SDL_Rect dest = new SDL_Rect();
                    SDL_GetWindowSize(Window, out int w, out int h);
                    double ratio = Math.Min((double)h / (double)NDS_HEIGHT, (double)w / (double)NDS_WIDTH);
                    int fillWidth;
                    int fillHeight;
                    if (!Stretched)
                    {
                        if (IntegerScaling)
                        {
                            fillWidth = ((int)(ratio * NDS_WIDTH) / NDS_WIDTH) * NDS_WIDTH;
                            fillHeight = ((int)(ratio * NDS_HEIGHT) / NDS_HEIGHT) * NDS_HEIGHT;
                        }
                        else
                        {
                            fillWidth = (int)(ratio * NDS_WIDTH);
                            fillHeight = (int)(ratio * NDS_HEIGHT);
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
                    SDL_RenderCopy(Renderer, Texture, IntPtr.Zero, ref dest);
                    SDL_RenderPresent(Renderer);

                    // TODO: NDS Saves
                    // if (Gba.Mem.SaveProvider.Dirty)
                    // {
                    //     Gba.Mem.SaveProvider.Dirty = false;
                    //     try
                    //     {
                    //         System.IO.File.WriteAllBytesAsync(Gba.Provider.SavPath, Gba.Mem.SaveProvider.GetSave());
                    //     }
                    //     catch
                    //     {
                    //         Console.WriteLine("Failed to write .sav file!");
                    //     }
                    // }

                    if (Excepted)
                    {
                        SdlMessage("Exception Caught", ExceptionMessage);
                        quit = true;
                    }
                }
                else
                {
                    if (ResetDue)
                    {
                        ResetDue = false;
                        byte[] save = Gba.Mem.SaveProvider.GetSave();
                        ProviderGba p = Gba.Provider;
                        Gba = new Gba(p);
                        Gba.Mem.SaveProvider.LoadSave(save);

                        nextFrameAt = GetTime();
                    }

                    double currentSec = GetTime();

                    // Reset time if behind schedule
                    if (currentSec - nextFrameAt >= SecondsPerFrameGba)
                    {
                        double diff = currentSec - nextFrameAt;
                        Log("Can't keep up! Skipping " + (int)(diff * 1000) + " milliseconds");
                        nextFrameAt = currentSec;
                    }

                    if (currentSec >= nextFrameAt)
                    {
                        nextFrameAt += SecondsPerFrameGba;

                        ThreadSync.Set();
                    }

                    if (currentSec >= fpsEvalTimer)
                    {
                        double diff = currentSec - fpsEvalTimer + 1;
                        double frames = CyclesRan / CyclesPerFrameGba;
                        CyclesRan = 0;

                        double mips = (double)Gba.Cpu.InstructionsRan / 1000000D;
                        Gba.Cpu.InstructionsRan = 0;

                        // Use Math.Floor to truncate to 2 decimal places
                        Fps = Math.Floor((frames / diff) * 100) / 100;
                        Mips = Math.Floor((mips / diff) * 100) / 100;
                        UpdateTitle();
                        Seconds++;
                        UpdatePlayingRpc();

                        fpsEvalTimer += 1;
                    }

#if UNSAFE
                    SDL_UpdateTexture(Texture, IntPtr.Zero, (IntPtr)Gba.Ppu.Renderer.ScreenFront, GBA_WIDTH * PpuRenderer.BYTES_PER_PIXEL);
#else
                fixed (uint* pixels = &Gba.Ppu.ScreenFront[0])
                {
                    SDL_UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixels, GBA_WIDTH * Ppu.BYTES_PER_PIXEL);GBA_HEIGHT
#endif

                    SDL_Rect dest = new SDL_Rect();
                    SDL_GetWindowSize(Window, out int w, out int h);
                    double ratio = Math.Min((double)h / (double)GBA_HEIGHT, (double)w / (double)GBA_WIDTH);
                    int fillWidth;
                    int fillHeight;
                    if (!Stretched)
                    {
                        if (IntegerScaling)
                        {
                            fillWidth = ((int)(ratio * GBA_WIDTH) / GBA_WIDTH) * GBA_WIDTH;
                            fillHeight = ((int)(ratio * GBA_HEIGHT) / GBA_HEIGHT) * GBA_HEIGHT;
                        }
                        else
                        {
                            fillWidth = (int)(ratio * GBA_WIDTH);
                            fillHeight = (int)(ratio * GBA_HEIGHT);
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
                    SDL_RenderCopy(Renderer, Texture, IntPtr.Zero, ref dest);
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

                    if (Excepted)
                    {
                        SdlMessage("Exception Caught", ExceptionMessage);
                        quit = true;
                    }
                }
            }

            SDL_DestroyRenderer(Renderer);
            SDL_DestroyWindow(Window);
            SDL_AudioQuit();
            SDL_VideoQuit();
            SDL_Quit();
            Cleanup();
        }

        public static void UpdatePlayingRpc()
        {
            var mm = ((Seconds / 60) % 60).ToString().PadLeft(2, '0');
            var ss = (Seconds % 60).ToString().PadLeft(2, '0');
            string stateString;
            if (Seconds > 3600)
            {
                var hh = ((Seconds / 3600) % 60).ToString().PadLeft(2, '0');
                stateString = $"Playing for {hh}:{mm}:{ss}";
            }
            else
            {
                stateString = $"Playing for {mm}:{ss}";
            }

#if DISCORD_RPC
            Client.SetPresence(new RichPresence()
            {
                Details = RomName,
                Timestamps = Timestamp,
                Assets = RpcAssets
            });
#endif
        }

        public static void Cleanup()
        {
#if DISCORD_RPC
            Client.Dispose();
#endif
            Environment.Exit(0);
        }

        public static double GetTime()
        {
            return (double)SDL_GetPerformanceCounter() / (double)SDL_GetPerformanceFrequency();
        }

        public static void SdlMessage(string title, string msg)
        {
            SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION, title, msg, Window);
        }

        public static byte[][] ReadAnimationFrames()
        {
            byte[][] buf = new byte[8][];

            for (int i = 0; i < LogoFrames; i++)
            {
                buf[i] = ReadResource($"OptimeGBA-SDL.resources.animation.{i}.raw");
            }

            return buf;
        }

        public static byte[] ReadResource(String res)
        {
            Stream img = typeof(MainSDL).Assembly.GetManifestResourceStream(res);
            return ReadFully(img);
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

            if (NdsMode)
            {
                switch (kb.keysym.sym)
                {
                    case SDL_Keycode.SDLK_z:
                        Nds.Keypad.B = pressed;
                        break;
                    case SDL_Keycode.SDLK_x:
                        Nds.Keypad.A = pressed;
                        break;

                    case SDL_Keycode.SDLK_BACKSPACE:
                        Nds.Keypad.Select = pressed;
                        break;
                    case SDL_Keycode.SDLK_RETURN:
                    case SDL_Keycode.SDLK_KP_ENTER:
                        if (!LAlt)
                        {
                            Nds.Keypad.Start = pressed;
                        }
                        break;

                    case SDL_Keycode.SDLK_LEFT:
                        Nds.Keypad.Left = pressed;
                        break;
                    case SDL_Keycode.SDLK_RIGHT:
                        Nds.Keypad.Right = pressed;
                        break;
                    case SDL_Keycode.SDLK_UP:
                        Nds.Keypad.Up = pressed;
                        break;
                    case SDL_Keycode.SDLK_DOWN:
                        Nds.Keypad.Down = pressed;
                        break;

                    case SDL_Keycode.SDLK_q:
                        Nds.Keypad.L = pressed;
                        break;
                    case SDL_Keycode.SDLK_e:
                        Nds.Keypad.R = pressed;
                        break;
                    case SDL_Keycode.SDLK_LCTRL:
                        LCtrl = pressed;
                        break;
                }

            }
            else
            {
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
                        if (!LAlt)
                        {
                            Gba.Keypad.Start = pressed;
                        }
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

                    case SDL_Keycode.SDLK_q:
                        Gba.Keypad.L = pressed;
                        break;
                    case SDL_Keycode.SDLK_e:
                        Gba.Keypad.R = pressed;
                        break;

                    case SDL_Keycode.SDLK_LCTRL:
                        LCtrl = pressed;
                        break;
                }

                if (pressed)
                {
                    switch (kb.keysym.sym)
                    {
                        case SDL_Keycode.SDLK_F1:
                            if (Gba.Ppu.Renderer.ColorCorrection)
                            {
                                Gba.Ppu.Renderer.DisableColorCorrection();
                            }
                            else
                            {
                                Gba.Ppu.Renderer.EnableColorCorrection();
                            }
                            break;

                        case SDL_Keycode.SDLK_F2:
                            Gba.Ppu.Renderer.DebugEnableRendering = !Gba.Ppu.Renderer.DebugEnableRendering;
                            break;
                        case SDL_Keycode.SDLK_F3:
                            Gba.GbaAudio.DebugEnableA = !Gba.GbaAudio.DebugEnableA;
                            UpdateTitle();
                            break;
                        case SDL_Keycode.SDLK_F4:
                            Gba.GbaAudio.DebugEnableB = !Gba.GbaAudio.DebugEnableB;
                            UpdateTitle();
                            break;
                        case SDL_Keycode.SDLK_F5:
                            Gba.GbaAudio.GbAudio.enable1Out = !Gba.GbaAudio.GbAudio.enable1Out;
                            UpdateTitle();
                            break;
                        case SDL_Keycode.SDLK_F6:
                            Gba.GbaAudio.GbAudio.enable2Out = !Gba.GbaAudio.GbAudio.enable2Out;
                            UpdateTitle();
                            break;
                        case SDL_Keycode.SDLK_F7:
                            Gba.GbaAudio.GbAudio.enable3Out = !Gba.GbaAudio.GbAudio.enable3Out;
                            UpdateTitle();
                            break;
                        case SDL_Keycode.SDLK_F8:
                            Gba.GbaAudio.GbAudio.enable4Out = !Gba.GbaAudio.GbAudio.enable4Out;
                            UpdateTitle();
                            break;

                        case SDL_Keycode.SDLK_LEFTBRACKET:
                            if (Gba.GbaAudio.GbAudio.PsgFactor > 0)
                            {
                                Gba.GbaAudio.GbAudio.PsgFactor--;
                                UpdateTitle();
                            }
                            break;

                        case SDL_Keycode.SDLK_RIGHTBRACKET:
                            Gba.GbaAudio.GbAudio.PsgFactor++;
                            UpdateTitle();
                            break;

                        case SDL_Keycode.SDLK_F9:
                            Gba.GbaAudio.Resample = !Gba.GbaAudio.Resample;
                            UpdateTitle();
                            break;
                    }
                }
            }

            switch (kb.keysym.sym)
            {
                case SDL_Keycode.SDLK_SPACE:
                    Space = pressed;
                    Sync = !(Space || Tab);
                    break;
                case SDL_Keycode.SDLK_TAB:
                    Tab = pressed;
                    Sync = !(Space || Tab);
                    break;

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

        public static void UpdateRomName(string path)
        {
            if (GameNameDictionary.ContainsKey(Gba.Provider.RomId))
            {
                RomName = GameNameDictionary[Gba.Provider.RomId];
            }
            else
            {
                RomName = Path.GetFileName(path);
            }
        }

        public static void UpdateTitle()
        {
            if (NdsMode)
            {
                SDL_SetWindowTitle(
                    Window,
                    "Optime GBA (DS) - " + Fps + " fps - " + Mips + " MIPS - " + GetAudioSamplesInQueue() + " samples queued"
                );
            }
            else
            {
                bool fA = Gba.GbaAudio.DebugEnableA;
                bool fB = Gba.GbaAudio.DebugEnableB;
                bool p1 = Gba.GbaAudio.GbAudio.enable1Out;
                bool p2 = Gba.GbaAudio.GbAudio.enable2Out;
                bool p3 = Gba.GbaAudio.GbAudio.enable3Out;
                bool p4 = Gba.GbaAudio.GbAudio.enable4Out;
                bool re = Gba.GbaAudio.Resample;
                SDL_SetWindowTitle(
                    Window,
                    "Optime GBA - " + Fps + " fps - " + Mips + " MIPS - " + GetAudioSamplesInQueue() + " samples queued | " +
                    (fA ? "A " : "- ") +
                    (fB ? "B " : "- ") +
                    (p1 ? "1 " : "- ") +
                    (p2 ? "2 " : "- ") +
                    (p3 ? "3 " : "- ") +
                    (p4 ? "4 " : "- ") +
                    (re ? "RE " : "-- ") +
                    "PSG " + Gba.GbaAudio.GbAudio.PsgFactor + "X"
                );
            }
        }

        static int CyclesLeft;
        static long CyclesRan;

        public static void EmulationThreadHandler()
        {
            try
            {
                while (true)
                {
                    ThreadSync.WaitOne();

                    RunFrame();

                    while (!Sync)
                    {
                        RunFrame();
                    }
                }
            }
            catch (Exception e)
            {
                ExceptionMessage = e.ToString();
                Excepted = true;
            }
        }

        public static void RunFrame()
        {
            if (NdsMode)
            {
                CyclesRan += CyclesPerFrameNds;
                CyclesLeft += CyclesPerFrameNds;
                while (CyclesLeft > 0)
                {
                    CyclesLeft -= (int)Nds.Step();
                }
            }
            else
            {
                CyclesRan += CyclesPerFrameGba;
                CyclesLeft += CyclesPerFrameGba;
                while (CyclesLeft > 0)
                {
                    CyclesLeft -= (int)Gba.StateStep();
                }
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
