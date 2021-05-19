using System;

namespace OptimeGBA
{

    public sealed class Nds7 : Device
    {
        public Nds Nds;
        
        public Nds7(Nds nds)
        { 
            Nds = nds;

            HwControl = new HwControlNds7(this);

            Mem = new MemoryNds7(this, nds.Provider);
            Cpu = new Arm7(this);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();
        }

        public override void StateChange() { }
    }
}