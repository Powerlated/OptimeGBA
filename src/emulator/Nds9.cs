using System;

namespace OptimeGBA
{

    public sealed class Nds9 : Device
    {
        public Nds Nds;
        public MemoryNds9 Mem;
        
        public Nds9(Nds nds)
        { 
            Nds = nds;

            HwControl = new HwControlNds(Nds, true);

            Mem = new MemoryNds9(this, nds.Provider);
            Cpu = new Arm7(this, Mem, true, true, Nds.Cp15);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            Cpu.SetVectorMode(true);
        }

        public override void StateChange() { }
    }
}