using System;

namespace OptimeGBA
{

    public sealed class Nds7 : DeviceUnit
    {
        public GbaProvider Provider;

        public Arm7 Arm7;
        public new MemoryNds7 Mem;
        public Keypad Keypad;

        public Nds7(GbaProvider provider)
        {
            Provider = provider;

            Scheduler = new Scheduler();

            Mem = new MemoryNds7(this);
            Ppu = new Ppu(this, Scheduler);
            Keypad = new Keypad();
            // Dma = new Dma(this);
            // Timers = new Timers(this, Scheduler);
            // HwControl = new HwControl(this);
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

        public override void StateChange()
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

        public override void Tick(uint cycles)
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