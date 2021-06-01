using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds9 : DeviceNds
    {
        public new MemoryNds9 Mem;
        public DmaNds Dma;
        public Nds9Math Math;

        public Nds9(Nds nds)
        {
            Nds = nds;

            HwControl = new HwControlNds(this, true);
            Mem = new MemoryNds9(this, nds.Provider);

            Cpu = new Arm7(this, Mem, true, true, Nds.Cp15);
            Dma = new DmaNds(this, true, Mem);

            Timers = new Timers(this, Nds.Scheduler, true);

            Math = new Nds9Math(this);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            Cpu.SetVectorMode(true);

            // screw it 
            Cpu.SetTimingsTable(
                Cpu.Timing8And16,
                1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32,
                1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing8And16InstrFetch,
                1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32InstrFetch,
                1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1
            );
        }

        public byte POSTFLG;

        public override void StateChange() { }
    }
}