using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public enum DMAStartTiming
    {
        Immediately = 0,
        VBlank = 1,
        HBlank = 2,
        Special = 3,
    }

    public enum DMADestAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        IncrementReload = 3,
    }

    public enum DMASrcAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        PROHIBITED = 3,
    }

    public class DMAChannel
    {
        public uint DMASAD;
        public uint DMADAD;
        public uint DMACNT_L;

        public uint DmaSource;
        public uint DmaDest;
        public uint DmaLength;

        // DMACNT_H
        public DMADestAddrCtrl DestAddrCtrl;
        public DMASrcAddrCtrl SrcAddrCtrl;
        public bool Repeat;
        public bool TransferType;
        public bool GamePakDRQ;
        public DMAStartTiming StartTiming;
        public bool FinishedIRQ;
        public bool Enabled; // Don't directly set to false, use Disable()

        public uint DMACNT_H;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x00: // DMASAD B0
                    val = 0; // Write only
                    break;
                case 0x01: // DMASAD B1
                    val = 0; // Write only
                    break;
                case 0x02: // DMASAD B2
                    val = 0; // Write only
                    break;
                case 0x03: // DMASAD B3
                    val = 0; // Write only
                    break;

                case 0x04: // DMADAD B0
                    val = 0; // Write only
                    break;
                case 0x05: // DMADAD B1
                    val = 0; // Write only
                    break;
                case 0x06: // DMADAD B2
                    val = 0; // Write only
                    break;
                case 0x07: // DMADAD B3
                    val = 0; // Write only
                    break;

                case 0x08: // DMACNT_L B0
                    val = 0; // Write only
                    break;
                case 0x09: // DMACNT_L B1
                    val = 0; // Write only
                    break;
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
            DestAddrCtrl = (DMADestAddrCtrl)BitRange(DMACNT_H, 5, 6);
            SrcAddrCtrl = (DMASrcAddrCtrl)BitRange(DMACNT_H, 7, 8);
            Repeat = BitTest(DMACNT_H, 9);
            TransferType = BitTest(DMACNT_H, 10);
            GamePakDRQ = BitTest(DMACNT_H, 11);
            StartTiming = (DMAStartTiming)BitRange(DMACNT_H, 12, 13);
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

    public class DMA
    {
        GBA Gba;
        public DMAChannel[] Ch = new DMAChannel[4] {
            new DMAChannel(),
            new DMAChannel(),
            new DMAChannel(),
            new DMAChannel(),
        };

        public DMA(GBA gba)
        {
            Gba = gba;
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
            throw new Exception("This shouldn't happen.");
        }

        public void ExecuteDma(DMAChannel c, uint ci)
        {
            // Least significant 28 (or 27????) bits
            c.DmaSource &= 0b1111111111111111111111111111;
            c.DmaDest &= 0b111111111111111111111111111;

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

            uint origLength = c.DmaLength;

            for (; c.DmaLength > 0; c.DmaLength--)
            {
                if (c.TransferType)
                {
                    Gba.Mem.Write32(c.DmaDest, Gba.Mem.Read32(c.DmaSource));
                    // Gba.Tick(ARM7.GetTiming32(srcAddr));

                    switch (c.DestAddrCtrl)
                    {
                        case DMADestAddrCtrl.Increment: c.DmaDest += 4; break;
                        case DMADestAddrCtrl.Decrement: c.DmaDest -= 4; break;
                        case DMADestAddrCtrl.Fixed: break;
                        case DMADestAddrCtrl.IncrementReload: c.DmaDest += 4; break;
                    }
                    switch (c.SrcAddrCtrl)
                    {
                        case DMASrcAddrCtrl.Increment: c.DmaSource += 4; break;
                        case DMASrcAddrCtrl.Decrement: c.DmaSource -= 4; break;
                        case DMASrcAddrCtrl.Fixed: break;
                    }
                }
                else
                {
                    Gba.Mem.Write16(c.DmaDest, Gba.Mem.Read16(c.DmaSource));
                    // Gba.Tick(ARM7.GetTiming8And16(srcAddr));

                    switch (c.DestAddrCtrl)
                    {
                        case DMADestAddrCtrl.Increment: c.DmaDest += 2; break;
                        case DMADestAddrCtrl.Decrement: c.DmaDest -= 2; break;
                        case DMADestAddrCtrl.Fixed: break;
                        case DMADestAddrCtrl.IncrementReload: c.DmaDest += 2; break;
                    }
                    switch (c.SrcAddrCtrl)
                    {
                        case DMASrcAddrCtrl.Increment: c.DmaSource += 2; break;
                        case DMASrcAddrCtrl.Decrement: c.DmaSource -= 2; break;
                        case DMASrcAddrCtrl.Fixed: break;
                    }
                }
            }

            if (c.DestAddrCtrl == DMADestAddrCtrl.IncrementReload)
            {
                c.DmaLength = origLength;

                if (c.Repeat)
                {
                    c.DmaDest = c.DMADAD;
                }
            }

            // if (c.FinishedIRQ && c.DmaLength == 0)
            // {
            //     Gba.HwControl.FlagInterrupt((Interrupt)((uint)Interrupt.DMA0 + ci));
            // }
        }

        public void ExecuteSoundDma(DMAChannel c, uint ci)
        {
            // Least significant 28 (or 27????) bits
            uint srcAddr = c.DmaSource & 0b1111111111111111111111111111;
            uint destAddr = c.DmaDest & 0b111111111111111111111111111;

            // 4 units of 32bits (16 bytes) are transferred to FIFO_A or FIFO_B
            for (uint i = 0; i < 4; i++)
            {
                byte b0 = Gba.Mem.Read8(srcAddr + 0);
                byte b1 = Gba.Mem.Read8(srcAddr + 1);
                byte b2 = Gba.Mem.Read8(srcAddr + 2);
                byte b3 = Gba.Mem.Read8(srcAddr + 3);
                if (destAddr == 0x40000A0)
                {
                    Gba.GbaAudio.A.Insert(b0);
                    Gba.GbaAudio.A.Insert(b1);
                    Gba.GbaAudio.A.Insert(b2);
                    Gba.GbaAudio.A.Insert(b3);
                }
                else if (destAddr == 0x40000A4)
                {
                    Gba.GbaAudio.B.Insert(b0);
                    Gba.GbaAudio.B.Insert(b1);
                    Gba.GbaAudio.B.Insert(b2);
                    Gba.GbaAudio.B.Insert(b3);
                }
                else
                {
                    Gba.Mem.Write8(destAddr + 0, b0);
                    Gba.Mem.Write8(destAddr + 1, b1);
                    Gba.Mem.Write8(destAddr + 2, b2);
                    Gba.Mem.Write8(destAddr + 3, b3);
                }
                // Gba.Tick(ARM7.GetTiming32(srcAddr));

                switch (c.SrcAddrCtrl)
                {
                    case DMASrcAddrCtrl.Increment: srcAddr += 4; break;
                    case DMASrcAddrCtrl.Decrement: srcAddr -= 4; break;
                    case DMASrcAddrCtrl.Fixed: break;
                }
            }

            c.DmaSource = srcAddr;

            // if (c.FinishedIRQ && c.DmaLength == 0)
            // {
            //     Gba.HwControl.FlagInterrupt((Interrupt)((uint)Interrupt.DMA0 + ci));
            // }
        }


        public void ExecuteImmediate(uint ci)
        {
            DMAChannel c = Ch[ci];

            if (c.Enabled && c.StartTiming == DMAStartTiming.Immediately)
            {
                c.Disable();

                ExecuteDma(c, ci);
            }
        }

        public void RepeatFifoA()
        {
            if (Ch[1].StartTiming == DMAStartTiming.Special)
            {
                ExecuteSoundDma(Ch[1], 1);
            }
        }
        public void RepeatFifoB()
        {
            if (Ch[2].StartTiming == DMAStartTiming.Special)
            {
                ExecuteSoundDma(Ch[2], 2);
            }
        }

        public void RepeatHblank()
        {
            for (uint ci = 0; ci < 4; ci++)
            {
                DMAChannel c = Ch[ci];
                if (c.StartTiming == DMAStartTiming.HBlank)
                {
                    c.DmaLength = c.DMACNT_L;
                    ExecuteDma(c, ci);
                }
            }
        }

        public void RepeatVblank()
        {
            for (uint ci = 0; ci < 4; ci++)
            {
                DMAChannel c = Ch[ci];
                if (c.StartTiming == DMAStartTiming.VBlank)
                {
                    c.DmaLength = c.DMACNT_L;
                    ExecuteDma(c, ci);
                }
            }
        }
    }
}