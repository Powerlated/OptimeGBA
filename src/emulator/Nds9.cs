using System;
using static OptimeGBA.Bits;

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

            Timers = new Timers(this, Nds.Scheduler, true, false);

            Math = new Nds9Math(this);

            Mem.InitPageTables();
            Cpu.InitFlushPipeline();

            Cpu.SetVectorMode(true);

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

        // POWCNT1
        public bool EnableDisplay;
        public bool Enable2DEngineA;
        public bool Enable3DRenderingEngine;
        public bool Enable3DGeometryEngine;
        public bool Enable2DEngineB;
        public bool DisplaySwap;

        public override void StateChange() { }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000304:
                    if (EnableDisplay) val = BitSet(val, 0);
                    if (Enable2DEngineA) val = BitSet(val, 1);
                    if (Enable3DRenderingEngine) val = BitSet(val, 2);
                    if (Enable3DGeometryEngine) val = BitSet(val, 3);
                    break;
                case 0x4000305:
                    if (Enable2DEngineB) val = BitSet(val, 1);
                    if (DisplaySwap) val = BitSet(val, 7);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000304:
                    EnableDisplay = BitTest(val, 0);
                    Enable2DEngineA = BitTest(val, 1);
                    Enable3DRenderingEngine = BitTest(val, 2);
                    Enable3DGeometryEngine = BitTest(val, 3);
                    break;
                case 0x4000305:
                    Enable2DEngineB = BitTest(val, 1);
                    DisplaySwap = BitTest(val, 7);
                    break;
            }
        }
    }
}