using System;

namespace OptimeGBA
{

    public sealed class Nds9 : Device
    {
        public Nds Nds;
        
        public Nds9(Nds nds)
        { 
            Nds = nds;

            Mem = new MemoryNds9(this, nds.Provider);
            Cpu = new Arm7(this, true);

            Mem.InitPageTables();
            Cpu.FillPipelineArm();

            Cpu.SetVectorMode(true);
        }

        public override void StateChange() { }
    }
}