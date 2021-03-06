using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;

namespace OptimeGBA
{
    public sealed unsafe class Memory
    {
        Gba Gba;


        public Memory(Gba gba, GbaProvider provider)
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
                case 3: SaveProvider = new Flash(FlashSize.Flash512k); break;
                case 4: SaveProvider = new Flash(FlashSize.Flash512k); break;
                case 5: SaveProvider = new Flash(FlashSize.Flash1m); break;
            }
        }

        public uint EepromThreshold = 0x2000000;

        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();
        public bool LogHwioAccesses = false;

        public SaveProvider SaveProvider = new NullSaveProvider();

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

        public const int BiosSize = 16384;
        public const int MaxRomSize = 67108864;
        public const int EwramSize = 262144;
        public const int IwramSize = 32768;
        public uint RomSize;

#if UNSAFE
        public byte* Bios = Memory.AllocateUnmanagedArray(BiosSize);
        public byte* Rom = Memory.AllocateUnmanagedArray(MaxRomSize);
        public byte* Ewram = Memory.AllocateUnmanagedArray(EwramSize);
        public byte* Iwram = Memory.AllocateUnmanagedArray(IwramSize);

        ~Memory()
        {
            Memory.FreeUnmanagedArray(Bios);
            Memory.FreeUnmanagedArray(Rom);
            Memory.FreeUnmanagedArray(Ewram);
            Memory.FreeUnmanagedArray(Iwram);
        }
#else
        public byte[] Bios = Memory.AllocateManagedArray(BiosSize);
        public byte[] Rom = Memory.AllocateManagedArray(MaxRomSize);
        public byte[] Ewram = Memory.AllocateManagedArray(EwramSize);
        public byte[] Iwram = Memory.AllocateManagedArray(IwramSize);
#endif

        public byte Read8(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x0: // BIOS
#if OPENTK_DEBUGGER
                    BiosReads++;
#endif
                    addr &= 0x3FFF;
                    return GetByte(Bios, addr);
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramReads++;
#endif
                    addr &= 0x3FFFF;
                    return GetByte(Ewram, addr);
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramReads++;
#endif
                    addr &= 0x7FFF;
                    return GetByte(Iwram, addr);
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        HwioReadLog.TryGetValue(addr, out uint count);
                        HwioReadLog[addr] = count + 1;
                    }

#if OPENTK_DEBUGGER
                    HwioReads++;
#endif
                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
#if OPENTK_DEBUGGER
                    PaletteReads++;
#endif
                    addr &= 0x3FF;
                    return GetByte(Gba.Ppu.Palettes, addr);
                case 0x6: // PPU VRAM
#if OPENTK_DEBUGGER
                    VramReads++;
#endif
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return GetByte(Gba.Ppu.Vram, addr);
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
#if OPENTK_DEBUGGER
                    OamReads++;
#endif
                    addr &= 0x3FF;
                    return GetByte(Gba.Ppu.Oam, addr);
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
#if OPENTK_DEBUGGER
                    RomReads++;
#endif
                    addr &= 0x1FFFFFF;
                    return GetByte(Rom, addr);
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        public ushort Read16(uint addr)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Read16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }
#endif

            switch (addr >> 24)
            {
                case 0x0: // BIOS
#if OPENTK_DEBUGGER
                    BiosReads += 2;
#endif
                    addr &= 0x3FFF;
                    return GetUshort(Bios, addr);
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramReads += 2;
#endif
                    addr &= 0x3FFFF;
                    return GetUshort(Ewram, addr);
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramReads += 2;
#endif
                    addr &= 0x7FFF;
                    return GetUshort(Iwram, addr);
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
#if OPENTK_DEBUGGER
                    PaletteReads += 2;
#endif
                    addr &= 0x3FF;
                    return GetUshort(Gba.Ppu.Palettes, addr);
                case 0x6: // PPU VRAM
#if OPENTK_DEBUGGER
                    VramReads += 2;
#endif
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return GetUshort(Gba.Ppu.Vram, addr);
                    }
                    else
                    {
                        return 0;
                    }

                case 0x7: // PPU OAM
#if OPENTK_DEBUGGER
                    OamReads += 2;
#endif
                    addr &= 0x3FF;
                    return GetUshort(Gba.Ppu.Oam, addr);
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
#if OPENTK_DEBUGGER
                    RomReads += 2;
#endif

                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return GetUshort(Rom, adjAddr);
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


        public uint Read32(uint addr)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Gba.Arm7.Error("Misaligned Read32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }
#endif

            switch (addr >> 24)
            {
                case 0x0: // BIOS
#if OPENTK_DEBUGGER
                    BiosReads += 4;
#endif
                    addr &= 0x3FFF;
                    return GetUint(Bios, addr);
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramReads += 4;
#endif
                    addr &= 0x3FFFF;
                    return GetUint(Ewram, addr);
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramReads += 4;
#endif
                    addr &= 0x7FFF;
                    return GetUint(Iwram, addr);
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
#if OPENTK_DEBUGGER
                    PaletteReads += 4;
#endif
                    addr &= 0x3FF;
                    return GetUint(Gba.Ppu.Palettes, addr);
                case 0x6: // PPU VRAM
#if OPENTK_DEBUGGER
                    VramReads += 4;
#endif
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return GetUint(Gba.Ppu.Vram, addr);
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
#if OPENTK_DEBUGGER
                    OamReads += 4;
#endif
                    addr &= 0x3FF;
                    return GetUint(Gba.Ppu.Oam, addr);
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
#if OPENTK_DEBUGGER
                    RomReads += 4;
#endif

                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return GetUint(Rom, adjAddr);
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

        public byte ReadDebug8(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x0: // BIOS
                    addr &= 0x3FFF;
                    return GetByte(Bios, addr);
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    addr &= 0x3FFFF;
                    return GetByte(Ewram, addr);
                case 0x3: // IWRAM
                    addr &= 0x7FFF;
                    return GetByte(Iwram, addr);
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    return GetByte(Iwram, addr);
                case 0x6: // PPU VRAM
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return GetByte(Gba.Ppu.Vram, addr);
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
                    addr &= 0x3FF;
                    return GetByte(Gba.Ppu.Oam, addr);
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak SRAM/Flash
                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        Console.WriteLine("EEPROM Read");
                        return SaveProvider.Read8(adjAddr);
                    }

                    return GetByte(Rom, adjAddr);
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        public ushort ReadDebug16(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);

            ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

            return u16;
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
            switch (addr >> 24)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramWrites++;
#endif
                    addr &= 0x3FFFF;
                    SetByte(Ewram, addr, val);
                    break;
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramWrites++;
#endif
                    addr &= 0x7FFF;
                    SetByte(Iwram, addr, val);
                    break;
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        HwioWriteLog.TryGetValue(addr, out uint count);
                        HwioWriteLog[addr] = count + 1;
                    }

#if OPENTK_DEBUGGER
                    HwioWrites++;
#endif
                    WriteHwio8(addr, val);
                    break;
                case 0x5: // PPU Palettes
                    // Gba.Arm7.Error("Write: Palette8");
                    return;
                case 0x6: // PPU VRAM
                    return;
                case 0x7: // PPU OAM
                    return;
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM
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

        public void Write16(uint addr, ushort val)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Write16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }
#endif

            switch (addr >> 24)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramWrites += 2;
#endif
                    addr &= 0x3FFFF;
                    SetUshort(Ewram, addr, val);
                    return;
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramWrites += 2;
#endif
                    addr &= 0x7FFF;
                    SetUshort(Iwram, addr, val);
                    return;
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                          // Gba.Arm7.Error("Write: Palette16");
#if OPENTK_DEBUGGER
                    PaletteWrites += 2;
#endif
                    addr &= 0x3FF;
                    if (GetUshort(Gba.Ppu.Palettes, addr) != val)
                    {
                        SetUshort(Gba.Ppu.Palettes, addr, val);
                        Gba.Ppu.UpdatePalette((addr & ~1u) / 2);
                    }
                    return;
                case 0x6: // PPU VRAM
#if OPENTK_DEBUGGER
                    VramWrites += 2;
#endif
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        SetUshort(Gba.Ppu.Vram, addr, val);
                    }
                    return;
                case 0x7: // PPU OAM
#if OPENTK_DEBUGGER
                    OamWrites += 2;
#endif
                    addr &= 0x3FF;
                    SetUshort(Gba.Ppu.Oam, addr, val);
                    return;
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM
                    uint adjAddr = addr & 0x1FFFFFF;

                    if (adjAddr >= EepromThreshold)
                    {
                        SaveProvider.Write8(adjAddr, (byte)val);
                    }
                    break;
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

        public void Write32(uint addr, uint val)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Gba.Arm7.Error("Misaligned Write32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }
#endif

            switch (addr >> 24)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
#if OPENTK_DEBUGGER
                    EwramWrites += 4;
#endif
                    addr &= 0x3FFFF;
                    SetUint(Ewram, addr, val);
                    return;
                case 0x3: // IWRAM
#if OPENTK_DEBUGGER
                    IwramWrites += 4;
#endif
                    addr &= 0x7FFF;
                    SetUint(Iwram, addr, val);
                    return;
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                          // Gba.Arm7.Error("Write: Palette32");
#if OPENTK_DEBUGGER
                    PaletteWrites += 4;
#endif
                    addr &= 0x3FF;
                    if (GetUint(Gba.Ppu.Palettes, addr) != val)
                    {
                        SetUint(Gba.Ppu.Palettes, addr, val);
                        Gba.Ppu.UpdatePalette((addr & ~3u) / 2 + 0);
                        Gba.Ppu.UpdatePalette((addr & ~3u) / 2 + 1);
                    }
                    return;
                case 0x6: // PPU VRAM
#if OPENTK_DEBUGGER
                    VramWrites += 4;
#endif
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        SetUint(Gba.Ppu.Vram, addr, val);
                    }
                    return;
                case 0x7: // PPU OAM
#if OPENTK_DEBUGGER
                    OamWrites += 4;
#endif
                    addr &= 0x3FF;
                    SetUint(Gba.Ppu.Oam, addr, val);
                    return;
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

        public byte ReadHwio8(uint addr)
        {
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetUint(byte[] arr, uint addr)
        {
            return (uint)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8) |
                    (arr[addr + 2] << 16) |
                    (arr[addr + 3] << 24)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetUshort(byte[] arr, uint addr)
        {
            return (ushort)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByte(byte[] arr, uint addr)
        {
            return arr[addr];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetUint(byte* arr, uint addr)
        {
            return (uint)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8) |
                    (arr[addr + 2] << 16) |
                    (arr[addr + 3] << 24)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetUshort(byte* arr, uint addr)
        {
            return (ushort)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByte(byte* arr, uint addr)
        {
            return arr[addr];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUint(byte[] arr, uint addr, uint val)
        {
            arr[addr + 0] = (byte)(val >> 0);
            arr[addr + 1] = (byte)(val >> 8);
            arr[addr + 2] = (byte)(val >> 16);
            arr[addr + 3] = (byte)(val >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUshort(byte[] arr, uint addr, ushort val)
        {
            arr[addr + 0] = (byte)(val >> 0);
            arr[addr + 1] = (byte)(val >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetByte(byte[] arr, uint addr, byte val)
        {
            arr[addr] = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUint(byte* arr, uint addr, uint val)
        {
            *(uint*)(arr + addr) = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUshort(byte* arr, uint addr, ushort val)
        {
            *(ushort*)(arr + addr) = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetByte(byte* arr, uint addr, byte val)
        {
            *(byte*)(arr + addr) = val;
        }

        public static byte* AllocateUnmanagedArray(int size)
        {
            byte* arr = (byte*)Marshal.AllocHGlobal(size).ToPointer();

            // Zero out array
            for (int i = 0; i < size; i++)
            {
                arr[i] = 0;
            }

            return arr;
        }

        public static uint* AllocateUnmanagedArray32(int size)
        {
            uint* arr = (uint*)Marshal.AllocHGlobal(size * sizeof(uint)).ToPointer();

            // Zero out array
            for (int i = 0; i < size; i++)
            {
                arr[i] = 0;
            }

            return arr;
        }

        public static void FreeUnmanagedArray(void* arr)
        {
            Marshal.FreeHGlobal(new IntPtr(arr));
        }

        public static byte[] AllocateManagedArray(int size)
        {
            return new byte[size];
        }

        public static uint[] AllocateManagedArray32(int size)
        {
            return new uint[size];
        }
    }
}
