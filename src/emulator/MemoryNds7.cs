using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;
using static Util;

namespace OptimeGBA
{
    public sealed unsafe class MemoryNds7 : Memory
    {
        Nds Nds;

        public MemoryNds7(Nds nds, ProviderNds provider)
        {
            Nds = nds;

            SaveProvider = new NullSaveProvider();

            for (uint i = 0; i < Arm7BiosSize && i < provider.Bios7.Length; i++)
            {
                Arm7Bios[i] = provider.Bios7[i];
            }
        }

        public const int Arm7BiosSize = 16384;
        public const int Arm7WramSize = 65536;

        public byte[] Arm7Bios = new byte[Arm7BiosSize];
        public byte[] Arm7Wram = new byte[Arm7WramSize];

        public byte RCNT;

        public override void InitPageTable(byte*[] table, uint[] maskTable, bool write)
        {
            byte* arm7Bios = TryPinByteArray(Arm7Bios);
            byte* mainRam = TryPinByteArray(Nds.MainRam);
            byte* arm7Wram = TryPinByteArray(Arm7Wram);

            // 12 bits shaved off already, shave off another 12 to get 24
            for (uint i = 0; i < 1048576; i++)
            {
                uint addr = (uint)(i << 12);
                switch (i >> 12)
                {
                    case 0x0: // BIOS
                        if (!write)
                        {
                            table[i] = arm7Bios;
                        }
                        maskTable[i] = 0x00003FFF;
                        break;
                    case 0x2: // Main Memory
                        table[i] = mainRam;
                        maskTable[i] = 0x003FFFFF;
                        break;
                    case 0x3: // Shared RAM / ARM7 WRAM
                        if (addr >= 0x03800000)
                        {
                            table[i] = arm7Wram;
                            maskTable[i] = 0x0000FFFF;
                        }
                        break;
                }
            }
        }

        ~MemoryNds7()
        {
            Console.WriteLine("Cleaning up NDS7 memory...");
            UnpinByteArray(Arm7Bios);
            UnpinByteArray(Nds.MainRam);
            UnpinByteArray(Arm7Wram);
        }

        public (byte[] array, uint offset) GetSharedRamParams(uint addr)
        {
            switch (Nds.MemoryControl.SharedRamControl)
            {
                case 0:
                default:
                    addr &= 0xFFFF; // ARM7 WRAM
                    return (Arm7Wram, addr);
                case 1:
                    addr &= 0x3FFF; // 1st half of Shared RAM
                    return (Nds.SharedRam, addr);
                case 2:
                    addr &= 0x3FFF; // 2st half of Shared RAM
                    addr += 0x4000;
                    return (Nds.SharedRam, addr);
                case 3:
                    addr &= 0x7FFF; // All 32k of Shared RAM
                    return (Nds.SharedRam, addr);
            }
        }

        public override byte Read8Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetByte(array, offset);
                case 0x4: // I/O Registers
                    return ReadHwio8(debug, addr);
                case 0x6: // ARM7 VRAM
                    return Nds.Ppu.ReadVram8Arm7(addr);
            }

            return 0;
        }

        public override ushort Read16Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUshort(array, offset);
                case 0x4: // I/O Registers
                    byte f0 = ReadHwio8(debug, addr++);
                    byte f1 = ReadHwio8(debug, addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
                case 0x6: // VRAM
                    return (ushort)(
                        (Nds.Ppu.ReadVram8Arm7(addr + 0) << 0) |
                        (Nds.Ppu.ReadVram8Arm7(addr + 1) << 8)
                    );
            }

            return 0;
        }

        public override uint Read32Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUint(array, offset);
                case 0x4: // I/O Registers
                    byte f0 = ReadHwio8(debug, addr++);
                    byte f1 = ReadHwio8(debug, addr++);
                    byte f2 = ReadHwio8(debug, addr++);
                    byte f3 = ReadHwio8(debug, addr++);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
                case 0x6: // VRAM
                    return (uint)(
                        (Nds.Ppu.ReadVram8Arm7(addr + 0) << 0) |
                        (Nds.Ppu.ReadVram8Arm7(addr + 1) << 8) |
                        (Nds.Ppu.ReadVram8Arm7(addr + 2) << 16) |
                        (Nds.Ppu.ReadVram8Arm7(addr + 3) << 24)
                    );
            }

            return 0;
        }

        public override void Write8Unregistered(bool debug, uint addr, byte val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetByte(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr, val);
                    break;
                case 0x6: // ARM7 VRAM
                    Nds.Ppu.WriteVram8Arm7(addr, val);
                    break;
            }
        }

        public override void Write16Unregistered(bool debug, uint addr, ushort val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUshort(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    break;
                case 0x6: // ARM7 VRAM
                    Nds.Ppu.WriteVram8Arm7(addr + 0, (byte)(val >> 0));
                    Nds.Ppu.WriteVram8Arm7(addr + 1, (byte)(val >> 8));
                    break;
            }
        }

        public override void Write32Unregistered(bool debug, uint addr, uint val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUint(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    WriteHwio8(debug, addr++, (byte)(val >> 16));
                    WriteHwio8(debug, addr++, (byte)(val >> 24));
                    break;
                case 0x6: // ARM7 VRAM
                    Nds.Ppu.WriteVram8Arm7(addr + 0, (byte)(val >> 0));
                    Nds.Ppu.WriteVram8Arm7(addr + 1, (byte)(val >> 8));
                    Nds.Ppu.WriteVram8Arm7(addr + 2, (byte)(val >> 16));
                    Nds.Ppu.WriteVram8Arm7(addr + 3, (byte)(val >> 24));
                    break;
            }
        }


        public byte ReadHwio8(bool debug, uint addr)
        {
            if (LogHwioAccesses)
            {
                lock (HwioReadLog)
                {
                    if ((addr & ~1) != 0 && !debug)
                    {
                        uint count;
                        HwioReadLog.TryGetValue(addr, out count);
                        HwioReadLog[addr] = count + 1;
                    }
                }
            }

            // Special exceptions for cleanly defined blocks of MMIO
            if (addr >= 0x4000400 && addr < 0x4000500) // Audio channels
            {
                return Nds.Audio.ReadHwio8Channels(addr);
            }

            switch (addr)
            {
                case 0x4000004: case 0x4000005: // DISPSTAT
                case 0x4000006: case 0x4000007: // VCOUNT
                    return Nds.Ppu.ReadHwio8Arm7(addr);

                case 0x40000B0: case 0x40000B1: case 0x40000B2: case 0x40000B3: // DMA0SAD
                case 0x40000B4: case 0x40000B5: case 0x40000B6: case 0x40000B7: // DMA0DAD
                case 0x40000B8: case 0x40000B9: case 0x40000BA: case 0x40000BB: // DMA0CNT
                case 0x40000BC: case 0x40000BD: case 0x40000BE: case 0x40000BF: // DMA1SAD
                case 0x40000C0: case 0x40000C1: case 0x40000C2: case 0x40000C3: // DMA1DAD
                case 0x40000C4: case 0x40000C5: case 0x40000C6: case 0x40000C7: // DMA1CNT
                case 0x40000C8: case 0x40000C9: case 0x40000CA: case 0x40000CB: // DMA2SAD 
                case 0x40000CC: case 0x40000CD: case 0x40000CE: case 0x40000CF: // DMA2DAD
                case 0x40000D0: case 0x40000D1: case 0x40000D2: case 0x40000D3: // DMA2CNT
                case 0x40000D4: case 0x40000D5: case 0x40000D6: case 0x40000D7: // DMA3SAD
                case 0x40000D8: case 0x40000D9: case 0x40000DA: case 0x40000DB: // DMA3DAD
                case 0x40000DC: case 0x40000DD: case 0x40000DE: case 0x40000DF: // DMA3CNT
                case 0x40000E0: case 0x40000E1: case 0x40000E2: case 0x40000E3: // DMA0 Fill Data
                case 0x40000E4: case 0x40000E5: case 0x40000E6: case 0x40000E7: // DMA1 Fill Data
                case 0x40000E8: case 0x40000E9: case 0x40000EA: case 0x40000EB: // DMA2 Fill Data
                case 0x40000EC: case 0x40000ED: case 0x40000EE: case 0x40000EF: // DMA3 Fill Data
                    return Nds.Dma7.ReadHwio8(addr);

                case 0x4000100: case 0x4000101: case 0x4000102: case 0x4000103: // Timer 0
                case 0x4000104: case 0x4000105: case 0x4000106: case 0x4000107: // Timer 1
                case 0x4000108: case 0x4000109: case 0x400010A: case 0x400010B: // Timer 2
                case 0x400010C: case 0x400010D: case 0x400010E: case 0x400010F: // Timer 3
                    return Nds.Timers7.ReadHwio8(addr);

                case 0x4000180: case 0x4000181: case 0x4000182: case 0x4000183: // IPCSYNC
                case 0x4000184: case 0x4000185: case 0x4000186: case 0x4000187: // IPCFIFOCNT
                case 0x4000188: case 0x4000189: case 0x400018A: case 0x400018B: // IPCFIFOSEND
                case 0x4100000: case 0x4100001: case 0x4100002: case 0x4100003: // IPCFIFORECV
                    return Nds.Ipcs[1].ReadHwio8(addr);

                case 0x40001A0: case 0x40001A1: // AUXSPICNT
                case 0x40001A2: case 0x40001A3: // AUXSPIDATA
                case 0x40001A4: case 0x40001A5: case 0x40001A6: case 0x40001A7: // ROMCTRL
                case 0x4100010: case 0x4100011: case 0x4100012: case 0x4100013: // Slot 1 Data In
                    return Nds.Cartridge.ReadHwio8(true, addr);

                case 0x40001C0: case 0x40001C1: // SPICNT
                case 0x40001C2: case 0x40001C3: // SPIDATA
                    return Nds.Spi.ReadHwio8(addr);

                case 0x4000136: case 0x4000137: // EXTKEYIN
                    // Console.WriteLine(Hex(Nds7.Cpu.R[15], 8));
                    goto case 0x4000130;
                case 0x4000130: case 0x4000131: // KEYINPUT 
                    return Nds.Keypad.ReadHwio8(addr);

                case 0x4000204: case 0x4000205: // EXMEMSTAT
                    return Nds.MemoryControl.ReadHwio8Nds7(addr);

                case 0x4000208: case 0x4000209: case 0x400020A: case 0x400020B: // IME
                case 0x4000210: case 0x4000211: case 0x4000212: case 0x4000213: // IE
                case 0x4000214: case 0x4000215: case 0x4000216: case 0x4000217: // IF
                    return Nds.HwControl7.ReadHwio8(addr);

                case 0x4000134:
                    return 0x80;
                case 0x4000135: // Stubbed RCNT
                    return 0;

                case 0x4000138: case 0x4000139: // RTC
                    return Nds.Rtc.ReadHwio8(addr);

                case 0x4000240: case 0x4000241: // Memory Control Status
                    return Nds.MemoryControl.ReadHwio8Nds7(addr);

                case 0x4000500: case 0x4000501: // SOUNDCNT
                case 0x4000504: case 0x4000505: // SOUNDBIAS
                case 0x4000508: case 0x4000509: // SNDCAPCNT
                    return Nds.Audio.ReadHwio8(addr);

                case 0x4000300:
                    // Console.WriteLine("NDS7 POSTFLG read");
                    return Nds.HwControl7.Postflg;

                case 0x4000304: case 0x4000305: case 0x4000306: case 0x4000307: // POWCNT1
                    return Nds.ReadHwio8Arm7(addr);
            }

            // Console.WriteLine($"NDS7: Unmapped MMIO read addr:{Hex(addr, 8)}");

            return 0;
        }

        public void WriteHwio8(bool debug, uint addr, byte val)
        {
            if (LogHwioAccesses)
            {
                lock (HwioWriteLog)
                {
                    if ((addr & ~1) != 0 && !debug)
                    {
                        uint count;
                        HwioWriteLog.TryGetValue(addr, out count);
                        HwioWriteLog[addr] = count + 1;
                    }
                }
            }
            
            // Special exceptions for cleanly defined blocks of MMIO
            if (addr >= 0x4000400 && addr < 0x4000500) // Audio channels
            {
                Nds.Audio.WriteHwio8Channels(addr, val);
                return;
            }

            switch (addr)
            {
                case 0x4000004: case 0x4000005: // DISPSTAT
                case 0x4000006: case 0x4000007: // VCOUNT
                    Nds.Ppu.WriteHwio8Arm7(addr, val); return;

                case 0x40000B0: case 0x40000B1: case 0x40000B2: case 0x40000B3: // DMA0SAD
                case 0x40000B4: case 0x40000B5: case 0x40000B6: case 0x40000B7: // DMA0DAD
                case 0x40000B8: case 0x40000B9: case 0x40000BA: case 0x40000BB: // DMA0CNT
                case 0x40000BC: case 0x40000BD: case 0x40000BE: case 0x40000BF: // DMA1SAD
                case 0x40000C0: case 0x40000C1: case 0x40000C2: case 0x40000C3: // DMA1DAD
                case 0x40000C4: case 0x40000C5: case 0x40000C6: case 0x40000C7: // DMA1CNT
                case 0x40000C8: case 0x40000C9: case 0x40000CA: case 0x40000CB: // DMA2SAD 
                case 0x40000CC: case 0x40000CD: case 0x40000CE: case 0x40000CF: // DMA2DAD
                case 0x40000D0: case 0x40000D1: case 0x40000D2: case 0x40000D3: // DMA2CNT
                case 0x40000D4: case 0x40000D5: case 0x40000D6: case 0x40000D7: // DMA3SAD
                case 0x40000D8: case 0x40000D9: case 0x40000DA: case 0x40000DB: // DMA3DAD
                case 0x40000DC: case 0x40000DD: case 0x40000DE: case 0x40000DF: // DMA3CNT
                case 0x40000E0: case 0x40000E1: case 0x40000E2: case 0x40000E3: // DMA0 Fill Data
                case 0x40000E4: case 0x40000E5: case 0x40000E6: case 0x40000E7: // DMA1 Fill Data
                case 0x40000E8: case 0x40000E9: case 0x40000EA: case 0x40000EB: // DMA2 Fill Data
                case 0x40000EC: case 0x40000ED: case 0x40000EE: case 0x40000EF: // DMA3 Fill Data
                    Nds.Dma7.WriteHwio8(addr, val); return;

                case 0x4000100: case 0x4000101: case 0x4000102: case 0x4000103: // Timer 0
                case 0x4000104: case 0x4000105: case 0x4000106: case 0x4000107: // Timer 1
                case 0x4000108: case 0x4000109: case 0x400010A: case 0x400010B: // Timer 2
                case 0x400010C: case 0x400010D: case 0x400010E: case 0x400010F: // Timer 3
                    Nds.Timers7.WriteHwio8(addr, val); return;

                case 0x4000180: case 0x4000181: case 0x4000182: case 0x4000183: // IPCSYNC
                case 0x4000184: case 0x4000185: case 0x4000186: case 0x4000187: // IPCFIFOCNT
                case 0x4000188: case 0x4000189: case 0x400018A: case 0x400018B: // IPCFIFOSEND
                    Nds.Ipcs[1].WriteHwio8(addr, val); return;

                case 0x40001A0: case 0x40001A1: // AUXSPICNT
                case 0x40001A2: case 0x40001A3: // AUXSPIDATA
                case 0x40001A4: case 0x40001A5: case 0x40001A6: case 0x40001A7: // ROMCTRL
                case 0x40001A8: case 0x40001A9: case 0x40001AA: case 0x40001AB: // Slot 1 Command 0-3
                case 0x40001AC: case 0x40001AD: case 0x40001AE: case 0x40001AF: // Slot 1 Command 4-7
                    Nds.Cartridge.WriteHwio8(true, addr, val); return;

                case 0x40001B0: case 0x40001B1: case 0x40001B2: case 0x40001B3: // Slot 1 KEY2 encryption seed
                case 0x40001B4: case 0x40001B5: case 0x40001B6: case 0x40001B7:
                case 0x40001B8: case 0x40001B9: case 0x40001BA: case 0x40001BB:
                    return;

                case 0x40001C0: case 0x40001C1: // SPICNT
                case 0x40001C2: case 0x40001C3: // SPIDATA
                    Nds.Spi.WriteHwio8(addr, val); return;

                case 0x4000204: case 0x4000205: // EXMEMSTAT
                    Nds.MemoryControl.WriteHwio8Nds7(addr, val); return;

                case 0x4000208: case 0x4000209: case 0x400020A: case 0x400020B: // IME
                case 0x4000210: case 0x4000211: case 0x4000212: case 0x4000213: // IE
                case 0x4000214: case 0x4000215: case 0x4000216: case 0x4000217: // IF
                    Nds.HwControl7.WriteHwio8(addr, val); return;

                case 0x4000134: case 0x4000135: // Stubbed RCNT
                    return;

                case 0x4000138: case 0x4000139: // RTC
                    Nds.Rtc.WriteHwio8(addr, val); return;

                case 0x4000500: case 0x4000501: // SOUNDCNT
                case 0x4000504: case 0x4000505: // SOUNDBIAS
                case 0x4000508: case 0x4000509: // SNDCAPCNT
                    Nds.Audio.WriteHwio8(addr, val); return;

                case 0x4000300:
                    Console.WriteLine("NDS7 POSTFLG write");
                    Nds.HwControl7.Postflg = (byte)(val & 1);
                    return;

                case 0x4000301:
                    if ((val & 0b11000000) == 0b10000000)
                    {
                        Nds.Cpu7.Halted = true;
                    }
                    return;

                case 0x4000304: case 0x4000305: case 0x4000306: case 0x4000307: // POWCNT1
                    Nds.WriteHwio8Arm7(addr, val);
                    return;
            }

            // Console.WriteLine($"NDS7: Unmapped MMIO write addr:{Hex(addr, 8)} val:{Hex(val, 2)}");
        }
    }
}
