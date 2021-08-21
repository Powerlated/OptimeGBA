using System.Runtime.CompilerServices;

namespace OptimeGBA
{
    sealed class Bits
    {
        public const uint BIT_0 = (1 << 0);
        public const uint BIT_1 = (1 << 1);
        public const uint BIT_2 = (1 << 2);
        public const uint BIT_3 = (1 << 3);
        public const uint BIT_4 = (1 << 4);
        public const uint BIT_5 = (1 << 5);
        public const uint BIT_6 = (1 << 6);
        public const uint BIT_7 = (1 << 7);
        public const uint BIT_8 = (1 << 8);
        public const uint BIT_9 = (1 << 9);
        public const uint BIT_10 = (1 << 10);
        public const uint BIT_11 = (1 << 11);
        public const uint BIT_12 = (1 << 12);
        public const uint BIT_13 = (1 << 13);
        public const uint BIT_14 = (1 << 14);
        public const uint BIT_15 = (1 << 15);
        public const uint BIT_16 = (1 << 16);
        public const uint BIT_17 = (1 << 17);
        public const uint BIT_18 = (1 << 18);
        public const uint BIT_19 = (1 << 19);
        public const uint BIT_20 = (1 << 20);
        public const uint BIT_21 = (1 << 21);
        public const uint BIT_22 = (1 << 22);
        public const uint BIT_23 = (1 << 23);
        public const uint BIT_24 = (1 << 24);
        public const uint BIT_25 = (1 << 25);
        public const uint BIT_26 = (1 << 26);
        public const uint BIT_27 = (1 << 27);
        public const uint BIT_28 = (1 << 28);
        public const uint BIT_29 = (1 << 29);
        public const uint BIT_30 = (1 << 30);


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BitTest(uint i, byte bit)
        {
            return (i & (1 << bit)) != 0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BitTest(ulong i, byte bit)
        {
            return (i & (1u << bit)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BitTest(long i, byte bit)
        {
            return (i & (1u << bit)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint BitSet(uint i, byte bit)
        {
            return (uint)(i | (uint)(1 << bit));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte BitSet(byte i, byte bit)
        {
            return (byte)(i | (byte)(1 << bit));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte BitClear(byte i, byte bit)
        {
            return (byte)(i & ~(byte)(1 << bit));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint BitRange(uint i, byte start, byte end)
        {
            return (i >> start) & (0xFFFFFFFF >> (31 - (end - start)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte BitReverse8(byte i)
        {
            return (byte)((i * 0x0202020202U & 0x010884422010U) % 1023);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LogicalShiftLeft32(uint n, byte bits)
        {
            return n << bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint LogicalShiftRight32(uint n, byte bits)
        {
            return n >> bits;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ArithmeticShiftRight32(uint n, byte bits)
        {
            return (uint)((int)n >> bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint RotateRight32(uint n, byte bits)
        {
            return (n >> bits) | (n << -bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong RotateRight64(ulong n, byte bits)
        {
            return (n >> bits) | (n << -bits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByteIn(long n, int pos)
        {
            return (byte)(n >> (pos * 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByteIn(ulong n, int pos)
        {
            return (byte)(n >> (pos * 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByteIn(long n, uint pos)
        {
            return (byte)(n >> (int)(pos * 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByteIn(ulong n, uint pos)
        {
            return (byte)(n >> (int)(pos * 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint SetByteIn(uint n, byte val, uint pos)
        {
            uint mask = ~(0xFFU << (int)(pos * 8));
            uint or = (uint)(val << (int)(pos * 8));
            return (n & mask) | or;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong SetByteIn(ulong n, byte val, uint pos)
        {
            ulong mask = ~(0xFFUL << (int)(pos * 8));
            ulong or = (ulong)((ulong)val << (int)(pos * 8));
            return (n & mask) | or;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long SetByteIn(long n, byte val, uint pos)
        {
            long mask = ~(0xFFL << (int)(pos * 8));
            long or = (long)((long)val << (int)(pos * 8));
            return (n & mask) | or;
        }


        // public bool bitReset(uint i, uint bit)
        // {
        //     return i & (~(1 << bit));
        // }

        // public bool bitSetValue(uint i, uint bit, bool value)
        // {
        //     if (value)
        //     {
        //         return i | (1 << bit);
        //     }
        //     else
        //     {
        //         return i & (~(1 << bit));
        //     }
        // }
    }

    public class CoreUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Swap<T>(ref T one, ref T two)
        {
            T temp = one;
            one = two;
            two = temp;
        }
    }
}