using System;

namespace OptimeGBA
{

    public sealed class GBA
    {
        public GbaProvider Provider;

        public ARM7 Arm7;
        public Memory Mem;
        public GBAAudio GbaAudio;
        public LCD Lcd;
        public DMA Dma;
        public Keypad Keypad;
        public Timers Timers;
        public HWControl HwControl;

        public Scheduler Scheduler;

        public AudioCallback AudioCallback;

        public uint[] registers = new uint[16];
        public GBA(GbaProvider provider)
        {
            Provider = provider;

            Scheduler = new Scheduler();

            Arm7 = new ARM7(this);
            Mem = new Memory(this, provider);
            GbaAudio = new GBAAudio(this, Scheduler);
            Lcd = new LCD(this, Scheduler);
            Keypad = new Keypad();
            Dma = new DMA(this);
            Timers = new Timers(this, Scheduler);
            HwControl = new HWControl(this);


            AudioCallback = provider.AudioCallback;
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