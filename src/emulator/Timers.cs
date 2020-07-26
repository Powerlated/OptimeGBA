using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class Timer
    {
        public uint CounterVal = 0;
        public uint ReloadVal = 0;

        public static readonly uint[] PrescalerDivs = {
            1, 64, 256, 1024
        };

        public uint Prescaler = 0;
        public uint PrescalerDiv = PrescalerDivs[0];

        public uint PrescalerSel = 0;
        public bool CountUpTiming = false;
        public bool EnableIrq = false;
        public bool Enabled = false;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x00: // TMCNT_L B0
                    val = (byte)(CounterVal >> 0);
                    break;
                case 0x01: // TMCNT_L B1
                    val = (byte)(CounterVal >> 8);
                    break;
                case 0x02: // TMCNT_H B0
                    val |= (byte)(PrescalerSel & 0b11);
                    if (CountUpTiming) val = BitSet(val, 2);
                    if (EnableIrq) val = BitSet(val, 6);
                    if (Enabled) val = BitSet(val, 7);
                    break;
                case 0x03: // TMCNT_H B1
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x00: // TMCNT_L B0
                    ReloadVal &= 0xFF00;
                    ReloadVal |= ((uint)val << 0);
                    break;
                case 0x01: // TMCNT_L B1
                    ReloadVal &= 0x00FF;
                    ReloadVal |= ((uint)val << 8);
                    break;
                case 0x02: // TMCNT_H B0
                    PrescalerSel = (uint)(val & 0b11);
                    PrescalerDiv = PrescalerDivs[PrescalerSel];
                    CountUpTiming = BitTest(val, 2);
                    EnableIrq = BitTest(val, 6);
                    if (BitTest(val, 7))
                    {
                        Enable();
                    }
                    else
                    {
                        Disable();
                    }
                    break;
                case 0x03: // TMCNT_H B1
                    break;
            }
        }

        public void Enable()
        {
            if (!Enabled)
            {
                Reload();
            }

            Enabled = true;
        }

        public void Disable()
        {
            Enabled = false;
        }

        public void Reload()
        {
            CounterVal = ReloadVal;
        }
    }

    public class Timers
    {
        GBA Gba;

        public Timers(GBA gba)
        {
            Gba = gba;
        }

        public Timer[] T = new Timer[4] {
            new Timer(),
            new Timer(),
            new Timer(),
            new Timer(),
        };

        public byte ReadHwio8(uint addr)
        {
            if (addr >= 0x4000100 && addr <= 0x4000103)
            {
                return T[0].ReadHwio8(addr - 0x4000100);
            }
            else if (addr >= 0x4000104 && addr <= 0x4000107)
            {
                return T[1].ReadHwio8(addr - 0x4000104);
            }
            else if (addr >= 0x4000108 && addr <= 0x400010B)
            {
                return T[2].ReadHwio8(addr - 0x4000108);
            }
            else if (addr >= 0x400010C && addr <= 0x400010F)
            {
                return T[3].ReadHwio8(addr - 0x400010C);
            }
            throw new Exception("This shouldn't happen.");
        }

        public void WriteHwio8(uint addr, byte val)
        {
            if (addr >= 0x4000100 && addr <= 0x4000103)
            {
                T[0].WriteHwio8(addr - 0x4000100, val);
                return;
            }
            else if (addr >= 0x4000104 && addr <= 0x4000107)
            {
                T[1].WriteHwio8(addr - 0x4000104, val);
                return;
            }
            else if (addr >= 0x4000108 && addr <= 0x400010B)
            {
                T[2].WriteHwio8(addr - 0x4000108, val);
                return;
            }
            else if (addr >= 0x400010C && addr <= 0x400010F)
            {
                T[3].WriteHwio8(addr - 0x400010C, val);
                return;
            }
            throw new Exception("This shouldn't happen.");
        }

        public void Tick(uint cycles)
        {
            for (uint ti = 0; ti < 4; ti++)
            {
                Timer t = T[ti];

                // if (t.CountUpTiming) throw new Exception("Implement timer cascading");

                if (t.Enabled)
                {
                    t.Prescaler += cycles;
                    while (t.Prescaler >= t.PrescalerDiv)
                    {
                        t.Prescaler -= t.PrescalerDiv;

                        t.CounterVal++;
                        if (t.CounterVal > 0xFFFF)
                        {
                            // On overflow, refill with reload value
                            t.CounterVal = t.ReloadVal;

                            if (ti < 2)
                            {
                                Gba.GbaAudio.TimerOverflow(ti);
                            }
                        }
                    }
                }
            }
        }
    }
}