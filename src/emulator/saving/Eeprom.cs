using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public class Eeprom : SaveProvider
    {
        public bool Active = false;
        public bool ReadMode = false;
        public bool Readied = false;
        public bool ReceivingAddr = false;
        public bool Terminate = false;
        public uint BitsRemaining = 0;
        public uint AddrBitsRemaining = 0;

        // 1 entry per bit because I'm lazy
        public byte[] EEPROM = new byte[0x10000];
        public uint Addr = 0;
        public byte ReadBitEEPROM()
        {
            return EEPROM[Addr];
        }
        public void WriteBitEEPROM(bool bit)
        {
            EEPROM[Addr] = Convert.ToByte(bit);
        }


        public override byte Read8(uint addr)
        {
            if (Active)
            {
                if (ReadMode)
                {
                    if (BitsRemaining > 64)
                    {
                        return 0;
                    }
                    else
                    {
                        Console.WriteLine("EEPROM Read");
                        byte bit = ReadBitEEPROM();
                        Addr++;
                        return bit;
                    }
                }
            }

            return 0;
        }

        public override void Write8(uint addr, byte val)
        {
            bool bit = BitTest(val, 0);
            if (Active)
            {
                if (BitsRemaining > 0)
                {
                    if (!ReadMode)
                    {
                        Console.WriteLine("EEPROM Write!");
                        WriteBitEEPROM(bit);
                        Addr++;
                    }
                }
                else
                {
                    if (bit == false)
                    {
                        Active = false;
                    }
                }
            }
            else if (ReceivingAddr)
            {
                Console.WriteLine($"EEPROM Addr Write! {val & 1}");

                Addr <<= 1;
                Addr |= (uint)(val & 1);
                AddrBitsRemaining--;
                if (AddrBitsRemaining == 0)
                {
                    Console.WriteLine($"EEPROM Addr Set!");
                    Active = true;
                }
            }
            else
            {
                if (Readied)
                {
                    Console.WriteLine("EEPROM Ready!");

                    ReadMode = bit;
                    ReceivingAddr = true;
                    AddrBitsRemaining = 6;
                    Readied = false;
                    Addr = 0;

                    if (ReadMode)
                    {
                        Console.WriteLine("EEPROM Read Mode!");
                        BitsRemaining = 68;
                    }
                    else
                    {
                        Console.WriteLine("EEPROM Write Mode!");
                        BitsRemaining = 64;
                    }
                }
                else
                {
                    if (bit) Readied = true;
                }
            }
        }

        public override byte[] GetSave()
        {
            return new byte[0];
        }

        public override void LoadSave(byte[] save)
        {

        }
    }
}