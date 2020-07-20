using System;

namespace OptimeGBA
{
    public class Memory
    {
        GBA Gba;

        public Memory(GBA gba)
        {
            Gba = gba;

            for (uint i = 0; i < Ewram.Length; i++)
            {
                Ewram[i] = 0x69;
            }

            for (uint i = 0; i < Iwram.Length; i++)
            {
                Iwram[i] = 0x69;
            }
        }

        public long EwramWrites = 0;
        public long IwramWrites = 0;
        public long HwioReads = 0;
        public long PaletteWrites = 0;
        public long VramWrites = 0;
        public long OamWrites = 0;

        public long BiosReads = 0;
        public long EwramReads = 0;
        public long IwramReads = 0;
        public long HwioWrites = 0;
        public long RomReads = 0;
        public long PaletteReads = 0;
        public long VramReads = 0;
        public long OamReads = 0;

        // External Work RAM
        public byte[] Ewram = new byte[262144];
        // Internal Work RAM
        public byte[] Iwram = new byte[32768];

        public byte[] Bios = new byte[16384];

        public byte[] Rom = new byte[33554432];

        public byte Read8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                BiosReads++;
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                EwramReads++;
                return Ewram[addr - 0x02000000];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                IwramReads++;
                return Iwram[addr - 0x03000000];
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                HwioReads++;
                return ReadHWIO8(addr);
            }

            // Display Memory
            else if (addr >= 0x05000000 && addr <= 0x07FFFFFF)
            {
                if (addr >= 0x05000000 && addr <= 0x050003FF)
                {
                    // Palette RAM
                    PaletteReads++;
                    return Gba.Lcd.Read8(addr);
                }
                else if (addr >= 0x06000000 && addr <= 0x06017FFF)
                {
                    // VRAM
                    VramReads++;
                    return Gba.Lcd.Read8(addr);
                }
                else if (addr >= 0x07000000 && addr <= 0x070003FF)
                {
                    // OAM
                    OamReads++;
                    return Gba.Lcd.Read8(addr);
                }
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                RomReads++;
                return Rom[addr - 0x08000000];
            }

            // This should be open bus
            return 0;
        }

        public uint Read16(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);

            uint u32 = (uint)((f1 << 8) | (f0 << 0));

            return u32;
        }

        public uint Read32(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);
            byte f2 = Read8(addr++);
            byte f3 = Read8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        public byte ReadDebug8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                return Ewram[addr - 0x02000000];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return Iwram[addr - 0x03000000];
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                return ReadHWIO8(addr);
            }

            // Display Memory
            else if (addr >= 0x05000000 && addr <= 0x07FFFFFF)
            {
                if (addr >= 0x05000000 && addr <= 0x050003FF)
                {
                    // Palette RAM
                    return Gba.Lcd.Read8(addr);
                }
                else if (addr >= 0x06000000 && addr <= 0x06017FFF)
                {
                    // VRAM
                    return Gba.Lcd.Read8(addr);
                }
                else if (addr >= 0x07000000 && addr <= 0x070003FF)
                {
                    // OAM
                    return Gba.Lcd.Read8(addr);
                }
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                return Rom[addr - 0x08000000];
            }

            // This should be open bus
            return 0;
        }

        public uint ReadDebug16(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);

            uint u32 = (uint)((f1 << 8) | (f0 << 0));

            return u32;
        }

        public uint ReadDebug32(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);
            byte f2 = ReadDebug8(addr++);
            byte f3 = ReadDebug8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        public void Write8(uint addr, byte val)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return;
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                EwramWrites++;
                Ewram[addr - 0x02000000] = val;
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                IwramWrites++;
                Iwram[addr - 0x03000000] = val;
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                HwioWrites++;
                WriteHWIO8(addr, val);
            }

            // Display Memory
            else if (addr >= 0x05000000 && addr <= 0x07FFFFFF)
            {
                if (addr >= 0x05000000 && addr <= 0x050003FF)
                {
                    // Palette RAM
                    PaletteWrites++;
                    Gba.Lcd.Write8(addr, val);
                }
                else if (addr >= 0x06000000 && addr <= 0x06017FFF)
                {
                    // VRAM
                    VramWrites++;
                    Gba.Lcd.Write8(addr, val);
                }
                else if (addr >= 0x07000000 && addr <= 0x070003FF)
                {
                    // OAM
                    OamWrites++;
                    Gba.Lcd.Write8(addr, val);
                }
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                return;
            }
        }

        public void Write16(uint addr, ushort val)
        {
            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);

            Write8(addr++, f0);
            Write8(addr++, f1);
        }

        public void Write32(uint addr, uint val)
        {
            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);
            byte f2 = (byte)(val >> 16);
            byte f3 = (byte)(val >> 24);

            Write8(addr++, f0);
            Write8(addr++, f1);
            Write8(addr++, f2);
            Write8(addr++, f3);
        }

        public byte ReadHWIO8(uint addr)
        {
            if (addr >= 0x4000000 && addr <= 0x4000056) // LCD
            {
                return Gba.Lcd.ReadHwio8(addr);
            }
            else if (addr >= 0x4000060 && addr <= 0x40000A8) // Sound
            {

            }
            else if (addr >= 0x40000B0 && addr <= 0x40000E0) // DMA
            {

            }
            else if (addr >= 0x4000100 && addr <= 0x4000110) // Timer
            {

            }
            else if (addr >= 0x4000120 && addr <= 0x400012C) // Serial
            {

            }
            else if (addr >= 0x4000130 && addr <= 0x4000132) // Keypad
            {
                switch (addr) {
                    case 0x4000130: return 0x03;
                    case 0x4000131: return 0xFF;
                }
            }
            else if (addr >= 0x4000134 && addr <= 0x400015A) // Serial Communications
            {

            }
            else if (addr >= 0x4000200 && addr <= 0x4FF0800) // Interrupt, Waitstate, and Power-Down Control
            {

            }
            return 0;
        }

        public void WriteHWIO8(uint addr, byte val)
        {
            if (addr >= 0x4000000 && addr <= 0x4000056) // LCD
            {
                Gba.Lcd.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000060 && addr <= 0x40000A8) // Sound
            {

            }
            else if (addr >= 0x40000B0 && addr <= 0x40000E0) // DMA
            {

            }
            else if (addr >= 0x4000100 && addr <= 0x4000110) // Timer
            {

            }
            else if (addr >= 0x4000120 && addr <= 0x400012C) // Serial
            {

            }
            else if (addr >= 0x4000130 && addr <= 0x4000132) // Keypad
            {

            }
            else if (addr >= 0x4000134 && addr <= 0x400015A) // Serial Communications
            {

            }
            else if (addr >= 0x4000200 && addr <= 0x4FF0800) // Interrupt, Waitstate, and Power-Down Control
            {

            }
        }
    }
}