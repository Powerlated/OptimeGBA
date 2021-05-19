using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;

namespace OptimeGBA
{
    public abstract class Memory
    {
        public SaveProvider SaveProvider;
        public SortedDictionary<uint, uint> HwioWriteLog = new SortedDictionary<uint, uint>();
        public SortedDictionary<uint, uint> HwioReadLog = new SortedDictionary<uint, uint>();
        public bool LogHwioAccesses = false;

        public abstract void InitPageTables();
        public const int PageSize = 1024;

        public abstract byte Read8(uint addr);
        public abstract ushort Read16(uint addr);
        public abstract uint Read32(uint addr);

        public abstract void Write8(uint addr, byte val);
        public abstract void Write16(uint addr, ushort val);
        public abstract void Write32(uint addr, uint val);
    }
}
