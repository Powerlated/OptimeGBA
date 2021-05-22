using System;

namespace OptimeGBA
{

    public sealed class Nds7 : Device
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
        }

        public override void StateChange() { }
    }
}