using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds9 : Device
    {
        public Nds Nds;
        public MemoryNds9 Mem;
        public HwControlNds HwControl;
        public Nds9Math Math;

        public Nds9(Nds nds)
        {
            Nds = nds;

            HwControl = new HwControlNds(this, true);

            Mem = new MemoryNds9(this, nds.Provider);
            Cpu = new Arm7(this, Mem, true, true, Nds.Cp15);

            Math = new Nds9Math(this);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            Cpu.SetVectorMode(true);

            // screw it 
            Cpu.SetTimingsTable(
                Cpu.Timing8And16,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
            );
            Cpu.SetTimingsTable(
                Cpu.Timing8And16InstrFetch,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32InstrFetch,
                8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8
            );
        }

        public override void StateChange() { }
    }
}