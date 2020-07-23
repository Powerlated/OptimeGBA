using System;
using System.Runtime.CompilerServices;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                BiosReads++;
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x02FFFFFF)
            {
                EwramReads++;
                return Ewram[(addr - 0x02000000) & 0x3FFFF];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03FFFFFF)
            {
                IwramReads++;
                return Iwram[(addr - 0x03000000) & 0x7FFF];
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                addr &= 0x400FFFF;

                // using (System.IO.StreamWriter w = System.IO.File.AppendText("hwio.txt"))
                // {
                //     w.WriteLine($"ReadHWIO8: {Util.Hex(addr, 8)}");
                // }

                HwioReads++;
                return ReadHwio8(addr);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);

            ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

            return u16;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);
            byte f2 = Read8(addr++);
            byte f3 = Read8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadDebug8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x02FFFFFF)
            {
                return Ewram[(addr - 0x02000000) & 0x3FFFF];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03FFFFFF)
            {
                return Iwram[(addr - 0x03000000) & 0x7FFF];
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                addr &= 0x400FFFF;

                return ReadHwio8(addr);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadDebug16(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);

            ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

            return u16;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadDebug32(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);
            byte f2 = ReadDebug8(addr++);
            byte f3 = ReadDebug8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write8(uint addr, byte val)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return;
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x02FFFFFF)
            {
                EwramWrites++;
                Ewram[(addr - 0x02000000) & 0x3FFFF] = val;
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03FFFFFF)
            {
                IwramWrites++;
                Iwram[(addr - 0x03000000) & 0x7FFF] = val;
            }

            // HWIO
            else if (addr >= 0x04000000 && addr <= 0x040003FE)
            {
                addr &= 0x400FFFF;

                // using (System.IO.StreamWriter w = System.IO.File.AppendText("hwio.txt"))
                // {
                //     w.WriteLine($"WriteHWIO8: {Util.Hex(addr, 8)}");
                // }

                HwioWrites++;
                WriteHwio8(addr, val);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);

            Write8(addr++, f0);
            Write8(addr++, f1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadHwio8(uint addr)
        {
            if (addr >= 0x4000000 && addr <= 0x4000056) // LCD
            {
                return Gba.Lcd.ReadHwio8(addr);
            }
            else if (addr >= 0x4000060 && addr <= 0x40000A8) // Sound
            {
                return Gba.GbaAudio.ReadHwio8(addr);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000DF) // DMA
            {
                return Gba.Dma.ReadHwio8(addr);
            }
            else if (addr >= 0x4000100 && addr <= 0x4000110) // Timer
            {

            }
            else if (addr >= 0x4000120 && addr <= 0x400012C) // Serial
            {

            }
            else if (addr >= 0x4000130 && addr <= 0x4000132) // Keypad
            {
                return Gba.Keypad.ReadHwio8(addr);
            }
            else if (addr >= 0x4000134 && addr <= 0x400015A) // Serial Communications
            {

            }
            else if (addr >= 0x4000200 && addr <= 0x4FF0800) // Interrupt, Waitstate, and Power-Down Control
            {
                return Gba.HwControl.ReadHwio8(addr);
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteHwio8(uint addr, byte val)
        {

            if (addr >= 0x4000000 && addr <= 0x4000056) // LCD
            {
                Gba.Lcd.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000060 && addr <= 0x40000A8) // Sound
            {
                Gba.GbaAudio.WriteHwio8(addr, val);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000DF) // DMA
            {
                Gba.Dma.WriteHwio8(addr, val);
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
                Gba.HwControl.WriteHwio8(addr, val);
            }
        }
    }
}