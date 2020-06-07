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

        public byte Read(uint addr)
        {
            // GBA Bios
            if (addr >= 0x00000000 && addr <= 0x00003FFF)
            {
                return Bios[addr - 0x00000000];
            }

            // External WRAM
            else if (addr >= 0x02000000 && addr <= 0x0203FFFF)
            {
                return Iwram[addr - 0x02000000];
            }

            // Internal WRAM
            else if (addr >= 0x03000000 && addr <= 0x03007FFF)
            {
                return Ewram[addr - 0x03000000];
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
            throw new Exception("Open Bus Read");
            // return 0;
        }

        public byte ReadHWIO(uint addr)
        {
            throw new Exception("HWIO Read");
        }
    }
}