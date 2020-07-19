using System;

namespace OptimeGBA
{
    public class Memory
    {

        public Memory(GBA gba)
        {

        }

        // Internal Work RAM
        public byte[] Iwram = new byte[32768];
        // External Work RAM
        public byte[] Ewram = new byte[262144];

        public byte[] Bios = new byte[16384];

        public byte[] Rom = new byte[33554432];

        public byte Read8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                return Ewram[addr - 0x02000000];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return Iwram[addr - 0x03000000];
            }

            // HWIO
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return ReadHWIO(addr);
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                return Rom[addr - 0x08000000];
            }

            // This should be open bus
            return 0;
        }

        public uint Read16(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);

            uint u32 = (uint)((f1 << 8) | (f0 << 0));

            return u32;
        }

        public uint Read32(uint addr)
        {
            byte f0 = Read8(addr++);
            byte f1 = Read8(addr++);
            byte f2 = Read8(addr++);
            byte f3 = Read8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        public byte ReadDebug8(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                return Ewram[addr - 0x02000000];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return Iwram[addr - 0x03000000];
            }

            // HWIO
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return ReadHWIO(addr);
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                return Rom[addr - 0x08000000];
            }

            // This should be open bus
            return 0;
        }

        public ushort ReadDebug16(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);

            ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

            return u16;
        }

        public uint ReadDebug32(uint addr)
        {
            byte f0 = ReadDebug8(addr++);
            byte f1 = ReadDebug8(addr++);
            byte f2 = ReadDebug8(addr++);
            byte f3 = ReadDebug8(addr++);

            uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

            return u32;
        }

        public void Write8(uint addr, byte val)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return;
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                Ewram[addr - 0x02000000] = val;
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                Iwram[addr - 0x03000000] = val;
            }

            // HWIO
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                WriteHWIO(addr, val);
            }

            // ROM
            else if (addr >= 0x08000000 && addr <= 0x09FFFFFF)
            {
                return;
            }
        }

        public void Write32(uint addr, uint val)
        {
            byte f0 = (byte)(val >> 0);
            byte f1 = (byte)(val >> 8);
            byte f2 = (byte)(val >> 16);
            byte f3 = (byte)(val >> 24);

            Write8(addr++, f0);
            Write8(addr++, f1);
            Write8(addr++, f2);
            Write8(addr++, f3);
        }

        public byte ReadHWIO(uint addr)
        {
            throw new Exception("HWIO Read");
        }

        public byte WriteHWIO(uint addr, byte val)
        {
            throw new Exception("HWIO Write");
        }
    }
}