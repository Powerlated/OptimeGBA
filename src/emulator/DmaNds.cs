using static OptimeGBA.Bits;
using static Util;
using System;


namespace OptimeGBA
{
    public enum DmaStartTimingNds9 : byte
    {
        Immediately = 0,
        VBlank = 1,
        HBlank = 2,
        UponRenderBegin = 3,
        MainMemoryDisplay = 4,
        Slot1 = 5,
        Slot2 = 6,
        GeometryCommandFifo = 7,
    }

    public enum DmaStartTimingNds7 : byte
    {
        Immediately = 0,
        VBlank = 1,
        Slot1 = 2,
        Misc = 3,
    }

    public sealed class DmaChannelNds
    {
        public bool Nds7;

        public DmaChannelNds(bool nds7)
        {
            Nds7 = nds7;
        }

        public uint DMASAD;
        public uint DMADAD;
        public uint DMACNT_L;

        public uint DmaSource;
        public uint DmaDest;
        public uint DmaLength;

        // DMACNT_H
        public DmaDestAddrCtrl DestAddrCtrl;
        public DmaSrcAddrCtrl SrcAddrCtrl;
        public bool Repeat;
        public bool TransferType;
        public byte StartTiming;
        public bool FinishedIRQ;
        public bool Enabled; // Don't directly set to false, use Disable()

        public uint DMACNT_H;

               public byte ReadHwio8(uint addr)
        {
            // DMASAD, DMADAD, and DMACNT_L are write-only
            byte val = 0;
            switch (addr)
            {
                case 0x0A: // DMACNT_H B0
                case 0x0B: // DMACNT_H B1
                    val = GetByteIn(GetControl(), addr & 1);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x00: // DMASAD B0
                case 0x01: // DMASAD B1
                case 0x02: // DMASAD B2
                case 0x03: // DMASAD B3
                    DMASAD = SetByteIn(DMASAD, val, addr & 3);
                    break;

                case 0x04: // DMADAD B0
                case 0x05: // DMADAD B1
                case 0x06: // DMADAD B2
                case 0x07: // DMADAD B3
                    DMADAD = SetByteIn(DMADAD, val, addr & 3);
                    break;

                case 0x08: // DMACNT_L B0
                case 0x09: // DMACNT_L B1
                    DMACNT_L = SetByteIn(DMACNT_L, val, addr & 1);
                    break;
                case 0x0A: // DMACNT_H B0
                case 0x0B: // DMACNT_H B1
                    DMACNT_H = SetByteIn(DMACNT_H, val, addr & 1);
                    UpdateControl();
                    break;
            }
        }

        public void UpdateControl()
        {
            DestAddrCtrl = (DmaDestAddrCtrl)BitRange(DMACNT_H, 5, 6);
            SrcAddrCtrl = (DmaSrcAddrCtrl)BitRange(DMACNT_H, 7, 8);
            Repeat = BitTest(DMACNT_H, 9);
            TransferType = BitTest(DMACNT_H, 10);
            if (!Nds7)
            {
                StartTiming = (byte)BitRange(DMACNT_H, 11, 13);
            }
            else
            {
                StartTiming = (byte)BitRange(DMACNT_H, 12, 13);
            }
            FinishedIRQ = BitTest(DMACNT_H, 14);
            if (BitTest(DMACNT_H, 15))
            {
                Enable();
            }
            else
            {
                Disable();
            }
        }

        public uint GetControl()
        {
            uint val = 0;
            val |= ((uint)DestAddrCtrl & 0b11) << 5;
            val |= ((uint)SrcAddrCtrl & 0b11) << 7;
            if (Repeat) val = BitSet(val, 9);
            if (TransferType) val = BitSet(val, 10);
            val |= ((uint)StartTiming & 0b111) << 11;
            if (FinishedIRQ) val = BitSet(val, 14);
            if (Enabled) val = BitSet(val, 15);

            DMACNT_H = val;

            return val;
        }

        public void Enable()
        {
            if (!Enabled)
            {
                DmaSource = DMASAD;
                DmaDest = DMADAD;
                DmaLength = DMACNT_L;
            }

            Enabled = true;
            GetControl();
        }

        public void Disable()
        {
            Enabled = false;
            GetControl();
        }
    }

    public unsafe sealed class DmaNds
    {
        bool Nds7;
        Memory Mem;
        HwControl HwControl;

        public DmaChannelNds[] Ch;
        static readonly uint[] DmaSourceMask = { 0x07FFFFFF, 0x0FFFFFFF, 0x0FFFFFFF, 0x0FFFFFFF };
        static readonly uint[] DmaDestMask = { 0x07FFFFFF, 0x07FFFFFF, 0x07FFFFFF, 0x0FFFFFFFF };

        public byte[] DmaFill = new byte[16];

        public bool DmaLock;

        public DmaNds(bool nds7, Memory mem, HwControlNds hwControl)
        {
            Nds7 = nds7;
            Mem = mem;
            HwControl = hwControl;

            Ch = new DmaChannelNds[4] {
                new DmaChannelNds(Nds7),
                new DmaChannelNds(Nds7),
                new DmaChannelNds(Nds7),
                new DmaChannelNds(Nds7),
            };
        }

        public byte ReadHwio8(uint addr)
        {
            if (addr >= 0x40000B0 && addr <= 0x40000BB)
            {
                return Ch[0].ReadHwio8(addr - 0x40000B0);
            }
            else if (addr >= 0x40000BC && addr <= 0x40000C7)
            {
                return Ch[1].ReadHwio8(addr - 0x40000BC);
            }
            else if (addr >= 0x40000C8 && addr <= 0x40000D3)
            {
                return Ch[2].ReadHwio8(addr - 0x40000C8);
            }
            else if (addr >= 0x40000D4 && addr <= 0x40000DF)
            {
                return Ch[3].ReadHwio8(addr - 0x40000D4);
            }
            else if (addr >= 0x40000E0 && addr <= 0x40000EF)
            {
                return DmaFill[addr & 0xF];
            }
            throw new Exception("This shouldn't happen.");
        }

        public void WriteHwio8(uint addr, byte val)
        {
            if (addr >= 0x40000B0 && addr <= 0x40000BB)
            {
                bool oldEnabled = Ch[0].Enabled;
                Ch[0].WriteHwio8(addr - 0x40000B0, val);
                if (!oldEnabled && Ch[0].Enabled) ExecuteImmediate(0);
                return;
            }
            else if (addr >= 0x40000BC && addr <= 0x40000C7)
            {
                bool oldEnabled = Ch[1].Enabled;
                Ch[1].WriteHwio8(addr - 0x40000BC, val);
                if (!oldEnabled && Ch[1].Enabled) ExecuteImmediate(1);
                return;
            }
            else if (addr >= 0x40000C8 && addr <= 0x40000D3)
            {
                bool oldEnabled = Ch[2].Enabled;
                Ch[2].WriteHwio8(addr - 0x40000C8, val);
                if (!oldEnabled && Ch[2].Enabled) ExecuteImmediate(2);
                return;
            }
            else if (addr >= 0x40000D4 && addr <= 0x40000DF)
            {
                bool oldEnabled = Ch[3].Enabled;
                Ch[3].WriteHwio8(addr - 0x40000D4, val);
                if (!oldEnabled && Ch[3].Enabled) ExecuteImmediate(3);
                return;
            }
            else if (addr >= 0x40000E0 && addr <= 0x40000EF)
            {
                DmaFill[addr & 0xF] = val;
                return;
            }
            throw new Exception("This shouldn't happen.");
        }

        public void ExecuteDma(DmaChannelNds c, uint ci)
        {

            DmaLock = true;

            // Console.WriteLine("NDS: Executing DMA");
            // Console.WriteLine("Source: " + Util.Hex(c.DmaSource, 8));
            // Console.WriteLine("Dest: " + Util.Hex(c.DmaDest, 8));
            // Console.WriteLine("Length: " + c.DmaLength);

            if (!Nds7)
            {
                c.DmaSource &= 0x0FFFFFFF;
                c.DmaDest &= 0x0FFFFFFF;

                // All NDS9 DMAs use 21-bit length
                c.DmaLength &= 0x1FFFFF;
                // Value of zero is treated as maximum length
                if (c.DmaLength == 0) c.DmaLength = 0x200000;
            }
            else
            {
                // Least significant 28 (or 27????) bits
                c.DmaSource &= DmaSourceMask[ci];
                c.DmaDest &= DmaDestMask[ci];

                if (ci == 3)
                {
                    // DMA 3 is 16-bit length
                    c.DmaLength &= 0xFFFF;
                    // Value of zero is treated as maximum length
                    if (c.DmaLength == 0) c.DmaLength = 0x10000;
                }
                else
                {
                    // DMA 0-2 are 14-bit length
                    c.DmaLength &= 0x3FFF;
                    // Value of zero is treated as maximum length
                    if (c.DmaLength == 0) c.DmaLength = 0x4000;
                }
            }

            // if (c.DmaLength != 1 && ci == 3)
            // {
            //     Console.WriteLine(((DmaStartTimingNds7)c.StartTiming).ToString());
            //     Console.WriteLine("DMA length " + c.DmaLength);
            // }

            // Console.WriteLine($"Starting DMA {ci}"); 
            // Console.WriteLine($"SRC: {Util.HexN(srcAddr, 7)}");
            // Console.WriteLine($"DEST: {Util.HexN(destAddr, 7)}");
            // Console.WriteLine($"LENGTH: {Util.HexN(c.DmaLength, 4)}");

            int destOffsPerUnit;
            int sourceOffsPerUnit;
            if (c.TransferType)
            {
                switch (c.DestAddrCtrl)
                {
                    case DmaDestAddrCtrl.Increment: destOffsPerUnit = +4; break;
                    case DmaDestAddrCtrl.Decrement: destOffsPerUnit = -4; break;
                    case DmaDestAddrCtrl.IncrementReload: destOffsPerUnit = +4; break;
                    default: destOffsPerUnit = 0; break;
                }
                switch (c.SrcAddrCtrl)
                {
                    case DmaSrcAddrCtrl.Increment: sourceOffsPerUnit = +4; break;
                    case DmaSrcAddrCtrl.Decrement: sourceOffsPerUnit = -4; break;
                    default: sourceOffsPerUnit = 0; break;
                }
            }
            else
            {
                switch (c.DestAddrCtrl)
                {
                    case DmaDestAddrCtrl.Increment: destOffsPerUnit = +2; break;
                    case DmaDestAddrCtrl.Decrement: destOffsPerUnit = -2; break;
                    case DmaDestAddrCtrl.IncrementReload: destOffsPerUnit = +2; break;
                    default: destOffsPerUnit = 0; break;
                }
                switch (c.SrcAddrCtrl)
                {
                    case DmaSrcAddrCtrl.Increment: sourceOffsPerUnit = +2; break;
                    case DmaSrcAddrCtrl.Decrement: sourceOffsPerUnit = -2; break;
                    default: sourceOffsPerUnit = 0; break;
                }
            }

            uint origLength = c.DmaLength;

            // TODO: NDS DMA timings
            if (c.TransferType)
            {
                for (; c.DmaLength > 0; c.DmaLength--)
                {
                    Mem.Write32(c.DmaDest & ~3u, Mem.Read32(c.DmaSource & ~3u));
                    // Gba.Tick(Gba.Cpu.Timing32[(c.DmaSource >> 24) & 0xF]);
                    // Gba.Tick(Gba.Cpu.Timing32[(c.DmaDest >> 24) & 0xF]);

                    c.DmaDest = (uint)(long)(destOffsPerUnit + c.DmaDest);
                    c.DmaSource = (uint)(long)(sourceOffsPerUnit + c.DmaSource);
                }
            }
            else
            {
                for (; c.DmaLength > 0; c.DmaLength--)
                {
                    Mem.Write16(c.DmaDest & ~1u, Mem.Read16(c.DmaSource & ~1u));
                    // Gba.Tick(Nds.Timing8And16[(c.DmaSource >> 24) & 0xF]);
                    // Gba.Tick(Nds.Timing8And16[(c.DmaDest >> 24) & 0xF]);

                    c.DmaDest = (uint)(long)(destOffsPerUnit + c.DmaDest);
                    c.DmaSource = (uint)(long)(sourceOffsPerUnit + c.DmaSource);
                }
            }

            if (c.DestAddrCtrl == DmaDestAddrCtrl.IncrementReload)
            {
                if (c.Repeat)
                {
                    c.DmaDest = c.DMADAD;
                }
            }


            if (c.FinishedIRQ)
            {
                HwControl.FlagInterrupt((uint)InterruptNds.Dma0 + ci);
            }

            DmaLock = false;
        }

        public void ExecuteImmediate(uint ci)
        {
            DmaChannelNds c = Ch[ci];
            // Console.WriteLine($"NDS{(Nds9 ? "9" : "7")}: Ch{ci} immediate DMA from:{Hex(c.DMASAD, 8)} to:{Hex(c.DMADAD, 8)}");

            if (c.Enabled && c.StartTiming == (byte)DmaStartTimingNds9.Immediately)
            {
                c.Disable();

                ExecuteDma(c, ci);
            }
        }

        public bool Repeat(byte val)
        {
            bool executed = false;
            if (!DmaLock)
            {
                for (uint ci = 0; ci < 4; ci++)
                {
                    DmaChannelNds c = Ch[ci];
                    if (c.StartTiming == val)
                    {
                        executed = true;
                        c.DmaLength = c.DMACNT_L;
                        ExecuteDma(c, ci);
                    }
                }
            }
            return executed;
        }
    }
}