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

        public override void InitPageTable(byte*[] table, uint[] maskTable, bool write)
        {
            byte* bios = TryPinByteArray(Bios);
            byte* ewram = TryPinByteArray(Ewram);
            byte* iwram = TryPinByteArray(Iwram);
            byte* palettes = TryPinByteArray(Gba.Ppu.Renderer.Palettes);
            byte* vram = TryPinByteArray(Gba.Ppu.Vram);
            byte* emptyPage = TryPinByteArray(EmptyPage);
            byte* oam = TryPinByteArray(Gba.Ppu.Renderer.Oam);
            byte* rom = TryPinByteArray(Rom);

            // 12 bits shaved off already, shave off another 12 to get 24
            for (uint i = 0; i < 1048576; i++)
            {
                uint addr = (uint)(i << 12);
                switch (i >> 12)
                {
                    case 0x0: // BIOS
                        if (!write)
                        {
                            table[i] = bios;
                        }
                        maskTable[i] = 0x00003FFF;
                        break;
                    case 0x2: // EWRAM
                        table[i] = ewram;
                        maskTable[i] = 0x0003FFFF;
                        break;
                    case 0x3: // IWRAM
                        table[i] = iwram;
                        maskTable[i] = 0x00007FFF;
                        break;
                    case 0x5: // Palettes
                        if (!write)
                        {
                            table[i] = palettes;
                        }
                        maskTable[i] = 0x3FF;
                        break;
                    case 0x6: // PPU VRAM
                        addr &= 0x1FFFF;
                        if (addr < 0x18000)
                        {
                            table[i] = vram;
                        }
                        else
                        {
                            table[i] = emptyPage;
                        }
                        maskTable[i] = 0x0001FFFF; // VRAM
                        break;
                    case 0x7: // PPU OAM
                        table[i] = oam;
                        maskTable[i] = 0x000003FF;
                        break;
                    case 0x8: // Game Pak ROM/FlashROM 
                    case 0x9: // Game Pak ROM/FlashROM 
                    case 0xA: // Game Pak ROM/FlashROM 
                    case 0xB: // Game Pak ROM/FlashROM 
                    case 0xC: // Game Pak ROM/FlashROM 
                        if (!write)
                        {
                            table[i] = rom;
                        }
                        maskTable[i] = 0x01FFFFFF;
                        break;
                    case 0xD: // Game Pak ROM/FlashROM/EEPROM
                        maskTable[i] = 0x01FFFFFF;
                        break;
                }
            }
        }

        ~MemoryGba()
        {
            Console.WriteLine("Cleaning up GBA memory...");
            UnpinByteArray(Bios);
            UnpinByteArray(Ewram);
            UnpinByteArray(Iwram);
            UnpinByteArray(Gba.Ppu.Renderer.Palettes);
            UnpinByteArray(Gba.Ppu.Vram);
            UnpinByteArray(EmptyPage);
            UnpinByteArray(Gba.Ppu.Renderer.Oam);
            UnpinByteArray(Rom);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override byte Read8Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    return ReadHwio8(debug, addr);
                case 0xA: // ROM / EEPROM
                case 0xB: // ROM / EEPROM
                case 0xC: // ROM / EEPROM
                case 0xD: // ROM / EEPROM
                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return GetByte(Rom, adjAddr);
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override ushort Read16Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    byte f0 = Read8Unregistered(debug, addr++);
                    byte f1 = Read8Unregistered(debug, addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
                case 0xA: // ROM / EEPROM
                case 0xB: // ROM / EEPROM
                case 0xC: // ROM / EEPROM
                case 0xD: // ROM / EEPROM
                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return GetUshort(Rom, adjAddr);
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override uint Read32Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    byte f0 = ReadHwio8(debug, addr++);
                    byte f1 = ReadHwio8(debug, addr++);
                    byte f2 = ReadHwio8(debug, addr++);
                    byte f3 = ReadHwio8(debug, addr++);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write8Unregistered(bool debug, uint addr, byte val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    WriteHwio8(debug, addr, val);
                    break;
                case 0xA: // ROM / EEPROM
                case 0xB: // ROM / EEPROM
                case 0xC: // ROM / EEPROM
                case 0xD: // ROM / EEPROM
                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        SaveProvider.Write8(adjAddr, val);
                    }
                    break;
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    SaveProvider.Write8(addr, val);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write16Unregistered(bool debug, uint addr, ushort val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    break;
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    if (GetUshort(Gba.Ppu.Renderer.Palettes, addr) != val)
                    {
                        SetUshort(Gba.Ppu.Renderer.Palettes, addr, val);
                    }
                    break;
                case 0xA: // ROM / EEPROM
                case 0xB: // ROM / EEPROM
                case 0xC: // ROM / EEPROM
                case 0xD: // ROM / EEPROM
                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        SaveProvider.Write8(adjAddr, (byte)val);
                    }
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write32Unregistered(bool debug, uint addr, uint val)
        {
            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    WriteHwio8(debug, addr++, (byte)(val >> 16));
                    WriteHwio8(debug, addr++, (byte)(val >> 24));
                    break;
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    if (GetUint(Gba.Ppu.Renderer.Palettes, addr) != val)
                    {
                        SetUint(Gba.Ppu.Renderer.Palettes, addr, val);
                    }
                    return;
                case 0x6: // PPU VRAM
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        SetUint(Gba.Ppu.Vram, addr, val);
                    }
                    return;
            }

        }

        public byte ReadHwio8(bool debug, uint addr)
        {
            if (LogHwioAccesses && (addr & ~1) != 0 && !debug)
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
                switch (addr) {
                    case 0x4000135: return 0x80;
                }
            }
            else if (addr >= 0x4000200 && addr <= 0x4FF0800) // Interrupt, Waitstate, and Power-Down Control
            {
                return Gba.HwControl.ReadHwio8(addr);
            }
            return 0;
        }

        public void WriteHwio8(bool debug, uint addr, byte val)
        {
            if (LogHwioAccesses && (addr & ~1) != 0 && !debug)
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
