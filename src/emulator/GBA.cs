
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

        public Scheduler Scheduler;

        public AudioCallback AudioCallback;

        public uint[] registers = new uint[16];
        public GBA(GbaProvider provider)
        {
            Provider = provider;

            Arm7 = new ARM7(this);
            Mem = new Memory(this);
            GbaAudio = new GBAAudio(this);
            Lcd = new LCD(this);
            Keypad = new Keypad();
            Dma = new DMA(this);
            Timers = new Timers(this);
            HwControl = new HWControl(this);

            Scheduler = new Scheduler();

            provider.Bios.CopyTo(Mem.Bios, 0);
            provider.Rom.CopyTo(Mem.Rom, 0);
            AudioCallback = provider.AudioCallback;

        }

        uint HaltTime = 0;
        public uint Step()
        {
            uint ticks = Arm7.Execute();

            Lcd.Tick(ticks);
            Timers.Tick(ticks);
            GbaAudio.Tick(ticks);

            // Scheduler.CurrentTicks += ticks;
            // while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            // {
            //     long current = Scheduler.CurrentTicks;
            //     long next = Scheduler.NextEventTicks;
            //     Scheduler.PopFirstEvent().Callback(current - next);
            // }
            return ticks;
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