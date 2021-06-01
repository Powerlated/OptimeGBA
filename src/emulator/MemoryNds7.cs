using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;

namespace OptimeGBA
{
    public sealed unsafe class MemoryNds7 : Memory
    {
        Nds7 Nds7;

        public MemoryNds7(Nds7 nds7, ProviderNds provider) : base(nds7)
        {
            Nds7 = nds7;

            SaveProvider = new NullSaveProvider();

            for (uint i = 0; i < Arm7BiosSize && i < provider.Bios7.Length; i++)
            {
                Arm7Bios[i] = provider.Bios7[i];
            }
        }

        public const int Arm7BiosSize = 16384;
        public const int Arm7WramSize = 65536;

        public byte[] Arm7Bios = MemoryUtil.AllocateManagedArray(Arm7BiosSize);
        public byte[] Arm7Wram = MemoryUtil.AllocateManagedArray(Arm7WramSize);

        public override void InitPageTable(byte[][] table, uint[] maskTable, bool write)
        {
            // 12 bits shaved off already, shave off another 12 to get 24
            for (uint i = 0; i < 1048576; i++)
            {
                uint addr = (uint)(i << 12);
                switch (i >> 12)
                {
                    case 0x0: // BIOS
                        if (!write)
                        {
                            table[i] = Arm7Bios;
                        }
                        maskTable[i] = 0x00003FFF;
                        break;
                    case 0x2: // Main Memory
                        table[i] = Nds7.Nds.MainRam;
                        maskTable[i] = 0x003FFFFF;
                        break;
                    case 0x3: // Shared RAM / ARM7 WRAM
                        if (addr >= 0x03800000)
                        {
                            table[i] = Arm7Wram;
                        }
                        maskTable[i] = 0x0000FFFF;
                        break;
                }
            }
        }

        public (byte[] array, uint offset) GetSharedRamParams(uint addr)
        {
            switch (Nds7.Nds.MemoryControl.SharedRamControl)
            {
                case 0:
                default:
                    addr &= 0xFFFF; // ARM7 WRAM
                    return (Arm7Wram, addr);
                case 1:
                    addr &= 0x3FFF; // 1st half of Shared RAM
                    return (Nds7.Nds.SharedRam, addr);
                case 2:
                    addr &= 0x3FFF; // 2st half of Shared RAM
                    addr += 0x4000;
                    return (Nds7.Nds.SharedRam, addr);
                case 3:
                    addr &= 0x7FFF; // All 32k of Shared RAM
                    return (Nds7.Nds.SharedRam, addr);
            }
        }

        public override byte Read8Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetByte(array, offset);
                case 0x4: // I/O Registers
                    return ReadHwio8(addr);
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }

            return 0;
        }

        public override ushort Read16Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUshort(array, offset);
                case 0x4: // I/O Registers
                    byte f0 = Read8Unregistered(addr++);
                    byte f1 = Read8Unregistered(addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }

            return 0;
        }

        public override uint Read32Unregistered(uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUint(array, offset);
                case 0x4: // I/O Registers
                    byte f0 = Read8Unregistered(addr++);
                    byte f1 = Read8Unregistered(addr++);
                    byte f2 = Read8Unregistered(addr++);
                    byte f3 = Read8Unregistered(addr++);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }

            return 0;
        }

        public override void Write8Unregistered(uint addr, byte val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetByte(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(addr, val);
                    break;
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }
        }

        public override void Write16Unregistered(uint addr, ushort val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUshort(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(addr++, (byte)(val >> 0));
                    WriteHwio8(addr++, (byte)(val >> 8));
                    break;
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }
        }

        public override void Write32Unregistered(uint addr, uint val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUint(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(addr++, (byte)(val >> 0));
                    WriteHwio8(addr++, (byte)(val >> 8));
                    WriteHwio8(addr++, (byte)(val >> 16));
                    WriteHwio8(addr++, (byte)(val >> 24));
                    break;
                case 0x6: // ARM7 VRAM
                    throw new NotImplementedException("ARM7 VRAM");
            }
        }


        public byte ReadHwio8(uint addr)
        {
            if (LogHwioAccesses && (addr & ~1) != 0)
            {
                uint count;
                HwioReadLog.TryGetValue(addr, out count);
                HwioReadLog[addr] = count + 1;
            }

            if (addr >= 0x4000000 && addr <= 0x4000007) // PPU
            {
                return Nds7.Nds.Ppu.ReadHwio8(addr);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000EF) // DMA
            {
                return Nds7.Dma.ReadHwio8(addr);
            }
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                return Nds7.Timers.ReadHwio8(addr);
            }
            else if (addr >= 0x4000130 && addr <= 0x4000132) // Keypad
            {
                return Nds7.Nds.Keypad.ReadHwio8(addr);
            }
            else if (addr >= 0x4000180 && addr <= 0x400018B) // FIFO
            {
                return Nds7.Nds.Ipcs[0].ReadHwio8(addr);
            }
            else if (addr >= 0x40001A0 && addr <= 0x40001AF) // Cartridge control
            {
                return Nds7.Nds.Cartridge.ReadHwio8(addr);
            }
            else if (addr >= 0x40001C0 && addr <= 0x40001C3) // SPI
            {
                return Nds7.Spi.ReadHwio8(addr);
            }
            else if (addr >= 0x4000208 && addr <= 0x4000217) // Interrupts
            {
                return Nds7.HwControl.ReadHwio8(addr);
            }
            else if (addr >= 0x4000240 && addr <= 0x4000241) // Memory Control
            {
                return Nds7.Nds.MemoryControl.ReadHwio8Nds7(addr);
            }
            else if (addr >= 0x4000400 && addr <= 0x400051D) // Sound
            {
                return Nds7.Nds.Audio.ReadHwio8(addr);
            }
            else if (addr >= 0x4100000 && addr <= 0x4100003) // IPCFIFORECV
            {
                return Nds7.Nds.Ipcs[0].ReadHwio8(addr);
            }
            else if (addr >= 0x4100010 && addr <= 0x4100013) // Cartridge data read
            {
                return Nds7.Nds.Cartridge.ReadHwio8(addr);
            }

            switch (addr)
            {
                case 0x4000138:
                    return Nds7.Nds.Rtc.ReadHwio8(addr);
                case 0x4000300:
                    // Console.WriteLine("NDS7 POSTFLG read");
                    return Nds7.POSTFLG;
            }

            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            if (LogHwioAccesses && (addr & ~1) != 0)
            {
                uint count;
                HwioWriteLog.TryGetValue(addr, out count);
                HwioWriteLog[addr] = count + 1;
            }

            if (addr >= 0x4000000 && addr <= 0x4000007) // PPU
            {
                Nds7.Nds.Ppu.WriteHwio8(addr, val);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000EF) // DMA
            {
                Nds7.Dma.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                Nds7.Timers.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000180 && addr <= 0x400018B) // FIFO
            {
                Nds7.Nds.Ipcs[0].WriteHwio8(addr, val);
            }
            else if (addr >= 0x40001A0 && addr <= 0x40001AF) // Cartridge control
            {
                Nds7.Nds.Cartridge.WriteHwio8(addr, val);
            }
            else if (addr >= 0x40001C0 && addr <= 0x40001C3) // SPI
            {
                Nds7.Spi.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000208 && addr <= 0x4000217) // Interrupts
            {
                Nds7.HwControl.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000400 && addr <= 0x400051D) // Sound
            {
                Nds7.Nds.Audio.WriteHwio8(addr, val);
            }

            switch (addr)
            {
                case 0x4000138:
                    Nds7.Nds.Rtc.WriteHwio8(addr, val);
                    break;
                case 0x4000300:
                    Console.WriteLine("NDS7 POSTFLG write");
                    Nds7.POSTFLG = (byte)(val & 1);
                    break;
                case 0x4000301:
                    if (val == 0xF0)
                    {
                        Nds7.Cpu.Halted = true;
                    }
                    break;
            }
        }
    }
}
