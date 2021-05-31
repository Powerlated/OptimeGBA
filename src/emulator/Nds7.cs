using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds7 : DeviceNds
    {
        public new MemoryNds7 Mem;
        public DmaNds Dma;
        public Spi Spi;

        public Nds7(Nds nds)
        {
            Nds = nds;

            HwControl = new HwControlNds(this, false);
            Mem = new MemoryNds7(this, nds.Provider);
            Spi = new Spi(this);

            Cpu = new Arm7(this, Mem, false, false, null);
            Dma = new DmaNds(this, Mem);

            Timers = new Timers(this, Nds.Scheduler, true);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            // screw it 
            Cpu.SetTimingsTable(
                Cpu.Timing8And16,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing8And16InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
        }

        public byte POSTFLG;

        public override void StateChange() { }
    }
}