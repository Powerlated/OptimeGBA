
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
            Mem = new Memory(this, provider);
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
            uint cycles = 1;
            // uint cycles = 2;
            Arm7.Execute();
            cycles = Arm7.PendingCycles;
            Arm7.PendingCycles = 0;

            Lcd.Tick(2);
            Timers.Tick(2);
            GbaAudio.Tick(2);

            return cycles;
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