using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public sealed unsafe class PpuNds3D
    {
        Nds Nds;
        Scheduler Scheduler;

        public PpuNds3D(Nds nds, Scheduler scheduler)
        {
            Nds = nds;
            Scheduler = scheduler;
        }

        // GXSTAT
        public byte CommandFifoIrqMode;

        // GXFIFO
        public uint PendingCommand;
        public CircularBuffer<ulong> CommandFifo = new CircularBuffer<ulong>(256, 0);

        public long LastTime;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000600:
                    break;
                case 0x4000601:
                    break;
                case 0x4000602:
                    val = (byte)CommandFifo.Entries;
                    break;
                case 0x4000603:
                    if (CommandFifo.Entries == 256) val = BitSet(val, 0);
                    if (CommandFifo.Entries < 128) val = BitSet(val, 1);
                    if (CommandFifo.Entries == 0) val = BitSet(val, 2);
                    val |= (byte)(CommandFifoIrqMode << 6);
                    break;
            }

            return val;
        }

        uint x;

        public void WriteHwio8(uint addr, byte val)
        {
            if (addr >= 0x4000404 && addr < 0x40005CC)
            {
                QueueCommand(0);
            }

            switch (addr)
            {
                case 0x4000400:
                case 0x4000401:
                case 0x4000402:
                    PendingCommand = SetByteIn(PendingCommand, val, addr & 3);
                    break;
                case 0x4000403:
                    PendingCommand = SetByteIn(PendingCommand, val, addr & 3);
                    QueueCommand(PendingCommand);
                    return;

                case 0x4000600:
                    return;
                case 0x4000601:
                    return;
                case 0x4000602:
                    return;
                case 0x4000603:
                    CommandFifoIrqMode = (byte)((val >> 6) & 0b11);
                    return;
            }
        }

        public void QueueCommand(ulong command)
        {
            CommandFifo.Insert(command);
        }

        public void Run()
        {
            if (CommandFifo.Entries > 0)
            {
                ulong cmd = CommandFifo.Pop();

                if (
                    (CommandFifo.Entries == 0 && CommandFifoIrqMode == 2) ||
                    (CommandFifo.Entries < 128 && CommandFifoIrqMode == 1)
                    )
                {
                    Nds.HwControl9.FlagInterrupt((uint)InterruptNds.GeometryFifo);
                }
            }
        }
    }
}
