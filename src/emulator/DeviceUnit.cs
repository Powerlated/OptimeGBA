using System;

namespace OptimeGBA
{

    public abstract class DeviceUnit
    {
        public Arm7 Cpu;
        public Dma Dma;
        public MemoryUnit Mem;
        public Ppu Ppu;
        public Timers Timers;
        public HwControl HwControl;

        public Scheduler Scheduler;

        public AudioCallback AudioCallback;

        public abstract void StateChange();
        public abstract void Tick(uint cycles);
    }
}