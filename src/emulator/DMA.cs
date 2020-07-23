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
                    DMASAD &= 0x000000FF;
                    DMASAD |= ((uint)val << 0);
                    break;
                case 0x01: // DMASAD B1
                    DMASAD &= 0x0000FF00;
                    DMASAD |= ((uint)val << 8);
                    break;
                case 0x02: // DMASAD B2
                    DMASAD &= 0x00FF0000;
                    DMASAD |= ((uint)val << 16);
                    break;
                case 0x03: // DMASAD B3
                    DMASAD &= 0xFF000000;
                    DMASAD |= ((uint)val << 24);
                    break;

                case 0x04: // DMADAD B0
                    DMADAD &= 0x000000FF;
                    DMADAD |= ((uint)val << 0);
                    break;
                case 0x05: // DMADAD B1
                    DMADAD &= 0x0000FF00;
                    DMADAD |= ((uint)val << 8);
                    break;
                case 0x06: // DMADAD B2
                    DMADAD &= 0x00FF0000;
                    DMADAD |= ((uint)val << 16);
                    break;
                case 0x07: // DMADAD B3
                    DMADAD &= 0xFF000000;
                    DMADAD |= ((uint)val << 24);
                    break;

                case 0x08: // DMACNT_L B0
                    DMACNT_L &= 0x00FF;
                    DMACNT_L |= ((uint)val << 0);
                    break;
                case 0x09: // DMACNT_L B1
                    DMACNT_L &= 0xFF00;
                    DMACNT_L |= ((uint)val << 8);
                    break;
                case 0x0A: // DMACNT_H B0
                    DMACNT_H &= 0x00FF;
                    DMACNT_H |= ((uint)val << 0);
                    UpdateControl();
                    break;
                case 0x0B: // DMACNT_H B1
                    DMACNT_H &= 0xFF00;
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
                return;
            }
            else if (addr >= 0x40000BC && addr <= 0x40000C7)
            {
                Ch[1].WriteHwio8(addr - 0x40000BC, val);
                return;
            }
            else if (addr >= 0x40000C8 && addr <= 0x40000D3)
            {
                Ch[2].WriteHwio8(addr - 0x40000C8, val);
                return;
            }
            else if (addr >= 0x40000D4 && addr <= 0x40000DF)
            {
                Ch[3].WriteHwio8(addr - 0x40000D4, val);
                return;
            }
            throw new Exception("This shouldn't happen.");
        }

        public void Tick(uint cycles)
        {
            ExecuteImmediates();
        }

        public void ExecuteImmediates()
        {
            for (uint ci = 0; ci < 4; ci++)
            {
                DMAChannel c = Ch[ci];

                if (c.Enabled && c.StartTiming == DMAStartTiming.Immediately)
                {
                    c.Disable();

                    // Least significant 28 (or 27????) bits
                    uint srcAddr = c.DmaSource & 0b1111111111111111111111111111;
                    uint destAddr = c.DmaDest & 0b1111111111111111111111111111;

                    uint origSrcAddr = srcAddr;
                    uint origDestAddr = destAddr;

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

                    uint origLength = c.DmaLength;

                    for (; c.DmaLength > 0; c.DmaLength--)
                    {
                        if (c.TransferType)
                        {
                            Gba.Mem.Write32(destAddr, Gba.Mem.Read32(srcAddr));

                            switch (c.DestAddrCtrl)
                            {
                                case DMADestAddrCtrl.Increment: destAddr += 4; break;
                                case DMADestAddrCtrl.Decrement: destAddr -= 4; break;
                                case DMADestAddrCtrl.Fixed: break;
                                case DMADestAddrCtrl.IncrementReload: destAddr += 4; break;
                            }
                            switch (c.SrcAddrCtrl)
                            {
                                case DMASrcAddrCtrl.Increment: srcAddr += 4; break;
                                case DMASrcAddrCtrl.Decrement: srcAddr -= 4; break;
                                case DMASrcAddrCtrl.Fixed: break;
                            }
                        }
                        else
                        {
                            Gba.Mem.Write16(destAddr, Gba.Mem.Read16(srcAddr));

                            switch (c.DestAddrCtrl)
                            {
                                case DMADestAddrCtrl.Increment: destAddr += 2; break;
                                case DMADestAddrCtrl.Decrement: destAddr -= 2; break;
                                case DMADestAddrCtrl.Fixed: break;
                                case DMADestAddrCtrl.IncrementReload: destAddr += 2; break;
                            }
                            switch (c.SrcAddrCtrl)
                            {
                                case DMASrcAddrCtrl.Increment: srcAddr += 2; break;
                                case DMASrcAddrCtrl.Decrement: srcAddr -= 2; break;
                                case DMASrcAddrCtrl.Fixed: break;
                            }
                        }
                    }

                    if (c.DestAddrCtrl == DMADestAddrCtrl.IncrementReload)
                    {
                        c.DmaLength = origLength;

                        if (c.Repeat)
                        {
                            c.DmaDest = origDestAddr;
                        }
                    }
                }
            }
        }
    }
}