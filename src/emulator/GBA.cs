public delegate void Callback();

namespace OptimeGBA
{

    public class GBA
    {
        public ARM7 Arm7;
        public Memory Mem;
        public Audio Audio;
        public LCD Lcd;

        public uint[] registers = new uint[16];
        public Callback AudioCallback;
        public GBA(Callback audioCallback)
        {
            Arm7 = new ARM7(this);
            Mem = new Memory(this);
            Audio = new Audio(this);
            Lcd = new LCD(this);
            AudioCallback = audioCallback;
        }

        public uint Step() {
            Arm7.Execute();
            Tick(4);
            return 8;
        }

        void Tick(uint cycles) {
            Lcd.Tick(cycles);
            // Audio.Tick(cycles);
        }
    }
}