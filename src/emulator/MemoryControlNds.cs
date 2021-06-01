using System;
using static OptimeGBA.Bits;
using System.Runtime.CompilerServices;

namespace OptimeGBA
{
    public class MemoryControlNds
    {
        public byte SharedRamControl;

        public byte[] VRAMCNT = new byte[9];

        public byte ReadHwio8Nds9(uint addr)
        {
            switch (addr)
            {
                case 0x4000240: return VRAMCNT[0];
                case 0x4000241: return VRAMCNT[1];
                case 0x4000242: return VRAMCNT[2];
                case 0x4000243: return VRAMCNT[3];
                case 0x4000244: return VRAMCNT[4];
                case 0x4000245: return VRAMCNT[5];
                case 0x4000246: return VRAMCNT[6];
                case 0x4000248: return VRAMCNT[7];
                case 0x4000249: return VRAMCNT[8];

                case 0x4000247: // Weirdo. Why you gotta place yourself in the middle of VRAMCNT?
                    return SharedRamControl;
            }

            return 0;
        }

        public void WriteHwio8Nds9(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000240: VRAMCNT[0] = val; break;
                case 0x4000241: VRAMCNT[1] = val; break;
                case 0x4000242: VRAMCNT[2] = val; break;
                case 0x4000243: VRAMCNT[3] = val; break;
                case 0x4000244: VRAMCNT[4] = val; break;
                case 0x4000245: VRAMCNT[5] = val; break;
                case 0x4000246: VRAMCNT[6] = val; break;
                case 0x4000248: VRAMCNT[7] = val; break;
                case 0x4000249: VRAMCNT[8] = val; break;

                case 0x4000247:
                    SharedRamControl = (byte)(val & 0b11);
                    break;
            }

            if (VramEnabledAndSet(2, 2) || VramEnabledAndSet(3, 2))
            {
                throw new NotImplementedException("Implement mapping VRAM banks C and D to ARM7");
            }
        }

        public byte ReadHwio8Nds7(uint addr)
        {
            switch (addr)
            {
                case 0x4000240:
                    throw new NotImplementedException("Implement VRAMSTAT for NDS7");
                    return 0;
                case 0x4000241:
                    return SharedRamControl;
            }

            return 0;
        }

        public void WriteHwio8Nds7(uint addr)
        {
            return;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool VramEnabledAndSet(uint bank, uint mst)
        {
            uint vramcntMst = VRAMCNT[bank] & 0b111U;
            bool vramcntEnable = BitTest(VRAMCNT[bank], 7);

            return vramcntEnable && vramcntMst == mst;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetOffset(uint bank)
        {
            return (uint)(VRAMCNT[bank] >> 3) & 0b11U;
        }
    }
}