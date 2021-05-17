using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.Memory;

namespace OptimeGBA
{
    public sealed unsafe class MemoryNds7
    {
        Nds7 Nds7;

        public MemoryNds7(Nds7 nds7)
        {


            
        }

        public uint EepromThreshold = 0x2000000;

        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();
        public bool LogHwioAccesses = false;

        public SaveProvider SaveProvider = new NullSaveProvider();

        public const int BiosSize = 16384;
        public const int MaxRomSize = 67108864;
        public const int EwramSize = 262144;
        public const int IwramSize = 32768;
        public const int PageSize = 1024;
        public uint RomSize;

#if UNSAFE
        public byte* Bios = Memory.AllocateUnmanagedArray(BiosSize);
        public byte* Rom = Memory.AllocateUnmanagedArray(MaxRomSize);
        public byte* Ewram = Memory.AllocateUnmanagedArray(EwramSize);
        public byte* Iwram = Memory.AllocateUnmanagedArray(IwramSize);

        public byte* EmptyPage = Memory.AllocateUnmanagedArray(PageSize);
        public byte*[] PageTableRead = new byte*[4194304];
        public byte*[] PageTableWrite = new byte*[4194304];

        ~MemoryNds7()
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

        public byte[] EmptyPage = Memory.AllocateManagedArray(PageSize);
        public byte[][] PageTableRead = new byte[4194304][];
        public byte[][] PageTableWrite = new byte[4194304][];
#endif

        public void InitPageTables()
        {
            InitPageTable(PageTableRead, false);
            InitPageTable(PageTableWrite, true);
        }

#if UNSAFE
        public void InitPageTable(byte*[] table, bool write)
#else
        public void InitPageTable(byte[][] table, bool write)
#endif
        {
           
            
        }

        public uint[] MemoryRegionMasks = {
            0x00003FFF, // 0x0 - BIOS
            0x00000000, // 0x1 - Unused
            0x0003FFFF, // 0x2 - EWRAM
            0x00007FFF, // 0x3 - IWRAM
            0x00000000, // 0x4 - I/O
            0x000003FF, // 0x5 - Palettes
            0x0001FFFF, // 0x6 - VRAM
            0x000003FF, // 0x7 - OAM
            0x01FFFFFF, // 0x8 - ROM
            0x01FFFFFF, // 0x9 - ROM
            0x01FFFFFF, // 0xA - ROM
            0x01FFFFFF, // 0xB - ROM
            0x01FFFFFF, // 0xC - ROM
            0x01FFFFFF, // 0xD - ROM
            0x00000000, // 0xE - SRAM / FLASH
            0x00000000, // 0xF - SRAM / FLASH
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint MaskAddress(uint addr)
        {
            return addr & MemoryRegionMasks[addr >> 24];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#if UNSAFE
        public byte* ResolvePageRead(uint addr)
#else
        public byte[] ResolvePageRead(uint addr)
#endif
        {
            return PageTableRead[addr >> 10];
        }

#if UNSAFE
        public byte* ResolvePageWrite(uint addr)
#else
        public byte[] ResolvePageWrite(uint addr)
#endif
        {
            return PageTableWrite[addr >> 10];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetByte(page, MaskAddress(addr));
            }

            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        uint count;
                        HwioReadLog.TryGetValue(addr, out count);
                        HwioReadLog[addr] = count + 1;
                    }

                    return ReadHwio8(addr);
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    return SaveProvider.Read8(addr);
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16(uint addr)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Nds7.Cpu.Error("Misaligned Read16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Nds7.Cpu.R[15], 8));
            }
#endif

            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUshort(page, MaskAddress(addr));
            }

            byte f0 = ReadHwio8(addr++);
            byte f1 = ReadHwio8(addr++);

            ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

            return u16;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32(uint addr)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Nds7.Cpu.Error("Misaligned Read32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Nds7.Cpu.R[15], 8));
            }
#endif

            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUint(page, MaskAddress(addr));
            }

            byte f0 = ReadHwio8(addr++);
            byte f1 = ReadHwio8(addr++);
            byte f2 = ReadHwio8(addr++);
            byte f3 = ReadHwio8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write8(uint addr, byte val)
        {
            var page = ResolvePageWrite(addr);
            if (page != null)
            {
                SetByte(page, MaskAddress(addr), val);
                return;
            }

            switch (addr >> 24)
            {
                case 0x4: // I/O Registers
                    // addr &= 0x400FFFF;

                    if (LogHwioAccesses && (addr & ~1) != 0)
                    {
                        uint count;
                        HwioWriteLog.TryGetValue(addr, out count);
                        HwioWriteLog[addr] = count + 1;
                    }

                    WriteHwio8(addr, val);
                    break;
                case 0xE: // Game Pak SRAM/Flash
                case 0xF: // Game Pak SRAM/Flash
                    SaveProvider.Write8(addr, val);
                    return;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Nds7.Cpu.Error("Misaligned Write16! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Nds7.Cpu.R[15], 8));
            }
#endif

            var page = ResolvePageWrite(addr);
            if (page != null)
            {
                SetUshort(page, MaskAddress(addr), val);
                return;
            }

            switch (addr >> 24)
            {

            }

            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);

            WriteHwio8(addr++, f0);
            WriteHwio8(addr++, f1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write32(uint addr, uint val)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Nds7.Cpu.Error("Misaligned Write32! " + Util.HexN(addr, 8) + " PC:" + Util.HexN(Nds7.Cpu.R[15], 8));
            }
#endif

            var page = ResolvePageWrite(addr);
            if (page != null)
            {
                SetUint(page, MaskAddress(addr), val);
                return;
            }

            switch (addr >> 24)
            {

            }

            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);
            byte f2 = (byte)(val >> 16);
            byte f3 = (byte)(val >> 24);

            WriteHwio8(addr++, f0);
            WriteHwio8(addr++, f1);
            WriteHwio8(addr++, f2);
            WriteHwio8(addr++, f3);
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
