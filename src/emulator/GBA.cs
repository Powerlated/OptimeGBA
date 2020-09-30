
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
            Mem = new Memory(this, provider);
            GbaAudio = new GBAAudio(this);
            Lcd = new LCD(this);
            Keypad = new Keypad();
            Dma = new DMA(this);
            Timers = new Timers(this);
            HwControl = new HWControl(this);

            Scheduler = new Scheduler();

            AudioCallback = provider.AudioCallback;
        }

        uint ExtraTicks = 0;
        public uint Step()
        {
            ExtraTicks = 0;
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
            return ticks + ExtraTicks;
        }

        public void Tick(uint cycles)
        {
            Lcd.Tick(cycles);
            Timers.Tick(cycles);
            GbaAudio.Tick(cycles);

            ExtraTicks += cycles;
        }
    }
}