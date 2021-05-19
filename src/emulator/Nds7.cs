using System;

namespace OptimeGBA
{

    public sealed class Nds7 : SubDevice
    {
        public ProviderGba Provider;

        public Arm7 Arm7;
        public new MemoryNds7 Mem;
        public Keypad Keypad;

        public Nds7(ProviderGba provider)
        {
            Provider = provider;

            Mem = new MemoryNds7(this);
            // Ppu = new Ppu(this, Scheduler);
            // Keypad = new Keypad();
            // Dma = new Dma(this);
            // Timers = new Timers(this, Scheduler);
            // HwControl = new HwControl(this);
            // Arm7 = new Arm7(this);

            Mem.InitPageTables();
            Arm7.FillPipelineArm();

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif
        }

        public override void StateChange() { }
    }
}