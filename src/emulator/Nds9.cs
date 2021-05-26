using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds9 : Device
    {
        public Nds Nds;
        public MemoryNds9 Mem;
        public HwControlNds HwControl;

        public Nds9(Nds nds)
        {
            Nds = nds;

            HwControl = new HwControlNds(this, true);

            Mem = new MemoryNds9(this, nds.Provider);
            Cpu = new Arm7(this, Mem, true, true, Nds.Cp15);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            Cpu.SetVectorMode(true);

            // screw it 
            Cpu.SetTimingsTable(
                Cpu.Timing8And16,
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32,
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4
            );
            Cpu.SetTimingsTable(
                Cpu.Timing8And16InstrFetch,
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4
            );
            Cpu.SetTimingsTable(
                Cpu.Timing32InstrFetch,
                4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4
            );
        }

        public override void StateChange() { }
    }
}