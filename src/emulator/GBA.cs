public delegate void Callback();

namespace OptimeGBA
{

    public class GBA
    {
        public ARM7 Arm7;
        public Memory Mem;
        public GBAAudio GbaAudio;
        public LCD Lcd;
        public DMA Dma;
        public Keypad Keypad;
        public HWControl HwControl;

        public uint[] registers = new uint[16];
        public Callback AudioCallback;
        public GBA(GbaRomProvider romProvider, Callback audioCallback)
        {
            Arm7 = new ARM7(this);
            Mem = new Memory(this, romProvider);
            GbaAudio = new GBAAudio(this);
            Lcd = new LCD(this);
            Keypad = new Keypad();
            Dma = new DMA(this);
            HwControl = new HWControl(this);

            AudioCallback = audioCallback;
        }

        public uint Step() {
            Arm7.Execute();
            Tick(4);
            return 4;
        }

        void Tick(uint cycles) {
            Lcd.Tick(cycles);
            Dma.Tick(cycles);
            // Audio.Tick(cycles);
        }
    }
}