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

        public MemoryNds7(Nds7 nds7)
        {


            
        }

        public uint RomSize;

        public const int Arm7WramSize = 65536; 

#if UNSAFE
        public byte* Bios = MemoryUtil.AllocateUnmanagedArray(Arm7WramSize);

        public byte* EmptyPage = MemoryUtil.AllocateUnmanagedArray(PageSize);
        public byte*[] PageTableRead = new byte*[4194304];
        public byte*[] PageTableWrite = new byte*[4194304];

        ~MemoryNds7()
        {
            MemoryUtil.FreeUnmanagedArray(Bios);
        }
#else
        public byte[] Bios = MemoryUtil.AllocateManagedArray(Arm7WramSize);

        public byte[] EmptyPage = MemoryUtil.AllocateManagedArray(PageSize);
        public byte[][] PageTableRead = new byte[4194304][];
        public byte[][] PageTableWrite = new byte[4194304][];
#endif

        public override void InitPageTables()
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
            0x00000000, // 0x1 - 
            0x00000000, // 0x2 - 
            0x00000000, // 0x3 - 
            0x00000000, // 0x4 - 
            0x00000000, // 0x5 - 
            0x00000000, // 0x6 - 
            0x00000000, // 0x7 - 
            0x00000000, // 0x8 - 
            0x00000000, // 0x9 - 
            0x00000000, // 0xA - 
            0x00000000, // 0xB - 
            0x00000000, // 0xC - 
            0x00000000, // 0xD - 
            0x00000000, // 0xE - 
            0x00000000, // 0xF - 
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
        public override byte Read8(uint addr)
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
        public override ushort Read16(uint addr)
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
        public override uint Read32(uint addr)
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
        public override void Write8(uint addr, byte val)
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
        public override void Write16(uint addr, ushort val)
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
        public override void Write32(uint addr, uint val)
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
