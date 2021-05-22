using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public enum InterruptGba
    {
        VBlank = 0,
        HBlank = 1,
        VCounterMatch = 2,
        Timer0Overflow = 3,
        Timer1Overflow = 4,
        Timer2Overflow = 5,
        Timer3Overflow = 6,
        Serial = 7,
        DMA0 = 8,
        DMA1 = 9,
        DMA2 = 10,
        DMA3 = 11,
        Keypad = 12,
        GamePak = 13,
    }

    public sealed class HwControlGba
    {
        Gba Gba;

        public HwControlGba(Gba gba)
        {
            Gba = gba;
        }

        public bool IME = false;

        public uint IE;
        public uint IF;

        public bool Available;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000200: // IE B0
                    return (byte)(IE >> 0);
                case 0x4000201: // IE B1
                    return (byte)(IE >> 8);

                case 0x4000202: // IF B0
                    return (byte)(IF >> 0);
                case 0x4000203: // IF B1
                    return (byte)(IF >> 8);

                case 0x4000208: // IME
                    if (IME) val = BitSet(val, 0);
                    break;
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000200: // IE B0
                    IE &= 0x3F00;
                    IE |= (ushort)((ushort)val << 0);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000201: // IE B1
                    IE &= 0x00FF;
                    IE |= (ushort)((val & 0x3F) << 8);
                    CheckAndFireInterrupts();
                    break;

                case 0x4000202: // IF B0
                    IF &= (ushort)(~((ushort)val << 0));
                    CheckAndFireInterrupts();
                    break;
                case 0x4000203: // IF B1
                    IF &= (ushort)(~((val & 0x3F) << 8));
                    CheckAndFireInterrupts();
                    break;

                case 0x4000208: // IME
                    IME = BitTest(val, 0);

                    CheckAndFireInterrupts();
                    break;

                case 0x4000301: // HALTCNT
                    if (BitTest(val, 7))
                    {
                    }
                    else
                    {
                        Gba.Scheduler.AddEventRelative(SchedulerId.HaltSkip, 0, Gba.HaltSkip);
                    }
                    break;
            }
        }

        public void FlagInterrupt(InterruptGba i)
        {
            IF |= (ushort)(1 << (int)i);
            CheckAndFireInterrupts();
        }
        
        public void CheckAndFireInterrupts()
        {
            Available = (IE & IF & 0x3FFF) != 0;
            Gba.Cpu.FlagInterrupt = Available && IME;
        }
    }
}