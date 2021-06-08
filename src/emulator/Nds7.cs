using System;
using static OptimeGBA.Bits;

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
            Dma = new DmaNds(this, false, Mem);

            Timers = new Timers(this, Nds.Scheduler, true, true);

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

        // POWCNT2
        public bool EnableSpeakers;
        public bool EnableWifi;

        // POSTFLG
        public byte POSTFLG;

        public override void StateChange() { }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000304:
                    if (EnableSpeakers) val = BitSet(val, 0);
                    if (EnableWifi) val = BitSet(val, 1);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000304:
                    EnableSpeakers = BitTest(val, 0);
                    EnableWifi = BitTest(val, 1);
                    break;
            }
        }
    }
}