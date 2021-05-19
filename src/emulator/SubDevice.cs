using System;

namespace OptimeGBA
{

    public abstract class SubDevice
    {
        public Device Device;
        public Arm7 Cpu;
        public Dma Dma;
        public Memory Mem;
        public Ppu Ppu;
        public Timers Timers;
        public HwControl HwControl;

        public abstract void StateChange();
    }
}