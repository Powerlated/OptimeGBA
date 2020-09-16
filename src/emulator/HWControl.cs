using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public enum Interrupt
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

    public class HWControl
    {
        GBA Gba;

        public HWControl(GBA gba)
        {
            Gba = gba;
        }

        public bool IME = false;

        public bool IE_VBlank;
        public bool IE_HBlank;
        public bool IE_VCounterMatch;
        public bool IE_Timer0Overflow;
        public bool IE_Timer1Overflow;
        public bool IE_Timer2Overflow;
        public bool IE_Timer3Overflow;
        public bool IE_Serial;
        public bool IE_DMA0;
        public bool IE_DMA1;
        public bool IE_DMA2;
        public bool IE_DMA3;
        public bool IE_Keypad;
        public bool IE_GamePak;

        public bool IF_VBlank;
        public bool IF_HBlank;
        public bool IF_VCounterMatch;
        public bool IF_Timer0Overflow;
        public bool IF_Timer1Overflow;
        public bool IF_Timer2Overflow;
        public bool IF_Timer3Overflow;
        public bool IF_Serial;
        public bool IF_DMA0;
        public bool IF_DMA1;
        public bool IF_DMA2;
        public bool IF_DMA3;
        public bool IF_Keypad;
        public bool IF_GamePak;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000200: // IE B0
                    if (IE_VBlank) val = BitSet(val, 0);
                    if (IE_HBlank) val = BitSet(val, 1);
                    if (IE_VCounterMatch) val = BitSet(val, 2);
                    if (IE_Timer0Overflow) val = BitSet(val, 3);
                    if (IE_Timer1Overflow) val = BitSet(val, 4);
                    if (IE_Timer2Overflow) val = BitSet(val, 5);
                    if (IE_Timer3Overflow) val = BitSet(val, 6);
                    if (IE_Serial) val = BitSet(val, 7);
                    break;
                case 0x4000201: // IE B1
                    if (IE_DMA0) val = BitSet(val, 0);
                    if (IE_DMA1) val = BitSet(val, 1);
                    if (IE_DMA2) val = BitSet(val, 2);
                    if (IE_DMA3) val = BitSet(val, 3);
                    if (IE_Keypad) val = BitSet(val, 4);
                    if (IE_GamePak) val = BitSet(val, 5);
                    break;

                case 0x4000202: // IF B0
                    if (IF_VBlank) val = BitSet(val, 0);
                    if (IF_HBlank) val = BitSet(val, 1);
                    if (IF_VCounterMatch) val = BitSet(val, 2);
                    if (IF_Timer0Overflow) val = BitSet(val, 3);
                    if (IF_Timer1Overflow) val = BitSet(val, 4);
                    if (IF_Timer2Overflow) val = BitSet(val, 5);
                    if (IF_Timer3Overflow) val = BitSet(val, 6);
                    if (IF_Serial) val = BitSet(val, 7);
                    break;
                case 0x4000203: // IF B1
                    if (IF_DMA0) val = BitSet(val, 0);
                    if (IF_DMA1) val = BitSet(val, 1);
                    if (IF_DMA2) val = BitSet(val, 2);
                    if (IF_DMA3) val = BitSet(val, 3);
                    if (IF_Keypad) val = BitSet(val, 4);
                    if (IF_GamePak) val = BitSet(val, 5);
                    break;

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
                    IE_VBlank = BitTest(val, 0);
                    IE_HBlank = BitTest(val, 1);
                    IE_VCounterMatch = BitTest(val, 2);
                    IE_Timer0Overflow = BitTest(val, 3);
                    IE_Timer1Overflow = BitTest(val, 4);
                    IE_Timer2Overflow = BitTest(val, 5);
                    IE_Timer3Overflow = BitTest(val, 6);
                    IE_Serial = BitTest(val, 7);

                    CheckAndFireInterrupts();
                    break;
                case 0x4000201: // IE B1
                    IE_DMA0 = BitTest(val, 0);
                    IE_DMA1 = BitTest(val, 1);
                    IE_DMA2 = BitTest(val, 2);
                    IE_DMA3 = BitTest(val, 3);
                    IE_Keypad = BitTest(val, 4);
                    IE_GamePak = BitTest(val, 5);

                    CheckAndFireInterrupts();
                    break;

                case 0x4000202: // IF B0
                    if (BitTest(val, 0)) IF_VBlank = false;
                    if (BitTest(val, 1)) IF_HBlank = false;
                    if (BitTest(val, 2)) IF_VCounterMatch = false;
                    if (BitTest(val, 3)) IF_Timer0Overflow = false;
                    if (BitTest(val, 4)) IF_Timer1Overflow = false;
                    if (BitTest(val, 5)) IF_Timer2Overflow = false;
                    if (BitTest(val, 6)) IF_Timer3Overflow = false;
                    if (BitTest(val, 7)) IF_Serial = false;
                    CheckAndFireInterrupts();
                    break;
                case 0x4000203: // IF B1
                    if (BitTest(val, 0)) IF_DMA0 = false;
                    if (BitTest(val, 1)) IF_DMA1 = false;
                    if (BitTest(val, 2)) IF_DMA2 = false;
                    if (BitTest(val, 3)) IF_DMA3 = false;
                    if (BitTest(val, 4)) IF_Keypad = false;
                    if (BitTest(val, 5)) IF_GamePak = false;
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
                        // Gba.Halt();
                    }
                    break;
            }
        }

        public void FlagInterrupt(Interrupt i)
        {
            switch (i)
            {
                case Interrupt.VBlank:
                    IF_VBlank = true; break;
                case Interrupt.HBlank:
                    IF_HBlank = true; break;
                case Interrupt.VCounterMatch:
                    IF_VCounterMatch = true; break;
                case Interrupt.Timer0Overflow:
                    IF_Timer0Overflow = true; break;
                case Interrupt.Timer1Overflow:
                    IF_Timer1Overflow = true; break;
                case Interrupt.Timer2Overflow:
                    IF_Timer2Overflow = true; break;
                case Interrupt.Timer3Overflow:
                    IF_Timer3Overflow = true; break;
                case Interrupt.Serial:
                    IF_Serial = true; break;
                case Interrupt.DMA0:
                    IF_DMA0 = true; break;
                case Interrupt.DMA1:
                    IF_DMA1 = true; break;
                case Interrupt.DMA2:
                    IF_DMA2 = true; break;
                case Interrupt.DMA3:
                    IF_DMA3 = true; break;
                case Interrupt.Keypad:
                    IF_Keypad = true; break;
                case Interrupt.GamePak:
                    IF_GamePak = true; break;
            }

            CheckAndFireInterrupts();
        }

        public bool AvailableAndEnabled = false;
        public bool Available;

        public void CheckAndFireInterrupts()
        {
            Available = false;
            if (IF_VBlank && IE_VBlank) { Available = true; }
            if (IF_HBlank && IE_HBlank) { Available = true; }
            if (IF_VCounterMatch && IE_VCounterMatch) { Available = true; }
            if (IF_Timer0Overflow && IE_Timer0Overflow) { Available = true; }
            if (IF_Timer1Overflow && IE_Timer1Overflow) { Available = true; }
            if (IF_Timer2Overflow && IE_Timer2Overflow) { Available = true; }
            if (IF_Timer3Overflow && IE_Timer3Overflow) { Available = true; }
            if (IF_Serial && IE_Serial) { Available = true; }
            if (IF_DMA0 && IE_DMA0) { Available = true; }
            if (IF_DMA1 && IE_DMA1) { Available = true; }
            if (IF_DMA2 && IE_DMA2) { Available = true; }
            if (IF_DMA3 && IE_DMA3) { Available = true; }
            if (IF_Keypad && IE_Keypad) { Available = true; }
            if (IF_GamePak && IE_GamePak) { Available = true; }

            // if (available && IME && !AvailableAndEnabled)
            // {
            //     Gba.Arm7.IRQ = true;
            // }
            AvailableAndEnabled = Available && IME;
        }

        public bool CheckAvailable()
        {
            if (IF_VBlank && IE_VBlank) { return true; }
            if (IF_HBlank && IE_HBlank) { return true; }
            if (IF_VCounterMatch && IE_VCounterMatch) { return true; }
            if (IF_Timer0Overflow && IE_Timer0Overflow) { return true; }
            if (IF_Timer1Overflow && IE_Timer1Overflow) { return true; }
            if (IF_Timer2Overflow && IE_Timer2Overflow) { return true; }
            if (IF_Timer3Overflow && IE_Timer3Overflow) { return true; }
            if (IF_Serial && IE_Serial) { return true; }
            if (IF_DMA0 && IE_DMA0) { return true; }
            if (IF_DMA1 && IE_DMA1) { return true; }
            if (IF_DMA2 && IE_DMA2) { return true; }
            if (IF_DMA3 && IE_DMA3) { return true; }
            if (IF_Keypad && IE_Keypad) { return true; }
            if (IF_GamePak && IE_GamePak) { return true; }

            return false;
        }
    }
}