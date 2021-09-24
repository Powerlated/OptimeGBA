using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;

namespace OptimeGBA
{
    public unsafe abstract class Memory
    {
        public SaveProvider SaveProvider;
        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();
        public bool LogHwioAccesses = false;

        public abstract void InitPageTable(byte*[] pageTable, uint[] maskTable, bool write);
        public const int PageSize = 1024;

        public uint[] MemoryRegionMasks = new uint[1048576];

        public byte[] EmptyPage = new byte[PageSize];
        public byte*[] PageTableRead = new byte*[1048576];
        public byte*[] PageTableWrite = new byte*[1048576];

        public abstract byte Read8Unregistered(bool debug, uint addr);
        public abstract void Write8Unregistered(bool debug, uint addr, byte val);
        public abstract ushort Read16Unregistered(bool debug, uint addr);
        public abstract void Write16Unregistered(bool debug, uint addr, ushort val);
        public abstract uint Read32Unregistered(bool debug, uint addr);
        public abstract void Write32Unregistered(bool debug, uint addr, uint val);

        public Dictionary<byte[], GCHandle> Handles = new Dictionary<byte[], GCHandle>();

        public byte* TryPinByteArray(byte[] arr)
        {
            GCHandle handle;
            if (!Handles.TryGetValue(arr, out handle))
            {
                handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
                Handles[arr] = handle;
            }
            
            return (byte*)handle.AddrOfPinnedObject();
        }

        public void UnpinByteArray(byte[] arr)
        {
            Handles[arr].Free();
            if (!Handles.Remove(arr))
            {
                throw new ArgumentException("Tried to unpin already unpinned array.");
            }
        }

        public void InitPageTables()
        {
            InitPageTable(PageTableRead, MemoryRegionMasks, false);
            InitPageTable(PageTableWrite, MemoryRegionMasks, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint MaskAddress(uint addr)
        {
            return addr & MemoryRegionMasks[addr >> 12];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* ResolvePageRead(uint addr)
        {
            return PageTableRead[addr >> 12];
        }

        public byte* ResolvePageWrite(uint addr)
        {
            return PageTableWrite[addr >> 12];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte Read8(uint addr)
        {
            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetByte(page, MaskAddress(addr));
            }

            return Read8Unregistered(false, addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort Read16(uint addr)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Console.Error.WriteLine("Misaligned Read16! " + Util.HexN(addr, 8));
            }
#endif

            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUshort(page, MaskAddress(addr));
            }

            return Read16Unregistered(false, addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort ReadDebug16(uint addr)
        {
            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUshort(page, MaskAddress(addr));
            }

            return Read16Unregistered(true, addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint Read32(uint addr)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Console.Error.WriteLine("Misaligned Read32! " + Util.HexN(addr, 8));
            }
#endif

            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUint(page, MaskAddress(addr));
            }

            return Read32Unregistered(false, addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadDebug32(uint addr)
        {
            var page = ResolvePageRead(addr);
            if (page != null)
            {
                return GetUint(page, MaskAddress(addr));
            }

            return Read32Unregistered(true, addr);
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

            Write8Unregistered(false, addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write16(uint addr, ushort val)
        {
#if DEBUG
            if ((addr & 1) != 0)
            {
                Console.Error.WriteLine("Misaligned Write16! " + Util.HexN(addr, 8));
            }
#endif

            var page = ResolvePageWrite(addr);
            if (page != null)
            {
                SetUshort(page, MaskAddress(addr), val);
                return;
            }

            Write16Unregistered(false, addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write32(uint addr, uint val)
        {
#if DEBUG
            if ((addr & 3) != 0)
            {
                Console.Error.WriteLine("Misaligned Write32! " + Util.HexN(addr, 8));
            }
#endif

            var page = ResolvePageWrite(addr);
            if (page != null)
            {
                SetUint(page, MaskAddress(addr), val);
                return;
            }

            Write32Unregistered(false, addr, val);
        }
    }
}
