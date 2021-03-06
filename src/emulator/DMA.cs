using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public enum DmaStartTiming
    {
        Immediately = 0,
        VBlank = 1,
        HBlank = 2,
        Special = 3,
    }

    public enum DmaDestAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        IncrementReload = 3,
    }

    public enum DmaSrcAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        PROHIBITED = 3,
    }

    public sealed class DmaChannel
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
        public DmaStartTiming StartTiming;
        public bool FinishedIRQ;
        public bool Enabled; // Don't directly set to false, use Disable()

        public uint DMACNT_H;

        public byte ReadHwio8(uint addr) => addr switch
        {
            0x00 or // DMASAD B0
            0x01 or // DMASAD B1
            0x02 or // DMASAD B2
            0x03 or // DMASAD B3
            0x04 or // DMADAD B0
            0x05 or // DMADAD B1
            0x06 or // DMADAD B2
            0x07 or // DMADAD B3
            0x08 or // DMACNT_L B0
            0x09 // DMACNT_L B1
            => 0, // Write only
            0x0A => (byte)(GetControl() >> 0), // DMACNT_H B0
            0x0B => (byte)(GetControl() >> 8)
        };

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
            StartTiming = (DmaStartTiming)BitRange(DMACNT_H, 12, 13);
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

    public sealed class Dma
    {
        Gba Gba;

        public DmaChannel[] Ch = new DmaChannel[4] {
            new DmaChannel(),
            new DmaChannel(),
            new DmaChannel(),
            new DmaChannel(),
        };

        static readonly uint[] DmaSourceMask = { 0x07FFFFFF, 0x0FFFFFFF, 0x0FFFFFFF, 0x0FFFFFFF };
        static readonly uint[] DmaDestMask = { 0x07FFFFFF, 0x07FFFFFF, 0x07FFFFFF, 0x0FFFFFFFF };

        public bool DmaLock;

        public Dma(Gba gba)
        {
            Gba = gba;
        }

        public byte ReadHwio8(uint addr)
        {
            if (addr is >= 0x40000B0 and <= 0x40000BB)
            {
                return Ch[0].ReadHwio8(addr - 0x40000B0);
            }
            else if (addr is 0x40000BC and <= 0x40000C7)
            {
                return Ch[1].ReadHwio8(addr - 0x40000BC);
            }
            else if (addr is >= 0x40000C8 and <= 0x40000D3)
            {
                return Ch[2].ReadHwio8(addr - 0x40000C8);
            }
            else if (addr is >= 0x40000D4 and <= 0x40000DF)
            {
                return Ch[3].ReadHwio8(addr - 0x40000D4);
            }
            throw new Exception("This shouldn't happen.");
        }

        public void WriteHwio8(uint addr, byte val)
        {
            if (addr is >= 0x40000B0 and <= 0x40000BB)
            {
                Ch[0].WriteHwio8(addr - 0x40000B0, val);
                ExecuteImmediate(0);
                return;
            }
            else if (addr is >= 0x40000BC and <= 0x40000C7)
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

        public void ExecuteDma(DmaChannel c, uint ci)
        {
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


            if (c.TransferType)
            {
                for (; c.DmaLength > 0; c.DmaLength--)
                {
                    Gba.Mem.Write32(c.DmaDest & ~3u, Gba.Mem.Read32(c.DmaSource & ~3u));
                    Gba.Tick(Arm7.Timing32[(c.DmaSource >> 24) & 0xF]);
                    Gba.Tick(Arm7.Timing32[(c.DmaDest >> 24) & 0xF]);

                    c.DmaDest = (uint)(long)(destOffsPerUnit + c.DmaDest);
                    c.DmaSource = (uint)(long)(sourceOffsPerUnit + c.DmaSource);
                }
            }
            else
            {
                for (; c.DmaLength > 0; c.DmaLength--)
                {
                    Gba.Mem.Write16(c.DmaDest & ~1u, Gba.Mem.Read16(c.DmaSource & ~1u));
                    Gba.Tick(Arm7.Timing8And16[(c.DmaSource >> 24) & 0xF]);
                    Gba.Tick(Arm7.Timing8And16[(c.DmaDest >> 24) & 0xF]);

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
                Gba.HwControl.FlagInterrupt((Interrupt)((uint)Interrupt.DMA0 + ci));
            }

            DmaLock = false;
        }

        public void ExecuteSoundDma(DmaChannel c, uint ci)
        {
            DmaLock = true;

            // Least significant 28 (or 27????) bits
            uint srcAddr = c.DmaSource & 0b1111111111111111111111111111;
            uint destAddr = c.DmaDest & 0b111111111111111111111111111;

            // 4 units of 32bits (16 bytes) are transferred to FIFO_A or FIFO_B
            for (uint i = 0; i < 4; i++)
            {
                uint val = Gba.Mem.Read32(srcAddr + 0);
                if (destAddr == 0x40000A0)
                {
                    Gba.GbaAudio.A.Insert((byte)val);
                    Gba.GbaAudio.A.Insert((byte)(val >>= 8));
                    Gba.GbaAudio.A.Insert((byte)(val >>= 8));
                    Gba.GbaAudio.A.Insert((byte)(val >>= 8));
                }
                else if (destAddr == 0x40000A4)
                {
                    Gba.GbaAudio.B.Insert((byte)val);
                    Gba.GbaAudio.B.Insert((byte)(val >>= 8));
                    Gba.GbaAudio.B.Insert((byte)(val >>= 8));
                    Gba.GbaAudio.B.Insert((byte)(val >>= 8));
                }
                else
                {
                    Gba.Mem.Write8(destAddr + 0, (byte)val); 
                    Gba.Mem.Write8(destAddr + 1, (byte)(val >>= 8)); 
                    Gba.Mem.Write8(destAddr + 2, (byte)(val >>= 8)); 
                    Gba.Mem.Write8(destAddr + 3, (byte)(val >>= 8));
                }

                switch (c.SrcAddrCtrl)
                {
                    case DmaSrcAddrCtrl.Increment: srcAddr += 4; break;
                    case DmaSrcAddrCtrl.Decrement: srcAddr -= 4; break;
                    case DmaSrcAddrCtrl.Fixed: break;
                }

                // Applying proper timing to sound DMAs causes crackling in certain games including PMD.
                // This only happens with scheduled timers, which leads me to believe the real problem is in there.
                // PROBLEM SOLVED.... my timers were 1 cycle too slow to reload
                Gba.Arm7.InstructionCycles += (Arm7.Timing32[(c.DmaSource >> 24) & 0xF]);
                Gba.Arm7.InstructionCycles += (Arm7.Timing32[(c.DmaDest >> 24) & 0xF]);
            }

            c.DmaSource = srcAddr;

            if (c.FinishedIRQ)
            {
                Gba.HwControl.FlagInterrupt((Interrupt)((uint)Interrupt.DMA0 + ci));
            }

            DmaLock = false;
        }


        public void ExecuteImmediate(uint ci)
        {
            DmaChannel c = Ch[ci];

            if (c.Enabled && c.StartTiming == DmaStartTiming.Immediately)
            {
                c.Disable();

                ExecuteDma(c, ci);
            }
        }

        public void RepeatFifoA()
        {
            if (!DmaLock)
            {
                if (Ch[1].StartTiming == DmaStartTiming.Special)
                {
                    ExecuteSoundDma(Ch[1], 1);
                }
            }
        }
        public void RepeatFifoB()
        {
            if (!DmaLock)
            {
                if (Ch[2].StartTiming == DmaStartTiming.Special)
                {
                    ExecuteSoundDma(Ch[2], 2);
                }
            }
        }

        public void RepeatHblank()
        {
            if (!DmaLock)
            {
                for (uint ci = 0; ci < 4; ci++)
                {
                    DmaChannel c = Ch[ci];
                    if (c.StartTiming == DmaStartTiming.HBlank)
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
                    DmaChannel c = Ch[ci];
                    if (c.StartTiming == DmaStartTiming.VBlank)
                    {
                        c.DmaLength = c.DMACNT_L;
                        ExecuteDma(c, ci);
                    }
                }
            }
        }
    }
}