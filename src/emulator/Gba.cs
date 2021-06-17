using System;

namespace OptimeGBA
{

    public unsafe sealed class Gba : Device
    {
        public ProviderGba Provider;

        public new MemoryGba Mem;
        public GbaAudio GbaAudio;
        public Keypad Keypad;
        public PpuGba Ppu;
        public HwControlGba HwControl;
        public DmaGba Dma;

        public Gba(ProviderGba provider)
        {
            Provider = provider;

            Scheduler = new Scheduler();

            Mem = new MemoryGba(this, provider);
            GbaAudio = new GbaAudio(this, Scheduler);
            Ppu = new PpuGba(this, Scheduler);
            Keypad = new Keypad();
            Dma = new DmaGba(this);
            Timers = new Timers(this, Scheduler, false, true);
            HwControl = new HwControlGba(this);
            Cpu = new Arm7(this, Mem, false, false, null);

            Cpu.SetTimingsTable(
                Cpu.Timing8And16,
                1, // BIOS
                1, // Unused
                3, // EWRAM
                1, // IWRAM
                1, // I/O Registers
                1, // PPU Palettes
                1, // PPU VRAM
                1, // PPU OAM
                5, // Game Pak ROM/FlashROM 
                5, // Game Pak ROM/FlashROM 
                5, // Game Pak ROM/FlashROM 
                5, // Game Pak ROM/FlashROM 
                5, // Game Pak ROM/FlashROM 
                5, // Game Pak ROM/FlashROM
                5, // Game Pak SRAM/Flash
                5  // Game Pak SRAM/Flash
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32,
                1, // BIOS
                1, // Unused
                6, // EWRAM
                1, // IWRAM
                1, // I/O Registers
                2, // PPU Palettes
                2, // PPU VRAM
                1, // PPU OAM
                8, // Game Pak ROM/FlashROM 
                8, // Game Pak ROM/FlashROM 
                8, // Game Pak ROM/FlashROM 
                8, // Game Pak ROM/FlashROM 
                8, // Game Pak ROM/FlashROM 
                8, // Game Pak ROM/FlashROM
                8, // Game Pak SRAM/Flash
                8 // Game Pak SRAM/Flash
            );

            Cpu.SetTimingsTable(
                Cpu.Timing8And16InstrFetch,
                1, // BIOS
                1, // Unused
                3, // EWRAM
                1, // IWRAM
                1, // I/O Registers
                1, // PPU Palettes
                1, // PPU VRAM
                1, // PPU OAM
                   // Compensate for no prefetch buffer 5 -> 2
                2, // Game Pak ROM/FlashROM 
                2, // Game Pak ROM/FlashROM 
                2, // Game Pak ROM/FlashROM 
                2, // Game Pak ROM/FlashROM 
                2, // Game Pak ROM/FlashROM 
                2, // Game Pak ROM/FlashROM
                5, // Game Pak SRAM/Flash
                5 // Game Pak SRAM/Flash
            );

            Cpu.SetTimingsTable(
                Cpu.Timing32InstrFetch,
                1, // BIOS
                1, // Unused
                6, // EWRAM
                1, // IWRAM
                1, // I/O Registers
                2, // PPU Palettes
                2, // PPU VRAM
                1, // PPU OAM
                // Compensate for no prefetch buffer 8 -> 4
                4, // Game Pak ROM/FlashROM 
                4, // Game Pak ROM/FlashROM 
                4, // Game Pak ROM/FlashROM 
                4, // Game Pak ROM/FlashROM 
                4, // Game Pak ROM/FlashROM 
                4, // Game Pak ROM/FlashROM
                8, // Game Pak SRAM/Flash
                8  // Game Pak SRAM/Flash
            );

            if (!provider.BootBios)
            {
                Cpu.R13svc = 0x03007FE0;
                Cpu.R13irq = 0x03007FA0;
                Cpu.R13usr = 0x03007F00;

                // Default Stack Pointer
                Cpu.R[13] = Cpu.R13usr;
                Cpu.R[15] = 0x08000000;
            }

            AudioCallback = provider.AudioCallback;

            Mem.InitPageTables();
            Cpu.InitFlushPipeline();

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif
        }

        public uint Step()
        {
            Cpu.CheckInterrupts();
            long beforeTicks = Scheduler.CurrentTicks;
            if (!Cpu.ThumbState)
            {
                Scheduler.CurrentTicks += Cpu.ExecuteArm();
            }
            else
            {
                Scheduler.CurrentTicks += Cpu.ExecuteThumb();
            }
            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void DoNothing(long cyclesLate) { }

        public override void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }

        public uint StateStep()
        {
            Cpu.CheckInterrupts();

            long beforeTicks = Scheduler.CurrentTicks;
            if (!Cpu.ThumbState)
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Cpu.ExecuteArm();
                }
            }
            else
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Cpu.ExecuteThumb();
                }
            }

            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            // Return cycles executed
            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
        }

        public void HaltSkip(long cyclesLate)
        {
            long before = Scheduler.CurrentTicks;
            while (!HwControl.Available)
            {
                long ticksPassed = Scheduler.NextEventTicks - Scheduler.CurrentTicks;
                Scheduler.CurrentTicks = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(0);
            }
        }
    }
}