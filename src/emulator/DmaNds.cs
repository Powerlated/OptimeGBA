using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public enum DmaStartTimingNds
    {
        Immediately = 0,
        VBlank = 1,
        HBlank = 2,
        Special = 3,
    }

    public sealed class DmaChannelNds
    {
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
        public bool GamePakDRQ;
        public DmaStartTimingNds StartTiming;
        public bool FinishedIRQ;
        public bool Enabled; // Don't directly set to false, use Disable()

        public uint DMACNT_H;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x00: // DMASAD B0
                    return (byte)(DMASAD >> 0);
                case 0x01: // DMASAD B1
                    return (byte)(DMASAD >> 8);
                case 0x02: // DMASAD B2
                    return (byte)(DMASAD >> 16);
                case 0x03: // DMASAD B3
                    return (byte)(DMASAD >> 24);

                case 0x04: // DMADAD B0
                    return (byte)(DMADAD >> 0);
                case 0x05: // DMADAD B1
                    return (byte)(DMADAD >> 8);
                case 0x06: // DMADAD B2
                    return (byte)(DMADAD >> 16);
                case 0x07: // DMADAD B3
                    return (byte)(DMADAD >> 24);

                case 0x08: // DMACNT_L B0
                    return (byte)(DMACNT_L >> 0);
                case 0x09: // DMACNT_L B1
                    return (byte)(DMACNT_L >> 8);
                case 0x0A: // DMACNT_H B0
                    val |= (byte)(GetControl() >> 0);
                    break;
                case 0x0B: // DMACNT_H B1
                    val |= (byte)(GetControl() >> 8);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x00: // DMASAD B0
                    DMASAD &= 0xFFFFFF00;
                    DMASAD |= ((uint)val << 0);
                    break;
                case 0x01: // DMASAD B1
                    DMASAD &= 0xFFFF00FF;
                    DMASAD |= ((uint)val << 8);
                    break;
                case 0x02: // DMASAD B2
                    DMASAD &= 0xFF00FFFF;
                    DMASAD |= ((uint)val << 16);
                    break;
                case 0x03: // DMASAD B3
                    DMASAD &= 0x00FFFFFF;
                    DMASAD |= ((uint)val << 24);
                    break;

                case 0x04: // DMADAD B0
                    DMADAD &= 0xFFFFFF00;
                    DMADAD |= ((uint)val << 0);
                    break;
                case 0x05: // DMADAD B1
                    DMADAD &= 0xFFFF00FF;
                    DMADAD |= ((uint)val << 8);
                    break;
                case 0x06: // DMADAD B2
                    DMADAD &= 0xFF00FFFF;
                    DMADAD |= ((uint)val << 16);
                    break;
                case 0x07: // DMADAD B3
                    DMADAD &= 0x00FFFFFF;
                    DMADAD |= ((uint)val << 24);
                    break;

                case 0x08: // DMACNT_L B0
                    DMACNT_L &= 0xFF00;
                    DMACNT_L |= ((uint)val << 0);
                    break;
                case 0x09: // DMACNT_L B1
                    DMACNT_L &= 0x00FF;
                    DMACNT_L |= ((uint)val << 8);
                    break;
                case 0x0A: // DMACNT_H B0
                    DMACNT_H &= 0xFF00;
                    DMACNT_H |= ((uint)val << 0);
                    UpdateControl();
                    break;
                case 0x0B: // DMACNT_H B1
                    DMACNT_H &= 0x00FF;
                    DMACNT_H |= ((uint)val << 8);
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
            GamePakDRQ = BitTest(DMACNT_H, 11);
            StartTiming = (DmaStartTimingNds)BitRange(DMACNT_H, 12, 13);
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
            if (GamePakDRQ) val = BitSet(val, 11);
            val |= ((uint)StartTiming & 0b11) << 12;
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
        DeviceNds Device;
        Memory Mem;

        public DmaChannelNds[] Ch = new DmaChannelNds[4] {
            new DmaChannelNds(),
            new DmaChannelNds(),
            new DmaChannelNds(),
            new DmaChannelNds(),
        };

        static readonly uint[] DmaSourceMask = { 0x07FFFFFF, 0x0FFFFFFF, 0x0FFFFFFF, 0x0FFFFFFF };
        static readonly uint[] DmaDestMask = { 0x07FFFFFF, 0x07FFFFFF, 0x07FFFFFF, 0x0FFFFFFFF };

        public byte[] DmaFill = new byte[16];

        public bool DmaLock;

        public DmaNds(DeviceNds deviceNds, Memory mem)
        {
            Device = deviceNds;
            Mem = mem;
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
                Ch[0].WriteHwio8(addr - 0x40000B0, val);
                ExecuteImmediate(0);
                return;
            }
            else if (addr >= 0x40000BC && addr <= 0x40000C7)
            {
                Ch[1].WriteHwio8(addr - 0x40000BC, val);
                ExecuteImmediate(1);
                return;
            }
            else if (addr >= 0x40000C8 && addr <= 0x40000D3)
            {
                Ch[2].WriteHwio8(addr - 0x40000C8, val);
                ExecuteImmediate(2);
                return;
            }
            else if (addr >= 0x40000D4 && addr <= 0x40000DF)
            {
                Ch[3].WriteHwio8(addr - 0x40000D4, val);
                ExecuteImmediate(3);
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
            Console.WriteLine("NDS: Executing DMA");
            DmaLock = true;

            // Least significant 28 (or 27????) bits
            c.DmaSource &= DmaSourceMask[ci];
            c.DmaDest &= DmaDestMask[ci];

            if (ci == 3)
            {
                // DMA 3 is 16-bit length
                c.DmaLength &= 0b1111111111111111;
                // Value of zero is treated as maximum length
                if (c.DmaLength == 0) c.DmaLength = 0x10000;
            }
            else
            {
                // DMA 0-2 are 14-bit length
                c.DmaLength &= 0b11111111111111;
                // Value of zero is treated as maximum length
                if (c.DmaLength == 0) c.DmaLength = 0x4000;
            }

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
                c.DmaLength = origLength;

                if (c.Repeat)
                {
                    c.DmaDest = c.DMADAD;
                }
            }

            if (c.FinishedIRQ)
            {
                Device.HwControl.FlagInterrupt((uint)InterruptNds.Dma0 + ci);
            }

            DmaLock = false;
        }

        public void ExecuteImmediate(uint ci)
        {
            DmaChannelNds c = Ch[ci];

            if (c.Enabled && c.StartTiming == DmaStartTimingNds.Immediately)
            {
                c.Disable();

                ExecuteDma(c, ci);
            }
        }

        public void RepeatHblank()
        {
            if (!DmaLock)
            {
                for (uint ci = 0; ci < 4; ci++)
                {
                    DmaChannelNds c = Ch[ci];
                    if (c.StartTiming == DmaStartTimingNds.HBlank)
                    {
                        c.DmaLength = c.DMACNT_L;
                        ExecuteDma(c, ci);
                    }
                }
            }
        }

        public void RepeatVblank()
        {
            if (!DmaLock)
            {
                for (uint ci = 0; ci < 4; ci++)
                {
                    DmaChannelNds c = Ch[ci];
                    if (c.StartTiming == DmaStartTimingNds.VBlank)
                    {
                        c.DmaLength = c.DMACNT_L;
                        ExecuteDma(c, ci);
                    }
                }
            }
        }
    }
}