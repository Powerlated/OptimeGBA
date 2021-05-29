using static Util;
using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public enum CartridgeState
    {
        Dummy,
        ReadCartridgeHeader
    }
    public class CartridgeNds
    {
        Nds Nds;
        byte[] Rom;

        public CartridgeNds(Nds nds)
        {
            Nds = nds;
            Rom = Nds.Provider.Rom;
        }

        ulong PendingCommand;

        // State
        CartridgeState State;
        uint DataPos;
        bool Key1Encryption;
        bool Key2Encryption;

        // AUXSPICNT
        byte SpiBaudRate;
        bool SpiHoldChipSel = false;
        bool SpiBusy = false;
        bool Slot1SpiMode = false;
        bool TransferReadyIrq = false;
        bool Slot1Enable = false;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x40001A0: // AUXSPICNT B0
                    val |= SpiBaudRate;
                    if (SpiHoldChipSel) val = BitSet(val, 6);
                    if (SpiBusy) val = BitSet(val, 7);
                    break;
                case 0x40001A1: // AUXSPICNT B0
                    if (Slot1SpiMode) val = BitSet(val, 5);
                    if (TransferReadyIrq) val = BitSet(val, 6);
                    if (Slot1Enable) val = BitSet(val, 7);
                    break;

                case 0x40001A4: // ROMCTRL B0
                    break;
                case 0x40001A5: // ROMCTRL B1
                    break;
                case 0x40001A6: // ROMCTRL B2
                    return 0b10000000;
                case 0x40001A7: // ROMCTRL B3
                    break;

                case 0x4100010: // From cartridge
                case 0x4100011:
                case 0x4100012:
                case 0x4100013:
                    return ReadData();
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x40001A0: // AUXSPICNT B0
                    SpiBaudRate = (byte)(val & 0b11);
                    SpiHoldChipSel = BitTest(val, 6);
                    SpiBusy = BitTest(val, 7);
                    return;
                case 0x40001A1: // AUXSPICNT B0
                    Slot1SpiMode = BitTest(val, 5);
                    TransferReadyIrq = BitTest(val, 6);
                    Slot1Enable = BitTest(val, 7);
                    return;
            }

            if (Slot1Enable)
            {
                switch (addr)
                {
                    case 0x40001A8: // Slot 1 Command out
                    case 0x40001A9:
                    case 0x40001AA:
                    case 0x40001AB:
                    case 0x40001AC:
                    case 0x40001AD:
                    case 0x40001AE:
                        PendingCommand |= val;
                        PendingCommand <<= 8;
                        return;
                    case 0x40001AF:
                        ProcessCommand();
                        PendingCommand = 0;
                        return;
                }
            }
        }

        public void ProcessCommand()
        {
            // TODO: Implement more commands
            var cmd = PendingCommand;
            Console.WriteLine("Slot 1 Command: " + Hex(cmd, 16));

            if (cmd == 0x9F00000000000000)
            {
                State = CartridgeState.Dummy;
            }
            else if (cmd == 0x0000000000000000)
            {
                State = CartridgeState.ReadCartridgeHeader;
            }
            else if ((cmd & 0xFF00000000000000) == 0x3C00000000000000)
            {
                State = CartridgeState.Dummy;

                Key1Encryption = true;
                Console.WriteLine("Slot 1: Enabled KEY1 encryption");
            }

            // TODO: Implement KEY1/KEY2 encryption
            if (Key1Encryption)
            {
                Console.WriteLine("Key1 Command");
                Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.Slot1DataTransferComplete);
            }
        }

        public byte ReadData()
        {
            switch (State)
            {
                case CartridgeState.Dummy:
                    return 0xFF;
                case CartridgeState.ReadCartridgeHeader:
                    // Repeatedly returns first 0x1000 bytes, with first 0x200 bytes filled
                    byte val = 0;
                    if (DataPos < 0x200)
                    {
                        val = Rom[DataPos];
                    }
                    DataPos = (DataPos + 1) & 0xFFF;
                    return val;
            }

            return 0;
        }
    }
}