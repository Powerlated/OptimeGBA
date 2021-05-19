using System;

namespace OptimeGBA
{

    public sealed class Nds : Device
    {
        public ProviderNds Provider;

        public GbaAudio GbaAudio;
        public Keypad Keypad;

        public Nds7 Nds7;

        public uint[] registers = new uint[16];
        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();
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

        public uint StateStep()
        {
            Nds7.Cpu.CheckInterrupts();

            long beforeTicks = Scheduler.CurrentTicks;
            if (!Nds7.Cpu.ThumbState)
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Nds7.Cpu.ExecuteArm();
                }
            }
            else
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Nds7.Cpu.ExecuteThumb();
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

        public override void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }
    }
}