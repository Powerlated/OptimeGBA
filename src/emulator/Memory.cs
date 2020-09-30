using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class Memory
    {
        GBA Gba;


        public Memory(GBA gba, GbaProvider provider)
        {
            Gba = gba;

            for (uint i = 0; i < Ewram.Length; i++)
            {
                Ewram[i] = 0x00;
            }

            for (uint i = 0; i < Iwram.Length; i++)
            {
                Iwram[i] = 0x00;
            }

            provider.Bios.CopyTo(Bios, 0);
            provider.Rom.CopyTo(Rom, 0);
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
                    SaveProvider = new Eeprom(EepromSize.Eeprom64k);
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

        public byte[] Bios = new byte[16384];
        public byte[] Rom = new byte[67108864];
        public uint RomSize;

        // External Work RAM
        public byte[] Ewram = new byte[262144];
        // Internal Work RAM
        public byte[] Iwram = new byte[32768];

        public byte Read8(uint addr)
        {
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads++;
                    return Bios[addr & 0x3FFF];
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    EwramReads++;
                    return Ewram[addr & 0x3FFFF];
                case 0x3: // IWRAM
                    IwramReads++;
                    return Iwram[addr & 0x7FFF];
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        uint count;
                        HwioReadLog.TryGetValue(addr, out count);
                        HwioReadLog[addr] = count + 1;
                    }

                    HwioReads++;
                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
                    PaletteReads++;
                    addr &= 0x3FF;
                    return Gba.Lcd.Palettes[addr];
                case 0x6: // PPU VRAM
                    VramReads++;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return Gba.Lcd.Vram[addr];
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
                    OamReads++;
                    addr &= 0x3FF;
                    return Gba.Lcd.Oam[addr];
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
                    RomReads++;
                    addr &= 0x1FFFFFF;
                    return Rom[addr];
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        public ushort Read16(uint addr)
        {
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Read16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }

            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads += 2;
                    addr &= 0x3FFF;
                    return (ushort)(
                           (Bios[addr + 0] << 0) |
                           (Bios[addr + 1] << 8)
                       );
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramReads += 2;
                    addr &= 0x3FFFF;
                    return (ushort)(
                             (Ewram[addr + 0] << 0) |
                             (Ewram[addr + 1] << 8)
                         );
                case 0x3: // IWRAM
                    IwramReads += 2;
                    addr &= 0x7FFF;
                    return (ushort)(
                           (Iwram[addr + 0] << 0) |
                           (Iwram[addr + 1] << 8)
                       );
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteReads += 2;
                    addr &= 0x3FF;
                    return (ushort)(
                           (Gba.Lcd.Palettes[addr + 0] << 0) |
                           (Gba.Lcd.Palettes[addr + 1] << 8)
                       );
                case 0x6: // PPU VRAM
                    VramReads += 2;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return (ushort)(
                            (Gba.Lcd.Vram[addr + 0] << 0) |
                            (Gba.Lcd.Vram[addr + 1] << 8)
                        );
                    }
                    else
                    {
                        return 0;
                    }

                case 0x7: // PPU OAM
                    OamReads += 2;
                    addr &= 0x3FF;
                    return (ushort)(
                             (Gba.Lcd.Oam[addr + 0] << 0) |
                             (Gba.Lcd.Oam[addr + 1] << 8)
                         );
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
                    RomReads += 2;

                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return (ushort)(
                        (Rom[adjAddr + 0] << 0) |
                        (Rom[adjAddr + 1] << 8)
                    );
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
            if ((addr & 3) != 0)
            {
                Gba.Arm7.Error("Misaligned Read32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }

            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    BiosReads += 4;
                    addr &= 0x3FFF;
                    return (uint)(
                            (Bios[addr + 0] << 0) |
                            (Bios[addr + 1] << 8) |
                            (Bios[addr + 2] << 16) |
                            (Bios[addr + 3] << 24)
                         );
                case 0x1: // Unused
                    goto default;
                case 0x2: // EWRAM
                    EwramReads += 4;
                    addr &= 0x3FFFF;
                    return (uint)(
                           (Ewram[addr + 0] << 0) |
                           (Ewram[addr + 1] << 8) |
                           (Ewram[addr + 2] << 16) |
                           (Ewram[addr + 3] << 24)
                        );
                case 0x3: // IWRAM
                    IwramReads += 4;
                    addr &= 0x7FFF;
                    return (uint)(
                              (Iwram[addr + 0] << 0) |
                              (Iwram[addr + 1] << 8) |
                              (Iwram[addr + 2] << 16) |
                              (Iwram[addr + 3] << 24)
                           );
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    PaletteReads += 4;
                    addr &= 0x3FF;
                    return (uint)(
                                (Gba.Lcd.Palettes[addr + 0] << 0) |
                                (Gba.Lcd.Palettes[addr + 1] << 8) |
                                (Gba.Lcd.Palettes[addr + 2] << 16) |
                                (Gba.Lcd.Palettes[addr + 3] << 24)
                             );
                case 0x6: // PPU VRAM
                    VramReads += 4;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return (uint)(
                            (Gba.Lcd.Vram[addr + 0] << 0) |
                            (Gba.Lcd.Vram[addr + 1] << 8) |
                            (Gba.Lcd.Vram[addr + 2] << 16) |
                            (Gba.Lcd.Vram[addr + 3] << 24)
                        );
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
                    OamReads += 4;
                    addr &= 0x3FF;
                    return (uint)(
                                  (Gba.Lcd.Oam[addr + 0] << 0) |
                                  (Gba.Lcd.Oam[addr + 1] << 8) |
                                  (Gba.Lcd.Oam[addr + 2] << 16) |
                                  (Gba.Lcd.Oam[addr + 3] << 24)
                               );
                case 0x8: // Game Pak ROM/FlashROM 
                case 0x9: // Game Pak ROM/FlashROM 
                case 0xA: // Game Pak ROM/FlashROM 
                case 0xB: // Game Pak ROM/FlashROM 
                case 0xC: // Game Pak ROM/FlashROM 
                case 0xD: // Game Pak ROM/FlashROM 
                    RomReads += 4;

                    uint adjAddr = addr & 0x1FFFFFF;
                    if (adjAddr >= EepromThreshold)
                    {
                        return SaveProvider.Read8(adjAddr);
                    }

                    return (uint)(
                            (Rom[adjAddr + 0] << 0) |
                            (Rom[adjAddr + 1] << 8) |
                            (Rom[adjAddr + 2] << 16) |
                            (Rom[adjAddr + 3] << 24)
                         );
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
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                    return Bios[addr & 0x3FFF];
                case 0x1: // Unused
                    break;
                case 0x2: // EWRAM
                    return Ewram[addr & 0x3FFFF];
                case 0x3: // IWRAM
                    return Iwram[addr & 0x7FFF];
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;
                    return ReadHwio8(addr);
                case 0x5: // PPU Palettes
                    addr &= 0x3FF;
                    return Gba.Lcd.Palettes[addr];
                case 0x6: // PPU VRAM
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        return Gba.Lcd.Vram[addr];
                    }
                    else
                    {
                        return 0;
                    }
                case 0x7: // PPU OAM
                    addr &= 0x3FF;
                    return Gba.Lcd.Oam[addr];
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

                    return Rom[adjAddr];
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
            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
                    EwramWrites++;
                    Ewram[addr & 0x3FFFF] = val;
                    break;
                case 0x3: // IWRAM
                    IwramWrites++;
                    Iwram[addr & 0x7FFF] = val;
                    break;
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        uint count;
                        HwioWriteLog.TryGetValue(addr, out count);
                        HwioWriteLog[addr] = count + 1;
                    }

                    HwioWrites++;
                    WriteHwio8(addr, val);
                    break;
                case 0x5: // PPU Palettes
                    // Gba.Arm7.Error("Write: Palette8");
                    PaletteWrites++;
                    addr &= 0x3FF;
                    Gba.Lcd.Palettes[addr] = val;
                    Gba.Lcd.UpdatePalette(addr / 2);
                    return;
                case 0x6: // PPU VRAM
                    VramWrites++;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        Gba.Lcd.Vram[addr] = val;
                    }
                    return;
                case 0x7: // PPU OAM
                    OamWrites++;
                    addr &= 0x3FF;
                    Gba.Lcd.Oam[addr] = val;
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
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Write16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }

            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
                    EwramWrites += 2;
                    addr &= 0x3FFFF;
                    Ewram[addr + 0] = (byte)(val >> 0);
                    Ewram[addr + 1] = (byte)(val >> 8);
                    return;
                case 0x3: // IWRAM
                    IwramWrites += 2;
                    addr &= 0x7FFF;
                    Iwram[addr + 0] = (byte)(val >> 0);
                    Iwram[addr + 1] = (byte)(val >> 8);
                    return;
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    // Gba.Arm7.Error("Write: Palette16");
                    PaletteWrites += 2;
                    addr &= 0x3FF;
                    Gba.Lcd.Palettes[addr + 0] = (byte)(val >> 0);
                    Gba.Lcd.Palettes[addr + 1] = (byte)(val >> 8);
                    Gba.Lcd.UpdatePalette((addr & ~1u) / 2);
                    return;
                case 0x6: // PPU VRAM
                    VramWrites += 2;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        Gba.Lcd.Vram[addr + 0] = (byte)(val >> 0);
                        Gba.Lcd.Vram[addr + 1] = (byte)(val >> 8);
                    }
                    return;
                case 0x7: // PPU OAM
                    OamWrites += 2;
                    addr &= 0x3FF;
                    Gba.Lcd.Oam[addr + 0] = (byte)(val >> 0);
                    Gba.Lcd.Oam[addr + 1] = (byte)(val >> 8);
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

                    Write8(addr++, f0);
                    Write8(addr++, f1);
                    return;
            }
        }

        public void Write32(uint addr, uint val)
        {
            if ((addr & 3) != 0)
            {
                Gba.Arm7.Error("Misaligned Write32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Gba.Arm7.R[15], 8));
            }

            switch ((addr >> 24) & 0xF)
            {
                case 0x0: // BIOS
                case 0x1: // Unused
                    return;
                case 0x2: // EWRAM
                    EwramWrites += 4;
                    addr &= 0x3FFFF;
                    Ewram[addr + 0] = (byte)(val >> 0);
                    Ewram[addr + 1] = (byte)(val >> 8);
                    Ewram[addr + 2] = (byte)(val >> 16);
                    Ewram[addr + 3] = (byte)(val >> 24);
                    return;
                case 0x3: // IWRAM
                    IwramWrites += 4;
                    addr &= 0x7FFF;
                    Iwram[addr + 0] = (byte)(val >> 0);
                    Iwram[addr + 1] = (byte)(val >> 8);
                    Iwram[addr + 2] = (byte)(val >> 16);
                    Iwram[addr + 3] = (byte)(val >> 24);
                    return;
                case 0x4: // I/O Registers
                    goto default;
                case 0x5: // PPU Palettes
                    // Gba.Arm7.Error("Write: Palette32");
                    PaletteWrites += 4;
                    addr &= 0x3FF;
                    Gba.Lcd.Palettes[addr + 0] = (byte)(val >> 0);
                    Gba.Lcd.Palettes[addr + 1] = (byte)(val >> 8);
                    Gba.Lcd.Palettes[addr + 2] = (byte)(val >> 16);
                    Gba.Lcd.Palettes[addr + 3] = (byte)(val >> 24);
                    Gba.Lcd.UpdatePalette((addr & ~3u) / 2 + 0);
                    Gba.Lcd.UpdatePalette((addr & ~3u) / 2 + 1);
                    return;
                case 0x6: // PPU VRAM
                    VramWrites += 4;
                    addr &= 0x1FFFF;
                    if (addr < 0x18000)
                    {
                        Gba.Lcd.Vram[addr + 0] = (byte)(val >> 0);
                        Gba.Lcd.Vram[addr + 1] = (byte)(val >> 8);
                        Gba.Lcd.Vram[addr + 2] = (byte)(val >> 16);
                        Gba.Lcd.Vram[addr + 3] = (byte)(val >> 24);
                    }
                    return;
                case 0x7: // PPU OAM
                    OamWrites += 4;
                    addr &= 0x3FF;
                    Gba.Lcd.Oam[addr + 0] = (byte)(val >> 0);
                    Gba.Lcd.Oam[addr + 1] = (byte)(val >> 8);
                    Gba.Lcd.Oam[addr + 2] = (byte)(val >> 16);
                    Gba.Lcd.Oam[addr + 3] = (byte)(val >> 24);
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
    }
}
