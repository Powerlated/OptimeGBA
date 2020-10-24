using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public enum EepromState
    {
        Ready,
        StartRequest,
        ReceiveRequestType,
        ReceiveAddrForRead,
        ReceiveAddrForWrite,
        ReceiveDataForWrite,
        ReceiveTerminatingZero
    }

    public enum EepromSize
    {
        Eeprom4k,
        Eeprom64k
    }

    public sealed class Eeprom : SaveProvider
    {
        EepromState State = EepromState.Ready;
        EepromSize Size;

        public byte[] EEPROM = new byte[0x2000];
        public uint Addr = 0;
        public uint ReadAddr = 0;

        public uint BitsRemaining = 0;
        public uint ReadBitsRemaining = 0;

        public Gba Gba;

        public Eeprom(Gba gba, EepromSize size)
        {
            Gba = gba;
            Size = size;
        }

        public byte ReadBitEEPROM()
        {
            byte bitIndex = (byte)(ReadAddr & 7);
            uint index = ReadAddr >> 3;
            return (byte)(BitTest(EEPROM[index], bitIndex) ? 1 : 0);
        }
        public void WriteBitEEPROM(bool bit)
        {
            byte bitIndex = (byte)(Addr & 7);
            uint index = Addr >> 3;
            if (bit)
            {
                EEPROM[index] = BitSet(EEPROM[index], bitIndex);
            }
            else
            {
                EEPROM[index] = BitClear(EEPROM[index], bitIndex);
            }
        }

        public override byte Read8(uint addr)
        {
            if (Gba.Dma.DmaLock)
            {
                // Console.WriteLine("[EEPROM] Read from DMA");
            }

            byte val = 0;
            if (ReadBitsRemaining > 0)
            {
                if (ReadBitsRemaining <= 64)
                {
                    val = ReadBitEEPROM();
                    // Console.WriteLine($"[EEPROM] Read (addr: {Util.Hex(ReadAddr, 4)}) {val}, bits remaining: " + ReadBitsRemaining);
                    ReadAddr++;
                }
                else
                {
                    val = 1;
                }

                ReadBitsRemaining--;
            }
            else
            {
                ReadBitsRemaining = 68;
            }

            return val;
        }

        public override void Write8(uint addr, byte val)
        {
            if (Gba.Dma.DmaLock)
            {
                // Console.WriteLine("[EEPROM] Write from DMA");
            }

            bool bit = BitTest(val, 0);
            switch (State)
            {
                case EepromState.Ready:
                    if (bit)
                    {
                        // Console.WriteLine("[EEPROM] Request started");
                        State = EepromState.StartRequest;
                    }
                    break;
                case EepromState.StartRequest:
                    BitsRemaining = Size == EepromSize.Eeprom64k ? 14U : 6U;
                    if (bit)
                    {
                        // Console.WriteLine("[EEPROM] Receiving read address");
                        State = EepromState.ReceiveAddrForRead;
                        ReadAddr = 0;
                    }
                    else
                    {
                        // Console.WriteLine("[EEPROM] Receiving write address");
                        State = EepromState.ReceiveAddrForWrite;
                        Addr = 0;
                    }
                    break;
                case EepromState.ReceiveAddrForRead:
                    if (BitsRemaining > 0)
                    {
                        ReadAddr |= bit ? 1u : 0u;
                        ReadAddr <<= 1;
                        ReadAddr &= 0x3FF;

                        BitsRemaining--;
                        // Console.WriteLine($"[EEPROM] Setting read address ({bit}), bits remaining: {BitsRemaining}");

                        if (BitsRemaining == 0)
                        {
                            // Console.WriteLine("[EEPROM] Read address written: " + Util.Hex(ReadAddr, 4));
                            State = EepromState.ReceiveTerminatingZero;
                            BitsRemaining = 68;
                            ReadBitsRemaining = 68;
                        }
                    }
                    break;
                case EepromState.ReceiveAddrForWrite:
                    if (BitsRemaining > 0)
                    {
                        Addr |= bit ? 1u : 0u;
                        Addr <<= 1;
                        Addr &= 0x3FF;

                        BitsRemaining--;
                        // Console.WriteLine($"[EEPROM] Setting write address ({bit}), bits remaining: {BitsRemaining}");

                        if (BitsRemaining == 0)
                        {
                            BitsRemaining = 64;
                            State = EepromState.ReceiveDataForWrite;
                            // Console.WriteLine("[EEPROM] Write address set: " + Util.Hex(Addr, 4));
                        }
                    }
                    break;
                case EepromState.ReceiveDataForWrite:
                    if (BitsRemaining > 0)
                    {
                        WriteBitEEPROM(bit);
                        // Console.WriteLine($"[EEPROM] Write (addr: {Util.Hex(Addr, 4)}) {Convert.ToByte(bit)}, bits remaining: " + BitsRemaining);
                        Addr++;
                        BitsRemaining--;

                        if (BitsRemaining == 0)
                        {
                            // Console.WriteLine($"[EEPROM] Write finished");
                            State = EepromState.Ready;
                        }
                    }
                    break;
                case EepromState.ReceiveTerminatingZero:
                    State = EepromState.Ready;
                    // Console.WriteLine($"[EEPROM] Received terminating zero");
                    break;
            }
        }

        public override byte[] GetSave()
        {
            return EEPROM;
        }

        public override void LoadSave(byte[] save)
        {
            for (uint i = 0; i < save.Length && i < EEPROM.Length; i++)
            {
                EEPROM[i] = save[i];
            }
        }
    }
}