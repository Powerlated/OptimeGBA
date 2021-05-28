using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public enum InterruptNds
    {
        VBlank = 0,
        HBlank = 1,
        VCounterMatch = 2,
        Timer0Overflow = 3,
        Timer1Overflow = 4,
        Timer2Overflow = 5,
        Timer3Overflow = 6,
        Rtc = 7,
        Dma0 = 8,
        Dma1 = 9,
        Dma2 = 10,
        Dma3 = 11,
        Keypad = 12,
        GamePak = 13,
        // 14, 15, unused
        IpcSync = 16,
        IpcSendFifoEmpty = 17,
        IpcRecvFifoPending = 18,
        Slot1DataTransferComplete = 19,
        Slot1rq = 20,
        GeometryFifo = 21, // ARM9 only
        ScreenUnfold = 22, // ARM7 only
        SpiBus = 23, // ARM7 only 
        Wifi = 24, // ARM7 only
    }

    public sealed class HwControlNds : HwControl
    {
        Device Device;
        bool Arm9; // Or Arm7

        public HwControlNds(Device device, bool arm9)
        {
            Device = device;
            Arm9 = arm9;
        }

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000208: // IME
                    if (IME) val = BitSet(val, 0);
                    break;

                case 0x4000210: // IE B0
                    return (byte)(IE >> 0);
                case 0x4000211: // IE B1
                    return (byte)(IE >> 8);
                case 0x4000212: // IE B2
                    return (byte)(IE >> 16);
                case 0x4000213: // IE B3
                    return (byte)(IE >> 24);

                case 0x4000214: // IF B0
                    return (byte)(IF >> 0);
                case 0x4000215: // IF B1
                    return (byte)(IF >> 8);
                case 0x4000216: // IF B2
                    return (byte)(IF >> 16);
                case 0x4000217: // IF B3
                    return (byte)(IF >> 24);
            }
            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000208: // IME
                    IME = BitTest(val, 0);
                    CheckAndFireInterrupts();
                    break;

                case 0x4000210: // IE B0
                    IE &= 0xFFFFFF00;
                    IE |= (uint)((uint)val << 0);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000211: // IE B1
                    IE &= 0xFFFF00FF;
                    IE |= (uint)((uint)val << 8);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000212: // IE B2
                    IE &= 0xFF00FFFF;
                    IE |= (uint)((uint)val << 16);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000213: // IE B3
                    IE &= 0x00FFFFFF;
                    IE |= (uint)((uint)val << 24);
                    CheckAndFireInterrupts();
                    break;

                case 0x4000214: // IF B0
                    IF &= ~(uint)((uint)val << 0);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000215: // IF B1
                    IF &= ~(uint)((uint)val << 8);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000216: // IF B2
                    IF &= ~(uint)((uint)val << 16);
                    CheckAndFireInterrupts();
                    break;
                case 0x4000217: // IF B3
                    IF &= ~(uint)((uint)val << 24);
                    CheckAndFireInterrupts();
                    break;
            }
        }

        public override void FlagInterrupt(uint i)
        {
            IF |= (uint)(1 << (int)i);
            CheckAndFireInterrupts();
        }

        public void CheckAndFireInterrupts()
        {
            Available = (IE & IF & 0xFFFFFFFF) != 0;
            Device.Cpu.FlagInterrupt = Available && IME;
        }
    }
}