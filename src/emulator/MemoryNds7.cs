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

        public override void InitPageTable(byte[][] table, bool write)
        {
            MemoryRegionMasks[0x0] = 0x00003FFF; // BIOS
            MemoryRegionMasks[0x1] = 0x00000000; // 
            MemoryRegionMasks[0x2] = 0x003FFFFF; // Main Memory
            MemoryRegionMasks[0x3] = 0x0000FFFF; //  Shared WRAM / ARM7 WRAM
            MemoryRegionMasks[0x4] = 0x00000000; // 
            MemoryRegionMasks[0x5] = 0x00000000; // 
            MemoryRegionMasks[0x6] = 0x00000000; // 
            MemoryRegionMasks[0x7] = 0x00000000; // 
            MemoryRegionMasks[0x8] = 0x00000000; // 
            MemoryRegionMasks[0x9] = 0x00000000; // 
            MemoryRegionMasks[0xA] = 0x00000000; // 
            MemoryRegionMasks[0xB] = 0x00000000; // 
            MemoryRegionMasks[0xC] = 0x00000000; // 
            MemoryRegionMasks[0xD] = 0x00000000; // 
            MemoryRegionMasks[0xE] = 0x00000000; // 
            MemoryRegionMasks[0xF] = 0x00000000; // 

            // 10 bits shaved off already, shave off another 14 to get 24
            for (uint i = 0; i < 4194304; i++)
            {
                uint addr = (uint)(i << 10);
                switch (i >> 14)
                {
                    case 0x0: // BIOS
                        if (!write)
                        {
                            table[i] = Arm7Bios;
                        }
                        break;
                    case 0x2: // Main Memory
                        table[i] = Nds7.Nds.MainRam;
                        break;
                    case 0x3: // Shared RAM / ARM7 WRAM
                        if (addr >= 0x03800000)
                        {
                            table[i] = Arm7Wram;
                        }
                        break;

                }
            }
        }

        public (byte[] array, uint offset) GetSharedRamParams(uint addr)
        {
            switch (Nds7.Nds.SharedRamControl)
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
            }
        }


        public byte ReadHwio8(uint addr)
        {
            return 0;
        }

        public void WriteHwio8(uint addr, byte val)
        {

        }
    }
}
