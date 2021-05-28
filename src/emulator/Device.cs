using System;

namespace OptimeGBA
{

    public abstract class Device
    {
        public Arm7 Cpu;
        public Timers Timers;
        public Memory Mem;

        public Scheduler Scheduler;

        public HwControl HwControl;

        public AudioCallback AudioCallback;

        public abstract void StateChange();
    }
}