using System;

namespace OptimeGBA
{

    public sealed class Gba
    {
        public GbaProvider Provider;

        public Arm7 Arm7;
        public Memory Mem;
        public GbaAudio GbaAudio;
        public Ppu Ppu;
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
            Ppu = new Ppu(this, Scheduler);
            Keypad = new Keypad();
            Dma = new Dma(this);
            Timers = new Timers(this, Scheduler);
            HwControl = new HwControl(this);
            Arm7 = new Arm7(this);

            AudioCallback = provider.AudioCallback;

            Mem.InitPageTables();
            Arm7.FillPipelineArm();

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif
        }

        public uint Step()
        {
            Arm7.CheckInterrupts();
            long beforeTicks = Scheduler.CurrentTicks;
            if (!Arm7.ThumbState)
            {
                Scheduler.CurrentTicks += Arm7.ExecuteArm();
            }
            else
            {
                Scheduler.CurrentTicks += Arm7.ExecuteThumb();
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

        public void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }

        public uint StateStep()
        {
            Arm7.CheckInterrupts();

            long beforeTicks = Scheduler.CurrentTicks;
            if (!Arm7.ThumbState)
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Arm7.ExecuteArm();
                }
            }
            else
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Arm7.ExecuteThumb();
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