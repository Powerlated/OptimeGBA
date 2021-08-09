using System;
using static Util;

namespace OptimeGBA
{
    public enum SpiFlashState
    {
        Ready,
        Identification,
        ReceiveAddress,
        Reading,
        Status,
        TakePrefix, // For cartridges with IR and Flash
    }

    public unsafe sealed class SpiFlash
    {
        public byte[] Data;

        public SpiFlash(byte[] data) {
            Data = data;
        }

        // Firmware flash state
        public SpiFlashState FlashState;
        public bool EnableWrite;
        public byte IdIndex;
        public uint Address;
        public byte AddressByteNum = 0;

        // From Nocash's original DS 
        byte[] Id = new byte[] { 0x20, 0x40, 0x12 };

        public byte OutData;

        public byte TransferTo(byte val, bool transferSize)
        {
            switch (FlashState)
            {
                case SpiFlashState.Ready:
                    // Console.WriteLine("SPI: Receive command! " + Hex(val, 2));
                    OutData = 0x00;
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
                            // Console.WriteLine("SPI ID");
                            OutData = 0x00;
                            break;
                        case 0x00:
                            break;
                        default:
                            throw new NotImplementedException("SPI: Unimplemented command: " + Hex(val, 2));
                    }
                    break;
                case SpiFlashState.ReceiveAddress:
                    // Console.WriteLine("SPI: Address byte write: " + Hex(val, 2));
                    Address |= (uint)(val << ((2 - AddressByteNum) * 8));
                    AddressByteNum++;
                    if (AddressByteNum > 2)
                    {
                        AddressByteNum = 0;
                        FlashState = SpiFlashState.Reading;
                        // Console.WriteLine("SPI: Address written: " + Hex(Address, 6));
                    }
                    break;
                case SpiFlashState.Reading:
                    // Console.WriteLine("SPI: Read from address: " + Hex(Address, 6));
                    // Nds7.Cpu.Error("SPI");
                    if (Address < 0x40000)
                    {
                        OutData = Data[Address];
                    }
                    else
                    {
                        OutData = 0;
                    }
                    Address += transferSize ? 2U : 1U;
                    Address &= 0xFFFFFF;
                    break;
                case SpiFlashState.Identification:
                    OutData = Id[IdIndex];
                    IdIndex++;
                    IdIndex %= 3;
                    break;
            }

            return OutData;
        }

        public void Deselect()
        {
            FlashState = SpiFlashState.Ready;
        }
    }
}