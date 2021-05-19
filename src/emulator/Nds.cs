using System;

namespace OptimeGBA
{

    public sealed class Nds
    {
        public ProviderNds Provider;

        public GbaAudio GbaAudio;
        public Keypad Keypad;

        public Nds7 Nds7;
        public Scheduler Scheduler;

        public uint[] registers = new uint[16];
        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();
            // AudioCallback = provider.AudioCallback;

            Nds7 = new Nds7(this) { Scheduler = Scheduler };

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif
        }

        public uint Step()
        {
            Nds7.Cpu.CheckInterrupts();
            long beforeTicks = Scheduler.CurrentTicks;
            if (!Nds7.Cpu.ThumbState)
            {
                Scheduler.CurrentTicks += Nds7.Cpu.ExecuteArm();
            }
            else
            {
                Scheduler.CurrentTicks += Nds7.Cpu.ExecuteThumb();
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

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
        }

        public void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }

        public void HaltSkip(long cyclesOffset) {}
    }
}