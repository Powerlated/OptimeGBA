using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public sealed class Timer
    {
        public uint Id = 0;
        public Timers Timers;

        public uint CounterVal = 0;
        public uint ReloadVal = 0;

        public long EnableCycles = 0;
        public long Interval;

        public static readonly uint[] PrescalerDivs = {
            1, 64, 256, 1024
        };

        public uint Prescaler = 0;
        public uint PrescalerDiv = PrescalerDivs[0];

        public uint PrescalerSel = 0;
        public bool CountUpTiming = false;
        public bool EnableIrq = false;
        public bool Enabled = false;

        public Timer(Timers timers, uint id)
        {
            Id = id;
            Timers = timers;
        }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x00: // TMCNT_L B0
                    val = (byte)(CalculateCounter() >> 0);
                    break;
                case 0x01: // TMCNT_L B1
                    val = (byte)(CalculateCounter() >> 8);
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
                    RecalculateInterval();
                    break;
                case 0x01: // TMCNT_L B1
                    ReloadVal &= 0x00FF;
                    ReloadVal |= ((uint)val << 8);
                    RecalculateInterval();
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
                    RecalculateInterval();
                    break;
                case 0x03: // TMCNT_H B1
                    break;
            }
        }

        public SchedulerId GetSchedulerId() {
            uint id = (uint)SchedulerId.Timer90 + Id;
            if (Timers.IsNds7) id += 4;

            return (SchedulerId)id;
        }

        public void Enable()
        {
            if (!Enabled)
            {
                Reload();
                Timers.Scheduler.AddEventRelative(GetSchedulerId(), CalculateOverflowCycles(), TimerOverflow);
                EnableCycles = CalculateAlignedCurrentTicks();
                // Console.WriteLine($"[Timer] {Id} Enable");
            }

            Enabled = true;
        }

        public uint CalculateCounter()
        {
            long diff = Timers.Scheduler.CurrentTicks - EnableCycles;
            diff /= PrescalerDiv;

            if (Enabled)
            {
                return (ushort)(CounterVal + diff);
            }
            else
            {
                return (ushort)CounterVal;
            }
        }

        public void Reschedule()
        {
            if (Enabled)
            {
                CounterVal = ReloadVal;
                EnableCycles = CalculateAlignedCurrentTicks();

                Timers.Scheduler.CancelEventsById(GetSchedulerId());
                Timers.Scheduler.AddEventRelative(GetSchedulerId(), CalculateOverflowCycles(), TimerOverflow);
            }
        }

        public void RecalculateInterval()
        {
            uint max = 0x10000;
            uint diff = max - ReloadVal;

            Interval = diff * PrescalerDivs[PrescalerSel];
        }

        public long CalculateOverflowCycles()
        {
            uint max = 0xFFFF;
            uint diff = max - CounterVal;

            // Align to the master clock
            uint prescalerMod = diff % PrescalerDiv;
            diff -= prescalerMod;
            diff += PrescalerDiv;

            return diff * PrescalerDiv;
        }

        public long CalculateAlignedCurrentTicks()
        {
            long ticks = Timers.Scheduler.CurrentTicks;
            long prescalerMod = Timers.Scheduler.CurrentTicks % PrescalerDiv;
            ticks -= prescalerMod;
            ticks += PrescalerDiv;

            return ticks;
        }

        public void Disable()
        {
            if (Enabled)
            {
                CounterVal = CalculateCounter();
                Enabled = false;

                Timers.Scheduler.CancelEventsById(GetSchedulerId());
            }
        }

        public void Reload()
        {
            CounterVal = ReloadVal;
        }

        public void TimerOverflow(long cyclesLate)
        {
            // On overflow, refill with reload value
            CounterVal = ReloadVal;

            if (!Timers.NdsMode)
            {
                var gba = (Gba)Timers.Device;
                if (EnableIrq)
                {
                    gba.HwControl.FlagInterrupt((uint)InterruptGba.Timer0Overflow + Id);
                }

                if (Id < 2)
                {
                    gba.GbaAudio.TimerOverflow(cyclesLate, Id);
                }
            }
            else
            {
                var nds = (DeviceNds)Timers.Device;
                nds.HwControl.FlagInterrupt((uint)InterruptNds.Timer0Overflow + Id);
            }

            if (Id < 3)
            {
                if (Timers.T[Id + 1].CountUpTiming)
                {
                    UnscheduledTimerIncrement();
                }
            }

            if (!CountUpTiming)
            {
                Timers.Scheduler.AddEventRelative(GetSchedulerId(), CalculateOverflowCycles() - cyclesLate, TimerOverflow);
            }

            EnableCycles = CalculateAlignedCurrentTicks() - cyclesLate;
            // Console.WriteLine($"[Timer] {Id} Overflow");
        }

        public void UnscheduledTimerIncrement()
        {
            CounterVal++;
            if (CounterVal > 0xFFFF)
            {
                TimerOverflow(0);
            }
        }
    }

    public sealed class Timers
    {
        public Device Device;
        public bool NdsMode;
        public bool IsNds7;
        public Scheduler Scheduler;


        public Timers(Device device, Scheduler scheduler, bool ndsMode, bool isNds7)
        {
            Device = device;
            NdsMode = ndsMode;
            IsNds7 = isNds7;
            Scheduler = scheduler;

            T = new Timer[4] {
                new Timer(this, 0),
                new Timer(this, 1),
                new Timer(this, 2),
                new Timer(this, 3),
            };
        }

        public Timer[] T;

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
    }
}