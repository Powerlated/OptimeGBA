
namespace OptimeGBA
{

    public class GBA
    {
        public GbaProvider Provider;

        public ARM7 Arm7;
        public Memory Mem;
        public GBAAudio GbaAudio;
        public LCD Lcd;
        public DMA Dma;
        public Keypad Keypad;
        public Timers Timers;
        public HWControl HwControl;

        public AudioCallback AudioCallback;

        public uint[] registers = new uint[16];
        public GBA(GbaProvider provider)
        {
            Arm7 = new ARM7(this);
            Mem = new Memory(this);
            GbaAudio = new GBAAudio(this);
            Lcd = new LCD(this);
            Keypad = new Keypad();
            Dma = new DMA(this);
            Timers = new Timers(this);
            HwControl = new HWControl(this);

            provider.Bios.CopyTo(Mem.Bios, 0);
            provider.Rom.CopyTo(Mem.Rom, 0);
            AudioCallback = provider.AudioCallback;

            Provider = provider;
        }

        public uint Step()
        {
            Arm7.Execute();

            Lcd.Tick(1);
            Timers.Tick(1);
            GbaAudio.Tick(1);

            return 1;
        }

        public void Tick(uint cycles)
        {
            Lcd.Tick(cycles);
            Timers.Tick(cycles);
            GbaAudio.Tick(cycles);

            // Audio.Tick(cycles);
        }
    }
}