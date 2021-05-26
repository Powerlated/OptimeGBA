using System;

namespace OptimeGBA
{
    public unsafe sealed class Nds7 : Device
    {
        public Nds Nds;
        public MemoryNds7 Mem;
        public HwControlNds HwControl;

        public Nds7(Nds nds)
        {
            Nds = nds;

            HwControl = new HwControlNds(this, false);

            Mem = new MemoryNds7(this, nds.Provider);
            Cpu = new Arm7(this, Mem, false, false, null);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

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