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


        public Memory(GBA gba)
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
        }

        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();
        public bool LogHwioAccesses = false;

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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                    return ReadSave(addr);
                    break;
            }

            return 0;
        }

        public ushort Read16(uint addr)
        {
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Read16! " + Util.HexN(addr, 8));
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                    addr &= 0x1FFFFFF;
                    return (ushort)(
                        (Rom[addr + 0] << 0) |
                        (Rom[addr + 1] << 8)
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
                Gba.Arm7.Error("Misaligned Read32! " + Util.HexN(addr, 8));
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                    addr &= 0x1FFFFFF;
                    return (uint)(
                            (Rom[addr + 0] << 0) |
                            (Rom[addr + 1] << 8) |
                            (Rom[addr + 2] << 16) |
                            (Rom[addr + 3] << 24)
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                    addr &= 0x1FFFFFF;
                    return Rom[addr];
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return ReadSave(addr);
                    break;
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                    break;
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    WriteSave(addr, val);
                    break;
            }
        }

        public void Write16(uint addr, ushort val)
        {
            if ((addr & 1) != 0)
            {
                Gba.Arm7.Error("Misaligned Write16! " + Util.HexN(addr, 8));
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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
                Gba.Arm7.Error("Misaligned Write32! " + Util.HexN(addr, 8));
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
                    if (addr < 0x6018000)
                    {
                        addr -= 0x6000000;
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


        public bool EEPROMActive = false;
        public bool EEPROMReadMode = false;
        public bool EEPROMReadied = false;
        public bool EEPROMReceivingAddr = false;
        public bool EEPROMTerminate = false;
        public uint EEPROMBitsRemaining = 0;
        public uint EEPROMAddrBitsRemaining = 0;

        // 1 entry per bit because I'm lazy
        public byte[] EEPROM = new byte[0x10000];
        public uint EEPROMAddr = 0;
        public byte ReadBitEEPROM()
        {
            return EEPROM[EEPROMAddr];
        }
        public void WriteBitEEPROM(bool bit)
        {
            EEPROM[EEPROMAddr] = Convert.ToByte(bit);
        }
        public bool UseEEPROM = false;

        public byte ReadSave(uint addr)
        {
            // Console.WriteLine("Save Read: " + Util.Hex(addr, 8));
            if (UseEEPROM)
            {
                if (EEPROMActive)
                {
                    if (EEPROMReadMode)
                    {
                        if (EEPROMBitsRemaining > 64)
                        {
                            return 0;
                        }
                        else
                        {
                            Console.WriteLine("EEPROM Read");
                            byte bit = ReadBitEEPROM();
                            EEPROMAddr++;
                            return bit;
                        }
                    }
                }
            }
            else
            {
                switch (addr)
                {
                    // Stub out Flash
                    case 0x0E000000: return 0x62;
                    case 0x0E000001: return 0x13;
                        // case 0x0E000000: return 0xC2;
                        // case 0x0E000001: return 0x09;
                }
            }
            return 0xFF;
        }

        public void WriteSave(uint addr, uint val)
        {
            // Console.WriteLine("Save Write: " + Util.Hex(addr, 8));
            if (UseEEPROM)
            {
                bool bit = BitTest(val, 0);
                if (EEPROMActive)
                {
                    if (EEPROMBitsRemaining > 0)
                    {
                        if (!EEPROMReadMode)
                        {
                            Console.WriteLine("EEPROM Write!");
                            WriteBitEEPROM(bit);
                            EEPROMAddr++;
                        }
                    }
                    else
                    {
                        if (bit == false)
                        {
                            EEPROMActive = false;
                        }
                    }
                }
                else if (EEPROMReceivingAddr)
                {
                    Console.WriteLine($"EEPROM Addr Write! {val & 1}");

                    EEPROMAddr <<= 1;
                    EEPROMAddr |= val & 1;
                    EEPROMAddrBitsRemaining--;
                    if (EEPROMAddrBitsRemaining == 0)
                    {
                        Console.WriteLine($"EEPROM Addr Set!");
                        EEPROMActive = true;
                    }
                }
                else
                {
                    if (EEPROMReadied)
                    {
                        Console.WriteLine("EEPROM Ready!");

                        EEPROMReadMode = bit;
                        EEPROMReceivingAddr = true;
                        EEPROMAddrBitsRemaining = 6;
                        EEPROMReadied = false;
                        EEPROMAddr = 0;

                        if (EEPROMReadMode)
                        {
                            Console.WriteLine("EEPROM Read Mode!");
                            EEPROMBitsRemaining = 68;
                        }
                        else
                        {
                            Console.WriteLine("EEPROM Write Mode!");
                            EEPROMBitsRemaining = 64;
                        }
                    }
                    else
                    {
                        if (bit) EEPROMReadied = true;
                    }
                }
            }
            else
            {

            }
        }
    }
}
