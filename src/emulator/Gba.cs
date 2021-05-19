using System;

namespace OptimeGBA
{

    public sealed class Gba : Device
    {
        public ProviderGba Provider;

        public GbaAudio GbaAudio;
        public Keypad Keypad;

        public Gba(ProviderGba provider)
        {
            Provider = provider;

            Scheduler = new Scheduler();

            Mem = new MemoryGba(this, provider);
            GbaAudio = new GbaAudio(this, Scheduler);
            Ppu = new PpuGba(this, Scheduler);
            Keypad = new Keypad();
            Dma = new Dma(this);
            Timers = new Timers(this, Scheduler);
            HwControl = new HwControlGba(this);
            Cpu = new Arm7(this);

            AudioCallback = provider.AudioCallback;

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

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