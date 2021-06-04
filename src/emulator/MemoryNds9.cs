using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;

namespace OptimeGBA
{
    public sealed unsafe class MemoryNds9 : Memory
    {
        Nds9 Nds9;

        public MemoryNds9(Nds9 nds9, ProviderNds provider) : base(nds9)
        {
            Nds9 = nds9;

            SaveProvider = new NullSaveProvider();

            for (uint i = 0; i < Arm9BiosSize && i < provider.Bios9.Length; i++)
            {
                Arm9Bios[i] = provider.Bios9[i];
            }
        }

        public const int Arm9BiosSize = 4096;
        public byte[] Arm9Bios = MemoryUtil.AllocateManagedArray(Arm9BiosSize);
        public const int ItcmSize = 32768;
        public byte[] Itcm = MemoryUtil.AllocateManagedArray(ItcmSize);
        public const int DtcmSize = 16384;
        public byte[] Dtcm = MemoryUtil.AllocateManagedArray(DtcmSize);

        public uint DtcmBase = 0;
        public uint ItcmVirtualSize = 0;
        public uint DtcmVirtualSize = 0;
        public bool ItcmLoadMode = false;
        public bool DtcmLoadMode = false;

        public override void InitPageTable(byte[][] table, uint[] maskTable, bool write)
        {
            // 12 bits shaved off already, shave off another 12 to get 24
            for (uint i = 0; i < 1048576; i++)
            {
                table[i] = null; // Clear everything out first, since on ARM9 things can move around

                uint addr = (uint)(i << 12);
                switch (i >> 12)
                {
                    case 0x2: // Main Memory
                        table[i] = Nds9.Nds.MainRam;
                        maskTable[i] = 0x003FFFFF;
                        break;
                    case 0xFF: // BIOS
                        if (!write)
                        {
                            table[i] = Arm9Bios;
                        }
                        maskTable[i] = 0x00000FFF;
                        break;
                }

                if (addr >= DtcmBase && addr < DtcmBase + DtcmVirtualSize)
                {

                    if (write || !DtcmLoadMode)
                    {
                        // Console.WriteLine("DTCM page set at " + Util.Hex(addr, 8));
                        table[i] = Dtcm;
                    }
                    maskTable[i] = 0x00003FFF;
                }

                // ITCM is immovable
                // ITCM has higher priority so write pages in after DTCM
                if (addr < ItcmVirtualSize)
                {
                    if (write || !ItcmLoadMode)
                    {
                        table[i] = Itcm;
                    }
                    maskTable[i] = 0x00007FFF;
                }
            }
        }

        public void UpdateTcmSettings()
        {
            // Console.WriteLine("Data TCM Settings: " + Util.Hex(Nds9.Nds.Cp15.DataTcmSettings, 8));
            ItcmVirtualSize = 512U << (int)((Nds9.Nds.Cp15.InstTcmSettings >> 1) & 0x1F);
            DtcmVirtualSize = 512U << (int)((Nds9.Nds.Cp15.DataTcmSettings >> 1) & 0x1F);

            DtcmBase = (uint)(Nds9.Nds.Cp15.DataTcmSettings & 0xFFFFF000);

            ItcmLoadMode = BitTest(Nds9.Nds.Cp15.ControlRegister, 19);
            DtcmLoadMode = BitTest(Nds9.Nds.Cp15.ControlRegister, 17);

            // Console.WriteLine("DTCM set to: " + Util.Hex(DtcmBase, 8) + " - " + Util.Hex(DtcmBase + DtcmVirtualSize - 1, 8));

            InitPageTables();
        }

        public (byte[] array, uint offset) GetSharedRamParams(uint addr)
        {
            switch (Nds9.Nds.MemoryControl.SharedRamControl)
            {
                case 0:
                default:
                    addr &= 0x7FFF; // All 32k of Shared RAM
                    return (Nds9.Nds.SharedRam, addr);
                case 1:
                    addr &= 0x3FFF; // 2nd half of Shared RAM
                    addr += 0x4000;
                    return (Nds9.Nds.SharedRam, addr);
                case 2:
                    addr &= 0x3FFF; // 1st half of Shared RAM
                    return (Nds9.Nds.SharedRam, addr);
                case 3:
                    return (EmptyPage, 0); // Unmapped
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
                case 0x5: // PPU Palettes
                    return Nds9.Nds.Ppu.ReadPalettes8(addr);
                case 0x6: // VRAM
                    return Nds9.Nds.Ppu.ReadVram8(addr);
                case 0x7: // PPU OAM
                    return Nds9.Nds.Ppu.ReadOam8(addr);
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
                case 0x5: // PPU Palettes
                    return Nds9.Nds.Ppu.ReadPalettes16(addr);
                case 0x6: // VRAM
                    return (ushort)(
                        (Nds9.Nds.Ppu.ReadVram8(addr + 0) << 0) |
                        (Nds9.Nds.Ppu.ReadVram8(addr + 1) << 8)
                    );
                case 0x7: // PPU OAM
                    return Nds9.Nds.Ppu.ReadOam16(addr);
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
                    byte f0 = Read8Unregistered(addr + 0);
                    byte f1 = Read8Unregistered(addr + 1);
                    byte f2 = Read8Unregistered(addr + 2);
                    byte f3 = Read8Unregistered(addr + 3);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
                case 0x5: // PPU Palettes
                    return Nds9.Nds.Ppu.ReadPalettes32(addr);
                case 0x6: // VRAM
                    return (uint)(
                        (Nds9.Nds.Ppu.ReadVram8(addr + 0) << 0) |
                        (Nds9.Nds.Ppu.ReadVram8(addr + 1) << 8) |
                        (Nds9.Nds.Ppu.ReadVram8(addr + 2) << 16) |
                        (Nds9.Nds.Ppu.ReadVram8(addr + 3) << 24)
                    );
                case 0x7: // PPU OAM
                    return Nds9.Nds.Ppu.ReadOam32(addr);
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
                case 0x5: // PPU Palettes
                    Nds9.Nds.Ppu.WritePalettes16(addr, val);
                    break;
                case 0x6: // VRAM
                    Nds9.Nds.Ppu.WriteVram8(addr, val);
                    break;
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
                case 0x5: // PPU Palettes
                    Nds9.Nds.Ppu.WritePalettes16(addr, val);
                    break;
                case 0x6: // VRAM
                    Nds9.Nds.Ppu.WriteVram8(addr + 0, (byte)(val >> 0));
                    Nds9.Nds.Ppu.WriteVram8(addr + 1, (byte)(val >> 8));
                    break;
                case 0x7: // PPU OAM
                    Nds9.Nds.Ppu.WriteOam16(addr, val);
                    break;
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
                case 0x5: // PPU Palettes
                    Nds9.Nds.Ppu.WritePalettes32(addr, val);
                    break;
                case 0x6: // VRAM
                    Nds9.Nds.Ppu.WriteVram8(addr + 0, (byte)(val >> 0));
                    Nds9.Nds.Ppu.WriteVram8(addr + 1, (byte)(val >> 8));
                    Nds9.Nds.Ppu.WriteVram8(addr + 2, (byte)(val >> 16));
                    Nds9.Nds.Ppu.WriteVram8(addr + 3, (byte)(val >> 24));
                    break;
                case 0x7: // PPU OAM
                    Nds9.Nds.Ppu.WriteOam32(addr, val);
                    break;
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

            if (addr >= 0x4000000 && addr <= 0x400006C) // PPU
            {
                return Nds9.Nds.Ppu.ReadHwio8(addr);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000EF) // DMA
            {
                return Nds9.Dma.ReadHwio8(addr);
            }
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                return Nds9.Timers.ReadHwio8(addr);
            }
            else if (addr >= 0x4000130 && addr <= 0x4000132) // Keypad
            {
                return Nds9.Nds.Keypad.ReadHwio8(addr);
            }
            else if (addr >= 0x4000180 && addr <= 0x400018B) // FIFO
            {
                return Nds9.Nds.Ipcs[1].ReadHwio8(addr);
            }
            else if (addr >= 0x40001A0 && addr <= 0x40001AF) // Cartridge control
            {
                return Nds9.Nds.Cartridge.ReadHwio8(false, addr);
            }
            else if (addr >= 0x4000204 && addr <= 0x4000205) // External Memory Control
            {
                return Nds9.Nds.MemoryControl.ReadHwio8Nds9(addr);
            }
            else if (addr >= 0x4000208 && addr <= 0x4000217) // Interrupts
            {
                return Nds9.HwControl.ReadHwio8(addr);
            }
            else if (addr >= 0x4000240 && addr <= 0x4000249) // Memory Control
            {
                return Nds9.Nds.MemoryControl.ReadHwio8Nds9(addr);
            }
            else if (addr >= 0x4000280 && addr <= 0x40002BF) // ARM9 Math
            {
                return Nds9.Math.ReadHwio8(addr);
            }
            else if (addr >= 0x4001000 && addr <= 0x400106C) // PPU B
            {
                return Nds9.Nds.Ppu.ReadHwio8(addr);
            }
            else if (addr >= 0x4100000 && addr <= 0x4100003) // IPCFIFORECV
            {
                return Nds9.Nds.Ipcs[1].ReadHwio8(addr);
            }
            else if (addr >= 0x4100010 && addr <= 0x4100013) // Cartridge data read
            {
                return Nds9.Nds.Cartridge.ReadHwio8(false, addr);
            }

            switch (addr)
            {
                case 0x4000300:
                    // Console.WriteLine("NDS9 POSTFLG read");
                    return Nds9.POSTFLG;
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

            if (addr >= 0x4000000 && addr <= 0x400006C) // PPU
            {
                Nds9.Nds.Ppu.WriteHwio8(addr, val);
            }
            else if (addr >= 0x40000B0 && addr <= 0x40000EF) // DMA
            {
                Nds9.Dma.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000100 && addr <= 0x400010F) // Timer
            {
                Nds9.Timers.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000180 && addr <= 0x400018B) // FIFO
            {
                Nds9.Nds.Ipcs[1].WriteHwio8(addr, val);
            }
            else if (addr >= 0x40001A0 && addr <= 0x40001AF) // Cartridge control
            {
                Nds9.Nds.Cartridge.WriteHwio8(false, addr, val);
            }
            else if (addr >= 0x4000204 && addr <= 0x4000205) // External Memory Control
            {
                Nds9.Nds.MemoryControl.WriteHwio8Nds9(addr, val);
            }
            else if (addr >= 0x4000208 && addr <= 0x4000217) // Interrupts
            {
                Nds9.HwControl.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4000240 && addr <= 0x4000249) // Memory Control
            {
                Nds9.Nds.MemoryControl.WriteHwio8Nds9(addr, val);
            }
            else if (addr >= 0x4000280 && addr <= 0x40002BF) // ARM9 Math
            {
                Nds9.Math.WriteHwio8(addr, val);
            }
            else if (addr >= 0x4001000 && addr <= 0x400106C) // PPU B
            {
                Nds9.Nds.Ppu.WriteHwio8(addr, val);
            }

            switch (addr)
            {
                case 0x4000300:
                    Console.WriteLine("NDS9 POSTFLG write");
                    Nds9.POSTFLG = (byte)(val & 0b11);
                    break;
            }
        }
    }
}
