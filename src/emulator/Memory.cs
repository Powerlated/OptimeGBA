using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OptimeGBA
{
    unsafe public class Memory
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

        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();

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

        public byte[] Bios = new byte[16384];
        public byte[] Rom = new byte[33554432];

        // External Work RAM
        public byte[] Ewram = new byte[262144];
        // Internal Work RAM
        public byte[] Iwram = new byte[32768];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads++;
                    return Bios[(addr - 0x00000000) & 0x3FFF];
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    EwramReads++;
                    return Ewram[(addr - 0x02000000) & 0x3FFFF];
                case 0x3: // IWRAM
                    IwramReads++;
                    return Iwram[(addr - 0x03000000) & 0x7FFF];
                case 0x4: // I/O Registers
                    addr &= 0x400FFFF;

                    // uint count;
                    // HwioReadLog.TryGetValue(addr, out count);
                    // HwioReadLog[addr] = count + 1;

                    HwioReads++;
                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
                    PaletteReads++;
                    addr = (addr - 0x05000000) & 0x3FF;
                    return Gba.Lcd.Palettes[addr];
                case 0x6: // PPU VRAM
                    VramReads++;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    return Gba.Lcd.Vram[addr];
                case 0x7: // PPU OAM
                    OamReads++;
                    addr = (addr - 0x07000000) & 0x3FF;
                    return Gba.Lcd.Oam[addr];
                case 0x8: // Game Pak ROM/FlashROM 
                    RomReads++;
                    return Rom[addr - 0x08000000];
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                    return ReadFlash(addr);
                case 0xF: // Game Pak SRAM/Flash
                    break;
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public ushort Read16(uint addr)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads += 2;
                    addr = (addr - 0x00000000) & 0x3FFF;
                    fixed (byte* ptr = Bios)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramReads += 2;
                    addr = (addr - 0x02000000) & 0x3FFFF;
                    fixed (byte* ptr = Ewram)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x3: // IWRAM
                    IwramReads += 2;
                    addr = (addr - 0x03000000) & 0x7FFF;
                    fixed (byte* ptr = Iwram)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteReads += 2;
                    addr = (addr - 0x05000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Palettes)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x6: // PPU VRAM
                    VramReads += 2;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    fixed (byte* ptr = Gba.Lcd.Vram)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x7: // PPU OAM
                    OamReads += 2;
                    addr = (addr - 0x07000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Oam)
                    {
                        return *(ushort*)(ptr + addr);
                    }
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                    RomReads += 2;
                    addr = (addr - 0x08000000);
                    if (addr < Rom.Length)
                    {
                        fixed (byte* ptr = Rom)
                        {
                            return *(ushort*)(ptr + addr);
                        }
                    }
                    else
                    {
                        return 0;
                    }
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    goto default;

                default:
                    byte f0 = Read8(addr++);
                    byte f1 = Read8(addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe public uint Read32(uint addr)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads += 4;
                    addr = (addr - 0x00000000) & 0x3FFF;
                    fixed (byte* ptr = Bios)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramReads += 4;
                    addr = (addr - 0x02000000) & 0x3FFFF;
                    fixed (byte* ptr = Ewram)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x3: // IWRAM
                    IwramReads += 4;
                    addr = (addr - 0x03000000) & 0x7FFF;
                    fixed (byte* ptr = Iwram)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteReads += 4;
                    addr = (addr - 0x05000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Palettes)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x6: // PPU VRAM
                    VramReads += 4;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    fixed (byte* ptr = Gba.Lcd.Vram)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x7: // PPU OAM
                    OamReads += 4;
                    addr = (addr - 0x07000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Oam)
                    {
                        return *(uint*)(ptr + addr);
                    }
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                    RomReads += 4;
                    addr = (addr - 0x08000000);
                    if (addr < Rom.Length)
                    {
                        fixed (byte* ptr = Rom)
                        {
                            return *(uint*)(ptr + addr);
                        }
                    }
                    else
                    {
                        return 0;
                    }
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    goto default;

                default:
                    byte f0 = Read8(addr++);
                    byte f1 = Read8(addr++);
                    byte f2 = Read8(addr++);
                    byte f3 = Read8(addr++);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadDebug8(uint addr)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    return Bios[(addr - 0x00000000) & 0x3FFF];
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    return Ewram[(addr - 0x02000000) & 0x3FFFF];
                case 0x3: // IWRAM
                    return Iwram[(addr - 0x03000000) & 0x7FFF];
                case 0x4: // I/O Registers
                    addr &= 0x400FFFF;

                    // uint count;
                    // HwioReadLog.TryGetValue(addr, out count);
                    // HwioReadLog[addr] = count + 1;

                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
                    addr = (addr - 0x05000000) & 0x3FF;
                    return Gba.Lcd.Palettes[addr];
                case 0x6: // PPU VRAM
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    return Gba.Lcd.Vram[addr];
                case 0x7: // PPU OAM
                    addr = (addr - 0x07000000) & 0x3FF;
                    return Gba.Lcd.Oam[addr];
                case 0x8: // Game Pak ROM/FlashROM 
                    return Rom[addr - 0x08000000];
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                    return ReadFlash(addr);
                case 0xF: // Game Pak SRAM/Flash
                    break;
            }

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
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    break;
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    EwramWrites++;
                    Ewram[(addr - 0x02000000) & 0x3FFFF] = val;
                    break;
                case 0x3: // IWRAM
                    IwramWrites++;
                    Iwram[(addr - 0x03000000) & 0x7FFF] = val;
                    break;
                case 0x4: // I/O Registers
                    addr &= 0x400FFFF;

                    uint count;
                    HwioWriteLog.TryGetValue(addr, out count);
                    HwioWriteLog[addr] = count + 1;

                    HwioWrites++;
                    WriteHwio8(addr, val);
                    break;
                case 0x5: // PPU Palettes
                    PaletteWrites++;
                    addr = (addr - 0x05000000) & 0x3FF;
                    Gba.Lcd.UpdatePalette(addr / 2);
                    Gba.Lcd.Palettes[addr] = val;
                    return;
                case 0x6: // PPU VRAM
                    VramWrites++;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    Gba.Lcd.Vram[addr] = val;
                    return;
                case 0x7: // PPU OAM
                    OamWrites++;
                    addr = (addr - 0x07000000) & 0x3FF;
                    Gba.Lcd.Oam[addr] = val;
                    return;
                case 0x8: // Game Pak ROM/FlashROM 
                    break;
                case 0x9: // Game Pak ROM/FlashROM 
                    break;
                case 0xA: // Game Pak ROM/FlashROM 
                    break;
                case 0xB: // Game Pak ROM/FlashROM 
                    break;
                case 0xC: // Game Pak ROM/FlashROM 
                    break;
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    WriteFlash(addr);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramWrites += 2;
                    addr = (addr - 0x02000000) & 0x3FFFF;
                    fixed (byte* ptr = Ewram)
                    {
                        *(ushort*)(ptr + addr) = val;
                        return;
                    }
                case 0x3: // IWRAM
                    IwramWrites += 2;
                    addr = (addr - 0x03000000) & 0x7FFF;
                    fixed (byte* ptr = Iwram)
                    {
                        *(ushort*)(ptr + addr) = val;
                        return;
                    }
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteWrites += 2;
                    addr = (addr - 0x05000000) & 0x3FF;
                    Gba.Lcd.UpdatePalette(addr / 2);
                    fixed (byte* ptr = Gba.Lcd.Palettes)
                    {
                        *(ushort*)(ptr + addr) = val;
                        return;
                    }
                case 0x6: // PPU VRAM
                    VramWrites += 2;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    fixed (byte* ptr = Gba.Lcd.Vram)
                    {
                        *(ushort*)(ptr + addr) = val;
                        return;
                    }
                case 0x7: // PPU OAM
                    OamWrites += 2;
                    addr = (addr - 0x07000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Oam)
                    {
                        *(ushort*)(ptr + addr) = val;
                        return;
                    }
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    goto default;

                default:
                    byte f0 = (byte)(val >> 0);
                    byte f1 = (byte)(val >> 8);

                    Write8(addr++, f0);
                    Write8(addr++, f1);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write32(uint addr, uint val)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramWrites += 4;
                    addr = (addr - 0x02000000) & 0x3FFFF;
                    fixed (byte* ptr = Ewram)
                    {
                        *(uint*)(ptr + addr) = val;
                        return;
                    }
                case 0x3: // IWRAM
                    IwramWrites += 4;
                    addr = (addr - 0x03000000) & 0x7FFF;
                    fixed (byte* ptr = Iwram)
                    {
                        *(uint*)(ptr + addr) = val;
                        return;
                    }
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteWrites += 4;
                    addr = (addr - 0x05000000) & 0x3FF;
                    Gba.Lcd.UpdatePalette(addr / 2);
                    fixed (byte* ptr = Gba.Lcd.Palettes)
                    {
                        *(uint*)(ptr + addr) = val;
                        return;
                    }
                case 0x6: // PPU VRAM
                    VramWrites += 4;
                    addr = (addr - 0x06000000) & 0x1FFFF;
                    fixed (byte* ptr = Gba.Lcd.Vram)
                    {
                        *(uint*)(ptr + addr) = val;
                        return;
                    }
                case 0x7: // PPU OAM
                    OamWrites += 4;
                    addr = (addr - 0x07000000) & 0x3FF;
                    fixed (byte* ptr = Gba.Lcd.Oam)
                    {
                        *(uint*)(ptr + addr) = val;
                        return;
                    }
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak SRAM/Flash
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    goto default;

                default:
                    byte f0 = (byte)(val >> 0);
                    byte f1 = (byte)(val >> 8);
                    byte f2 = (byte)(val >> 16);
                    byte f3 = (byte)(val >> 24);

                    Write8(addr++, f0);
                    Write8(addr++, f1);
                    Write8(addr++, f2);
                    Write8(addr++, f3);
                    return;
            }
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
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                return Gba.Timers.ReadHwio8(addr);
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
            else if (addr >= 0x4000060 && addr <= 0x40000A7) // Sound
            {
                Gba.GbaAudio.WriteHwio8(addr, val);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000DF) // DMA
            {
                Gba.Dma.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                Gba.Timers.WriteHwio8(addr, val);
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

        public byte ReadFlash(uint addr)
        {
            switch (addr)
            {
                // Stub out Flash
                case 0x0E000000: return 0x62;
                case 0x0E000001: return 0x13;
            }
            return 0;
        }

        public void WriteFlash(uint addr)
        {
            return;
        }
    }
}