public delegate void Callback();

namespace OptimeGBA
{

    class GBA
    {
        public ARM7 Arm7;
        public Memory Mem;
        public Audio Audio;

        public uint[] registers = new uint[16];
        public Callback AudioCallback;
        public GBA(Callback audioCallback)
        {
            Arm7 = new ARM7(this);
            Mem = new Memory(this);
            Audio = new Audio(this);
            AudioCallback = audioCallback;
        }

        public uint Run() {
            // Arm7.Execute();
            Tick(8);
            return 8;
        }

        void Tick(uint cycles) {
            Audio.Tick(cycles);
        }
    }
}