using System;

namespace OptimeGBA
{

    public sealed class Gba
    {
        public GbaProvider Provider;

        public Arm7 Arm7;
        public Memory Mem;
        public GbaAudio GbaAudio;
        public Lcd Lcd;
        public Dma Dma;
        public Keypad Keypad;
        public Timers Timers;
        public HwControl HwControl;

        public Scheduler Scheduler;

        public AudioCallback AudioCallback;

        public uint[] registers = new uint[16];
        public Gba(GbaProvider provider)
        {
            Provider = provider;

            Scheduler = new Scheduler();

            Mem = new Memory(this, provider);
            GbaAudio = new GbaAudio(this, Scheduler);
            Lcd = new Lcd(this, Scheduler);
            Keypad = new Keypad();
            Dma = new Dma(this);
            Timers = new Timers(this, Scheduler);
            HwControl = new HwControl(this);
            Arm7 = new Arm7(this);

            AudioCallback = provider.AudioCallback;

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif
        }

        uint ExtraTicks = 0;
        public uint Step()
        {
            ExtraTicks = 0;
            uint ticks = Arm7.Execute();

            Scheduler.CurrentTicks += ticks;
            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            return ticks + ExtraTicks;
        }

        public uint SchedulerStep()
        {
            ExtraTicks = 0;

            uint executed = 0;
            while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
            {
                uint cycles = Arm7.Execute();
                Scheduler.CurrentTicks += cycles;
                executed += cycles;
            }
            long next = Scheduler.NextEventTicks;
            Scheduler.PopFirstEvent().Callback(Scheduler.CurrentTicks - next);

            return executed + ExtraTicks;
        }

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
            ExtraTicks += cycles;
        }

        public void HaltSkip(long cyclesLate)
        {
            long before = Scheduler.CurrentTicks;
            while (!HwControl.Available)
            {
                long ticksPassed = Scheduler.NextEventTicks - Scheduler.CurrentTicks;
                Scheduler.CurrentTicks = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(0);

                ExtraTicks += (uint)ticksPassed;
            }
        }
    }
}