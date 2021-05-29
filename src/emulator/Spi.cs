using static OptimeGBA.Bits;
using System;
using static Util;

namespace OptimeGBA
{
    public enum SpiDevice : byte
    {
        PowerManager = 0,
        Firmware = 1,
        Touchscreen = 2
    }

    public enum SpiFlashState
    {
        Ready,
        Identification,
        ReceiveAddress,
        Reading,
        Status,
    }

    public unsafe sealed class Spi
    {
        public Nds7 Nds7;
        public byte[] Firmware;

        public Spi(Nds7 nds7)
        {
            Nds7 = nds7;
            Firmware = nds7.Nds.Provider.Firmware;
        }

        // From Nocash's original DS 
        byte[] Id = new byte[] { 0x20, 0x40, 0x12 };

        // SPICNT
        byte BaudRate;
        SpiDevice DeviceSelect;
        bool TransferSize;
        bool ChipSelHold;
        bool EnableIrq;
        bool EnableSpi;
        bool Busy;

        // Firmware flash state
        SpiFlashState FlashState;
        bool EnableWrite;
        byte IdIndex;
        uint Address;
        byte AddressByteNum = 0;

        byte OutData;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x40001C0: // SPICNT B0
                    val |= BaudRate;
                    if (Busy) val = BitSet(val, 7);
                    break;
                case 0x40001C1: // SPICNT B1
                    val |= (byte)DeviceSelect;
                    if (TransferSize) val = BitSet(val, 2);
                    if (ChipSelHold) val = BitSet(val, 3);
                    if (EnableIrq) val = BitSet(val, 6);
                    if (EnableSpi) val = BitSet(val, 7);
                    break;

                case 0x40001C2: // SPIDATA
                    // Console.WriteLine("SPI: Read! " + Hex(InData, 2));
                    return OutData;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x40001C0: // SPICNT B0
                    BaudRate = (byte)(val & 0b11);
                    break;
                case 0x40001C1: // SPICNT B1
                    DeviceSelect = (SpiDevice)(val & 0b11);
                    TransferSize = BitTest(val, 2);
                    bool oldChipSelHold = ChipSelHold;
                    ChipSelHold = BitTest(val, 3);
                    EnableIrq = BitTest(val, 6);
                    EnableSpi = BitTest(val, 7);

                    if (!EnableSpi)
                    {
                        ChipSelHold = false;
                    }

                    if (oldChipSelHold && !ChipSelHold)
                    {
                        Console.WriteLine("SPI: Transfer complete!");
                        FlashState = SpiFlashState.Ready;
                        if (EnableIrq)
                        {
                            Nds7.HwControl.FlagInterrupt((uint)InterruptNds.SpiBus);
                        }
                    }
                    break;

                case 0x40001C2: // SPIDATA
                    TransferTo(val);
                    break;
            }
        }

        public void TransferTo(byte val)
        {
            if (EnableSpi)
            {
                switch (DeviceSelect)
                {
                    case SpiDevice.Firmware:
                        TransferToSpiFlash(val);
                        break;
                    case SpiDevice.Touchscreen:
                        Console.WriteLine("Touchscreen access");
                        break;
                    case SpiDevice.PowerManager:
                        Console.WriteLine("Power manager access");
                        break;

                }
            }
        }

        public void TransferToSpiFlash(byte val)
        {
            switch (FlashState)
            {
                case SpiFlashState.Ready:
                    Console.WriteLine("SPI: Receive command! " + Hex(val, 2));
                    switch (val)
                    {
                        case 0x06:
                            EnableWrite = true;
                            break;
                        case 0x04:
                            EnableWrite = false;
                            break;
                        case 0x9F:
                            FlashState = SpiFlashState.Identification;
                            IdIndex = 0;
                            break;
                        case 0x03:
                            FlashState = SpiFlashState.ReceiveAddress;
                            Address = 0;
                            AddressByteNum = 0;
                            break;
                        case 0x05: // Identification
                            OutData = 0x00;
                            break;
                        case 0x00:
                            break;
                        default:
                            throw new NotImplementedException("SPI: Unimplemented command: " + Hex(val, 2));
                    }
                    break;
                case SpiFlashState.ReceiveAddress:
                    Console.WriteLine("SPI: Address byte write: " + Hex(val, 2));
                    Address |= (uint)(val << ((2 - AddressByteNum) * 8));
                    AddressByteNum++;
                    if (AddressByteNum > 2)
                    {
                        AddressByteNum = 0;
                        FlashState = SpiFlashState.Reading;
                        Console.WriteLine("SPI: Address written: " + Hex(Address, 6));
                    }
                    break;
                case SpiFlashState.Reading:
                    // Console.WriteLine("SPI: Read from address: " + Hex(Address, 6));
                    // Nds7.Cpu.Error("SPI");
                    if (Address < 0x40000)
                    {
                        OutData = Firmware[Address];
                    }
                    else
                    {
                        OutData = 0;
                    }
                    Address++;
                    Address &= 0xFFFFFF;
                    break;
            }
        }
    }
}