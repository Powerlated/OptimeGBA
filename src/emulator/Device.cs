using System;

namespace OptimeGBA
{

    public abstract class Device
    {
        public Arm7 Cpu;
        public Dma Dma;
        public Timers Timers;
        public HwControl HwControl;

        public Scheduler Scheduler;

        public AudioCallback AudioCallback;

        public abstract void StateChange();
    }
}