using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;

namespace OptimeGBA
{
    public sealed unsafe class MemoryGba : Memory
    {
        Gba Gba;

        public MemoryGba(Gba gba, ProviderGba provider) : base(gba)
        {
            Gba = gba;

            for (uint i = 0; i < MaxRomSize && i < provider.Rom.Length; i++)
            {
                Rom[i] = provider.Rom[i];
            }

            for (uint i = 0; i < BiosSize && i < provider.Bios.Length; i++)
            {
                Bios[i] = provider.Bios[i];
            }

            RomSize = (uint)provider.Rom.Length;

            // Detect save type

            string[] strings = {
                "NONE_LOLOLLEXTRATONOTMATCHRANDOMSTRINGS",
                "EEPROM_",
                "SRAM_",
                "FLASH_",
                "FLASH512_",
                "FLASH1M_",
            };
            uint matchedIndex = 0;

            for (uint i = 0; i < strings.Length; i++)
            {
                char[] chars = strings[i].ToCharArray();

                int stringLength = chars.Length;
                int matchLength = 0;
                for (uint j = 0; j < provider.Rom.Length; j++)
                {
                    if (provider.Rom[j] == chars[matchLength])
                    {
                        matchLength++;
                        if (matchLength >= chars.Length)
                        {
                            matchedIndex = i;
                            goto breakOuterLoop;
                        }
                    }
                    else
                    {
                        matchLength = 0;
                    }
                }
            }
        breakOuterLoop:

            Console.WriteLine($"Save Type: {strings[matchedIndex]}");

            switch (matchedIndex)
            {
                case 0: SaveProvider = new NullSaveProvider(); break;
                case 1:
                    SaveProvider = new Eeprom(Gba, EepromSize.Eeprom64k);
                    if (RomSize < 16777216)
                    {
                        EepromThreshold = 0x1000000;
                    }
                    else
                    {
                        EepromThreshold = 0x1FFFF00;
                    }
                    Console.WriteLine("EEPROM Threshold: " + Util.Hex(EepromThreshold, 8));
                    break;
                case 2: SaveProvider = new Sram(); break;
                case 3: SaveProvider = new Flash(Gba, FlashSize.Flash512k); break;
                case 4: SaveProvider = new Flash(Gba, FlashSize.Flash512k); break;
                case 5: SaveProvider = new Flash(Gba, FlashSize.Flash1m); break;
            }
        }

        public uint EepromThreshold = 0x2000000;

        public const int BiosSize = 16384;
        public const int MaxRomSize = 67108864;
        public const int EwramSize = 262144;
        public const int IwramSize = 32768;
        public uint RomSize;

        public byte[] Bios = MemoryUtil.AllocateManagedArray(BiosSize);
        public byte[] Rom = MemoryUtil.AllocateManagedArray(MaxRomSize);
        public byte[] Ewram = MemoryUtil.AllocateManagedArray(EwramSize);
        public byte[] Iwram = MemoryUtil.AllocateManagedArray(IwramSize);

        public override void InitPageTable(byte[][] table, bool write)
        {
            MemoryRegionMasks[0x0] = 0x00003FFF; // BIOS
            MemoryRegionMasks[0x1] = 0x00000000; // Unused
            MemoryRegionMasks[0x2] = 0x0003FFFF; // EWRAM
            MemoryRegionMasks[0x3] = 0x00007FFF; // IWRAM
            MemoryRegionMasks[0x4] = 0x00000000; // I/O
            MemoryRegionMasks[0x5] = 0x000003FF; // Palettes
            MemoryRegionMasks[0x6] = 0x0001FFFF; // VRAM
            MemoryRegionMasks[0x7] = 0x000003FF; // OAM
            MemoryRegionMasks[0x8] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0x9] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0xA] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0xB] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0xC] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0xD] = 0x01FFFFFF; // ROM
            MemoryRegionMasks[0xE] = 0x00000000; // SRAM / FLASH
            MemoryRegionMasks[0xF] = 0x00000000; // SRAM / FLASH

            // 10 bits shaved off already, shave off another 14 to get 24
            for (uint i = 0; i < 4194304; i++)
            {
                uint addr = (uint)(i << 10);
                switch (i >> 14)
                {
                    case 0x0: // BIOS
                        if (!write)
                        {
                            table[i] = Bios;
                        }
                        break;
                    case 0x2: // EWRAM
                        table[i] = Ewram;
                        break;
                    case 0x3: // IWRAM
                        table[i] = Iwram;
                        break;
                    case 0x5: // Palettes
                        if (!write)
                        {
                            table[i] = Gba.Ppu.Renderer.Palettes;
                        }
                        break;
                    case 0x6: // PPU VRAM
                        addr &= 0x1FFFF;
                        if (addr < 0x18000)
                        {
                            table[i] = Gba.Ppu.Renderer.Vram;
                        }
                        else
                        {
                            table[i] = EmptyPage;
                        }
                        break;
                    case 0x7: // PPU OAM
                        addr &= 0x3FF;
                        table[i] = Gba.Ppu.Renderer.Oam;
                        break;
                    case 0x8: // Game Pak ROM/FlashROM 
                    case 0x9: // Game Pak ROM/FlashROM 
                    case 0xA: // Game Pak ROM/FlashROM 
                    case 0xB: // Game Pak ROM/FlashROM 
                    case 0xC: // Game Pak ROM/FlashROM 
                    case 0xD: // Game Pak ROM/FlashROM 
                        if (!write)
                        {
                            table[i] = Rom;
                        }
                        break;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override byte Read8Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    return ReadHwio8(addr);
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override ushort Read16Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    byte f0 = Read8Unregistered(addr++);
                    byte f1 = Read8Unregistered(addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override uint Read32Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    byte f0 = Read8Unregistered(addr++);
                    byte f1 = Read8Unregistered(addr++);
                    byte f2 = Read8Unregistered(addr++);
                    byte f3 = Read8Unregistered(addr++);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write8Unregistered(uint addr, byte val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    WriteHwio8(addr, val);
                    break;
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    SaveProvider.Write8(addr, val);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write16Unregistered(uint addr, ushort val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    WriteHwio8(addr++, (byte)(val >> 0));
                    WriteHwio8(addr++, (byte)(val >> 8));
                    break;
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    if (GetUshort(Gba.Ppu.Renderer.Palettes, addr) != val)
                    {
                        SetUshort(Gba.Ppu.Renderer.Palettes, addr, val);
                        Gba.Ppu.Renderer.UpdatePalette((addr & ~1u) / 2);
                    }
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write32Unregistered(uint addr, uint val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    WriteHwio8(addr++, (byte)(val >> 0));
                    WriteHwio8(addr++, (byte)(val >> 8));
                    WriteHwio8(addr++, (byte)(val >> 16));
                    WriteHwio8(addr++, (byte)(val >> 24));
                    break;
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    if (GetUint(Gba.Ppu.Renderer.Palettes, addr) != val)
                    {
                        SetUint(Gba.Ppu.Renderer.Palettes, addr, val);
                        Gba.Ppu.Renderer.UpdatePalette((addr & ~3u) / 2 + 0);
                        Gba.Ppu.Renderer.UpdatePalette((addr & ~3u) / 2 + 1);
                    }
                    return;
                case 0x6: // PPU VRAM
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        SetUint(Gba.Ppu.Renderer.Vram, addr, val);
                    }
                    return;
            }

        }

        public byte ReadHwio8(uint addr)
        {
            if (LogHwioAccesses && (addr & ~1) != 0)
            {
                uint count;
                HwioWriteLog.TryGetValue(addr, out count);
                HwioWriteLog[addr] = count + 1;
            }

            if (addr >= 0x4000000 && addr <= 0x4000056) // PPU
            {
                return Gba.Ppu.ReadHwio8(addr);
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

        public void WriteHwio8(uint addr, byte val)
        {

            if (LogHwioAccesses && (addr & ~1) != 0)
            {
                uint count;
                HwioReadLog.TryGetValue(addr, out count);
                HwioReadLog[addr] = count + 1;
            }

            if (addr >= 0x4000000 && addr <= 0x4000056) // PPU
            {
                Gba.Ppu.WriteHwio8(addr, val);
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
    }
}
