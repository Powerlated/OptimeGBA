using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;

namespace OptimeGBA
{
    public static unsafe class MemoryUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetUlong(byte[] arr, uint addr)
        {
            return
                ((ulong)arr[addr + 0] << 0) |
                ((ulong)arr[addr + 1] << 8) |
                ((ulong)arr[addr + 2] << 16) |
                ((ulong)arr[addr + 3] << 24) |
                ((ulong)arr[addr + 4] << 32) |
                ((ulong)arr[addr + 5] << 40) |
                ((ulong)arr[addr + 6] << 48) |
                ((ulong)arr[addr + 7] << 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetUint(byte[] arr, uint addr)
        {
            return (uint)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8) |
                    (arr[addr + 2] << 16) |
                    (arr[addr + 3] << 24)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetUshort(byte[] arr, uint addr)
        {
            return (ushort)(
                    (arr[addr + 0] << 0) |
                    (arr[addr + 1] << 8)
                );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByte(byte[] arr, uint addr)
        {
            return arr[addr];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong GetUlong(byte* arr, uint addr)
        {
            return *(ulong*)(arr + addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetUint(byte* arr, uint addr)
        {
            return *(uint*)(arr + addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetUshort(byte* arr, uint addr)
        {
            return *(ushort*)(arr + addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte GetByte(byte* arr, uint addr)
        {
            return *(byte*)(arr + addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUlong(byte[] arr, uint addr, ulong val)
        {
            arr[addr + 0] = (byte)(val >> 0);
            arr[addr + 1] = (byte)(val >> 8);
            arr[addr + 2] = (byte)(val >> 16);
            arr[addr + 3] = (byte)(val >> 24);
            arr[addr + 4] = (byte)(val >> 32);
            arr[addr + 5] = (byte)(val >> 40);
            arr[addr + 6] = (byte)(val >> 48);
            arr[addr + 7] = (byte)(val >> 56);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUint(byte[] arr, uint addr, uint val)
        {
            arr[addr + 0] = (byte)(val >> 0);
            arr[addr + 1] = (byte)(val >> 8);
            arr[addr + 2] = (byte)(val >> 16);
            arr[addr + 3] = (byte)(val >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUshort(byte[] arr, uint addr, ushort val)
        {
            arr[addr + 0] = (byte)(val >> 0);
            arr[addr + 1] = (byte)(val >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetByte(byte[] arr, uint addr, byte val)
        {
            arr[addr] = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUlong(byte* arr, uint addr, ulong val)
        {
            *(ulong*)(arr + addr) = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUint(byte* arr, uint addr, uint val)
        {
            *(uint*)(arr + addr) = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetUshort(byte* arr, uint addr, ushort val)
        {
            *(ushort*)(arr + addr) = val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetByte(byte* arr, uint addr, byte val)
        {
            *(byte*)(arr + addr) = val;
        }

        public static byte* AllocateUnmanagedArray(int size)
        {
            byte* arr = (byte*)Marshal.AllocHGlobal(size).ToPointer();

            // Zero out array
            for (int i = 0; i < size; i++)
            {
                arr[i] = 0;
            }

            return arr;
        }

        public static ushort* AllocateUnmanagedArray16(int size)
        {
            ushort* arr = (ushort*)Marshal.AllocHGlobal(size * sizeof(ushort)).ToPointer();

            // Zero out array
            for (int i = 0; i < size; i++)
            {
                arr[i] = 0;
            }

            return arr;
        }

        public static uint* AllocateUnmanagedArray32(int size)
        {
            uint* arr = (uint*)Marshal.AllocHGlobal(size * sizeof(uint)).ToPointer();

            // Zero out array
            for (int i = 0; i < size; i++)
            {
                arr[i] = 0;
            }

            return arr;
        }

        public static void FreeUnmanagedArray(void* arr)
        {
            Marshal.FreeHGlobal(new IntPtr(arr));
        }

        public static byte[] AllocateManagedArray(int size)
        {
            return new byte[size];
        }

        public static ushort[] AllocateManagedArray16(int size)
        {
            return new ushort[size];
        }

        public static uint[] AllocateManagedArray32(int size)
        {
            return new uint[size];
        }
    }
}
