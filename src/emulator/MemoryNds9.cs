using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using static OptimeGBA.Bits;
using System.Runtime.InteropServices;
using static OptimeGBA.MemoryUtil;
using static Util;

namespace OptimeGBA
{
    public sealed unsafe class MemoryNds9 : Memory
    {
        Nds Nds;

        public MemoryNds9(Nds nds, ProviderNds provider)
        {
            Nds = nds;

            SaveProvider = new NullSaveProvider();

            for (uint i = 0; i < Arm9BiosSize && i < provider.Bios9.Length; i++)
            {
                Arm9Bios[i] = provider.Bios9[i];
            }
        }

        public const int Arm9BiosSize = 4096;
        public byte[] Arm9Bios = MemoryUtil.AllocateManagedArray(Arm9BiosSize);
        public const int ItcmSize = 32768;
        public byte[] Itcm = MemoryUtil.AllocateManagedArray(ItcmSize);
        public const int DtcmSize = 16384;
        public byte[] Dtcm = MemoryUtil.AllocateManagedArray(DtcmSize);

        public uint DtcmBase = 0;
        public uint ItcmVirtualSize = 0;
        public uint DtcmVirtualSize = 0;
        public bool ItcmLoadMode = false;
        public bool DtcmLoadMode = false;

        public override void InitPageTable(byte*[] table, uint[] maskTable, bool write)
        {
            byte* mainRam = TryPinByteArray(Nds.MainRam);
            byte* arm9Bios = TryPinByteArray(Arm9Bios);
            byte* dtcm = TryPinByteArray(Dtcm);
            byte* itcm = TryPinByteArray(Itcm);

            // 12 bits shaved off already, shave off another 12 to get 24
            for (uint i = 0; i < 1048576; i++)
            {
                table[i] = null; // Clear everything out first, since on ARM9 things can move around

                uint addr = (uint)(i << 12);
                switch (i >> 12)
                {
                    case 0x2: // Main Memory
                        table[i] = mainRam;
                        maskTable[i] = 0x003FFFFF;
                        break;
                    case 0xFF: // BIOS
                        if (!write)
                        {
                            table[i] = arm9Bios;
                        }
                        maskTable[i] = 0x00000FFF;
                        break;
                }

                if (addr >= DtcmBase && addr < DtcmBase + DtcmVirtualSize)
                {

                    if (write || !DtcmLoadMode)
                    {
                        // Console.WriteLine("DTCM page set at " + Util.Hex(addr, 8));
                        table[i] = dtcm;
                    }
                    maskTable[i] = 0x00003FFF;
                }

                // ITCM is immovable
                // ITCM has higher priority so write pages in after DTCM
                if (addr < ItcmVirtualSize)
                {
                    if (write || !ItcmLoadMode)
                    {
                        table[i] = itcm;
                    }
                    maskTable[i] = 0x00007FFF;
                }
            }
        }

        ~MemoryNds9()
        {
            Console.WriteLine("Cleaning up NDS9 memory...");
            UnpinByteArray(Nds.MainRam);
            UnpinByteArray(Arm9Bios);
            UnpinByteArray(Dtcm);
            UnpinByteArray(Itcm);
        }

        public void UpdateTcmSettings()
        {
            // Console.WriteLine("Data TCM Settings: " + Util.Hex(Nds.Cp15.DataTcmSettings, 8));
            ItcmVirtualSize = 512U << (int)((Nds.Cp15.InstTcmSettings >> 1) & 0x1F);
            DtcmVirtualSize = 512U << (int)((Nds.Cp15.DataTcmSettings >> 1) & 0x1F);

            DtcmBase = (uint)(Nds.Cp15.DataTcmSettings & 0xFFFFF000);

            ItcmLoadMode = BitTest(Nds.Cp15.ControlRegister, 19);
            DtcmLoadMode = BitTest(Nds.Cp15.ControlRegister, 17);

            Console.WriteLine("ITCM set to: " + Util.Hex(0, 8) + " - " + Util.Hex(ItcmVirtualSize - 1, 8));
            Console.WriteLine("DTCM set to: " + Util.Hex(DtcmBase, 8) + " - " + Util.Hex(DtcmBase + DtcmVirtualSize - 1, 8));

            InitPageTables();
        }

        public (byte[] array, uint offset) GetSharedRamParams(uint addr)
        {
            switch (Nds.MemoryControl.SharedRamControl)
            {
                case 0:
                default:
                    addr &= 0x7FFF; // All 32k of Shared RAM
                    return (Nds.SharedRam, addr);
                case 1:
                    addr &= 0x3FFF; // 2nd half of Shared RAM
                    addr += 0x4000;
                    return (Nds.SharedRam, addr);
                case 2:
                    addr &= 0x3FFF; // 1st half of Shared RAM
                    return (Nds.SharedRam, addr);
                case 3:
                    // throw new NotImplementedException("Implement unmapping Shared RAM from ARM9 without EmptyPage, since some game can possibly try to write to the EmptyPage");
                    EmptyPage[0] = 0;
                    return (EmptyPage, 0); // Unmapped
            }
        }

        public override byte Read8Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetByte(array, offset);
                case 0x4: // I/O Registers
                    return ReadHwio8(debug, addr);
                case 0x5: // PPU Palettes
                    return Nds.Ppu.ReadPalettes8(addr);
                case 0x6: // VRAM
                    return Nds.Ppu.ReadVram8Arm9(addr);
                case 0x7: // PPU OAM
                    return Nds.Ppu.ReadOam8(addr);
            }

            return 0;
        }

        public override ushort Read16Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUshort(array, offset);
                case 0x4: // I/O Registers
                    byte f0 = ReadHwio8(debug, addr++);
                    byte f1 = ReadHwio8(debug, addr++);

                    ushort u16 = (ushort)((f1 << 8) | (f0 << 0));

                    return u16;
                case 0x5: // PPU Palettes
                    return Nds.Ppu.ReadPalettes16(addr);
                case 0x6: // VRAM
                    return (ushort)(
                        (Nds.Ppu.ReadVram8Arm9(addr + 0) << 0) |
                        (Nds.Ppu.ReadVram8Arm9(addr + 1) << 8)
                    );
                case 0x7: // PPU OAM
                    return Nds.Ppu.ReadOam16(addr);
            }

            return 0;
        }

        public override uint Read32Unregistered(bool debug, uint addr)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    return GetUint(array, offset);
                case 0x4: // I/O Registers
                    if (addr >= 0x4000320 && addr < 0x40006A4) // 3D
                    {
                        return Nds.Ppu3D.ReadHwio32(addr);
                    }

                    byte f0 = ReadHwio8(debug, addr + 0);
                    byte f1 = ReadHwio8(debug, addr + 1);
                    byte f2 = ReadHwio8(debug, addr + 2);
                    byte f3 = ReadHwio8(debug, addr + 3);

                    uint u32 = (uint)((f3 << 24) | (f2 << 16) | (f1 << 8) | (f0 << 0));

                    return u32;
                case 0x5: // PPU Palettes
                    return Nds.Ppu.ReadPalettes32(addr);
                case 0x6: // VRAM
                    return (uint)(
                        (Nds.Ppu.ReadVram8Arm9(addr + 0) << 0) |
                        (Nds.Ppu.ReadVram8Arm9(addr + 1) << 8) |
                        (Nds.Ppu.ReadVram8Arm9(addr + 2) << 16) |
                        (Nds.Ppu.ReadVram8Arm9(addr + 3) << 24)
                    );
                case 0x7: // PPU OAM
                    return Nds.Ppu.ReadOam32(addr);
            }

            return 0;
        }

        public override void Write8Unregistered(bool debug, uint addr, byte val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetByte(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr, val);
                    break;
                case 0x5: // PPU Palettes - duplicated across upper-lower in 8-bit??
                    Console.WriteLine("NDS: 8-bit write to palettes");
                    // Nds.Ppu.WritePalettes8(addr + 0, val);
                    // Nds.Ppu.WritePalettes8(addr + 1, val);
                    break;
            }
        }

        public override void Write16Unregistered(bool debug, uint addr, ushort val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUshort(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    break;
                case 0x5: // PPU Palettes
                    Nds.Ppu.WritePalettes16(addr, val);
                    break;
                case 0x6: // VRAM
                    Nds.Ppu.WriteVram8Arm9(addr + 0, (byte)(val >> 0));
                    Nds.Ppu.WriteVram8Arm9(addr + 1, (byte)(val >> 8));
                    break;
                case 0x7: // PPU OAM
                    Nds.Ppu.WriteOam16(addr, val);
                    break;
            }
        }

        public override void Write32Unregistered(bool debug, uint addr, uint val)
        {
            switch (addr >> 24)
            {
                case 0x3: // Shared RAM
                    (byte[] array, uint offset) = GetSharedRamParams(addr);
                    SetUint(array, offset, val);
                    break;
                case 0x4: // I/O Registers
                    if (addr >= 0x4000320 && addr < 0x40006A4) // 3D
                    {
                        Nds.Ppu3D.WriteHwio32(addr, val);
                        return;
                    }
                    WriteHwio8(debug, addr++, (byte)(val >> 0));
                    WriteHwio8(debug, addr++, (byte)(val >> 8));
                    WriteHwio8(debug, addr++, (byte)(val >> 16));
                    WriteHwio8(debug, addr++, (byte)(val >> 24));
                    break;
                case 0x5: // PPU Palettes
                    Nds.Ppu.WritePalettes32(addr, val);
                    break;
                case 0x6: // VRAM
                    Nds.Ppu.WriteVram8Arm9(addr + 0, (byte)(val >> 0));
                    Nds.Ppu.WriteVram8Arm9(addr + 1, (byte)(val >> 8));
                    Nds.Ppu.WriteVram8Arm9(addr + 2, (byte)(val >> 16));
                    Nds.Ppu.WriteVram8Arm9(addr + 3, (byte)(val >> 24));
                    break;
                case 0x7: // PPU OAM
                    Nds.Ppu.WriteOam32(addr, val);
                    break;
            }
        }

        public byte ReadHwio8(bool debug, uint addr)
        {
            if (LogHwioAccesses) 
            {
                lock (HwioReadLog) {
                    if ((addr & ~1) != 0 && !debug)
                    {
                        uint count;
                        HwioReadLog.TryGetValue(addr, out count);
                        HwioReadLog[addr] = count + 1;
                    }
                }
            }

            if (addr >= 0x4000320 && addr < 0x40006A4) // 3D
            {
                Console.Error.WriteLine("8-bit or 16-bit read to 3D");
                return 0;
            }

            switch (addr)
            {
                // Engine A
                case 0x4000000: case 0x4000001: case 0x4000002: case 0x4000003: // DISPCNT A
                case 0x4000004: case 0x4000005: // DISPSTAT
                case 0x4000006: case 0x4000007: // VCOUNT
                case 0x4000008: case 0x4000009: // BG0CNT
                case 0x400000A: case 0x400000B: // BG1CNT
                case 0x400000C: case 0x400000D: // BG2CNT
                case 0x400000E: case 0x400000F: // BG3CNT
                case 0x4000010: case 0x4000011: case 0x4000012: case 0x4000013: // BG0OFS
                case 0x4000014: case 0x4000015: case 0x4000016: case 0x4000017: // BG1OFS
                case 0x4000018: case 0x4000019: case 0x400001A: case 0x400001B: // BG2OFS
                case 0x400001C: case 0x400001D: case 0x400001E: case 0x400001F: // BG3OFS
                case 0x4000020: case 0x4000021: case 0x4000022: case 0x4000023: // BG2PA/PB
                case 0x4000024: case 0x4000025: case 0x4000026: case 0x4000027: // BG2PC/PD
                case 0x4000028: case 0x4000029: case 0x400002A: case 0x400002B: // BG2X
                case 0x400002C: case 0x400002D: case 0x400002E: case 0x400002F: // BG2Y
                case 0x4000030: case 0x4000031: case 0x4000032: case 0x4000033: // BG3PA/PB
                case 0x4000034: case 0x4000035: case 0x4000036: case 0x4000037: // BG3PC/PD
                case 0x4000038: case 0x4000039: case 0x400003A: case 0x400003B: // BG3X
                case 0x400003C: case 0x400003D: case 0x400003E: case 0x400003F: // BG3Y
                case 0x4000040: case 0x4000041: case 0x4000042: case 0x4000043: // WINH
                case 0x4000044: case 0x4000045: case 0x4000046: case 0x4000047: // WINV
                case 0x4000048: case 0x4000049: case 0x400004A: case 0x400004B: // WININ/OUT
                case 0x400004C: case 0x400004D: // MOSAIC
                case 0x4000050: case 0x4000051: // BLDCNT
                case 0x4000052: case 0x4000053: // BLDALPHA
                case 0x4000054: case 0x4000055: // BLDY
                case 0x4000060: case 0x4000061: // DISP3DCNT
                case 0x4000064: case 0x4000065: case 0x4000066: case 0x4000067: // DISPCAPCNT
                case 0x400006C: case 0x400006D: // MASTER_BRIGHT

                // Engine B
                case 0x4001000: case 0x4001001: case 0x4001002: case 0x4001003: // DISPCNT A
                case 0x4001008: case 0x4001009: // BG0CNT
                case 0x400100A: case 0x400100B: // BG1CNT
                case 0x400100C: case 0x400100D: // BG2CNT
                case 0x400100E: case 0x400100F: // BG3CNT
                case 0x4001010: case 0x4001011: case 0x4001012: case 0x4001013: // BG0OFS
                case 0x4001014: case 0x4001015: case 0x4001016: case 0x4001017: // BG1OFS
                case 0x4001018: case 0x4001019: case 0x400101A: case 0x400101B: // BG2OFS
                case 0x400101C: case 0x400101D: case 0x400101E: case 0x400101F: // BG3OFS
                case 0x4001020: case 0x4001021: case 0x4001022: case 0x4001023: // BG2PA/PB
                case 0x4001024: case 0x4001025: case 0x4001026: case 0x4001027: // BG2PC/PD
                case 0x4001028: case 0x4001029: case 0x400102A: case 0x400102B: // BG2X
                case 0x400102C: case 0x400102D: case 0x400102E: case 0x400102F: // BG2Y
                case 0x4001030: case 0x4001031: case 0x4001032: case 0x4001033: // BG3PA/PB
                case 0x4001034: case 0x4001035: case 0x4001036: case 0x4001037: // BG3PC/PD
                case 0x4001038: case 0x4001039: case 0x400103A: case 0x400103B: // BG3X
                case 0x400103C: case 0x400103D: case 0x400103E: case 0x400103F: // BG3Y
                case 0x4001040: case 0x4001041: case 0x4001042: case 0x4001043: // WINH
                case 0x4001044: case 0x4001045: case 0x4001046: case 0x4001047: // WINV
                case 0x4001048: case 0x4001049: case 0x400104A: case 0x400104B: // WININ/OUT
                case 0x400104C: case 0x400104D: // MOSAIC
                case 0x4001050: case 0x4001051: // BLDCNT
                case 0x4001052: case 0x4001053: // BLDALPHA
                case 0x4001054: case 0x4001055: // BLDY
                case 0x400106C: case 0x400106D: // MASTER_BRIGHT
                    return Nds.Ppu.ReadHwio8Arm9(addr);

                case 0x40000B0: case 0x40000B1: case 0x40000B2: case 0x40000B3: // DMA0SAD
                case 0x40000B4: case 0x40000B5: case 0x40000B6: case 0x40000B7: // DMA0DAD
                case 0x40000B8: case 0x40000B9: case 0x40000BA: case 0x40000BB: // DMA0CNT
                case 0x40000BC: case 0x40000BD: case 0x40000BE: case 0x40000BF: // DMA1SAD
                case 0x40000C0: case 0x40000C1: case 0x40000C2: case 0x40000C3: // DMA1DAD
                case 0x40000C4: case 0x40000C5: case 0x40000C6: case 0x40000C7: // DMA1CNT
                case 0x40000C8: case 0x40000C9: case 0x40000CA: case 0x40000CB: // DMA2SAD 
                case 0x40000CC: case 0x40000CD: case 0x40000CE: case 0x40000CF: // DMA2DAD
                case 0x40000D0: case 0x40000D1: case 0x40000D2: case 0x40000D3: // DMA2CNT
                case 0x40000D4: case 0x40000D5: case 0x40000D6: case 0x40000D7: // DMA3SAD
                case 0x40000D8: case 0x40000D9: case 0x40000DA: case 0x40000DB: // DMA3DAD
                case 0x40000DC: case 0x40000DD: case 0x40000DE: case 0x40000DF: // DMA3CNT
                case 0x40000E0: case 0x40000E1: case 0x40000E2: case 0x40000E3: // DMA0 Fill Data
                case 0x40000E4: case 0x40000E5: case 0x40000E6: case 0x40000E7: // DMA1 Fill Data
                case 0x40000E8: case 0x40000E9: case 0x40000EA: case 0x40000EB: // DMA2 Fill Data
                case 0x40000EC: case 0x40000ED: case 0x40000EE: case 0x40000EF: // DMA3 Fill Data
                    return Nds.Dma9.ReadHwio8(addr);

                case 0x4000100: case 0x4000101: case 0x4000102: case 0x4000103: // Timer 0
                case 0x4000104: case 0x4000105: case 0x4000106: case 0x4000107: // Timer 1
                case 0x4000108: case 0x4000109: case 0x400010A: case 0x400010B: // Timer 2
                case 0x400010C: case 0x400010D: case 0x400010E: case 0x400010F: // Timer 3
                    return Nds.Timers9.ReadHwio8(addr);

                case 0x4000180: case 0x4000181: case 0x4000182: case 0x4000183: // IPCSYNC
                case 0x4000184: case 0x4000185: case 0x4000186: case 0x4000187: // IPCFIFOCNT
                case 0x4000188: case 0x4000189: case 0x400018A: case 0x400018B: // IPCFIFOSEND
                case 0x4100000: case 0x4100001: case 0x4100002: case 0x4100003: // IPCFIFORECV
                    return Nds.Ipcs[0].ReadHwio8(addr);

                case 0x40001A0: case 0x40001A1: // AUXSPICNT
                case 0x40001A2: case 0x40001A3: // AUXSPIDATA
                case 0x40001A4: case 0x40001A5: case 0x40001A6: case 0x40001A7: // ROMCTRL
                case 0x4100010: case 0x4100011: case 0x4100012: case 0x4100013: // Slot 1 Data In
                    return Nds.Cartridge.ReadHwio8(false, addr);

                case 0x4000208: case 0x4000209: case 0x400020A: case 0x400020B: // IME
                case 0x4000210: case 0x4000211: case 0x4000212: case 0x4000213: // IE
                case 0x4000214: case 0x4000215: case 0x4000216: case 0x4000217: // IF
                    return Nds.HwControl9.ReadHwio8(addr);

                case 0x4000130: case 0x4000131: // KEYINPUT 
                    return Nds.Keypad.ReadHwio8(addr); 

                case 0x4000204: case 0x4000205: // EXMEMCNT
                case 0x4000240: case 0x4000241: case 0x4000242: case 0x4000243: // VRAMCNT
                case 0x4000244: case 0x4000245: case 0x4000246: case 0x4000247: // VRAMCNT, WRAMCNT
                case 0x4000248: case 0x4000249: // VRAMCNT
                    return Nds.MemoryControl.ReadHwio8Nds9(addr);

                case 0x4000280: case 0x4000281: case 0x4000282: case 0x4000283: // DIVCNT B3
                case 0x4000290: case 0x4000291: case 0x4000292: case 0x4000293: // DIV_NUMER
                case 0x4000294: case 0x4000295: case 0x4000296: case 0x4000297: // DIV_NUMER
                case 0x4000298: case 0x4000299: case 0x400029A: case 0x400029B: // DIV_DENOM
                case 0x400029C: case 0x400029D: case 0x400029E: case 0x400029F: // DIV_DENOM
                case 0x40002A0: case 0x40002A1: case 0x40002A2: case 0x40002A3: // DIV_RESULT
                case 0x40002A4: case 0x40002A5: case 0x40002A6: case 0x40002A7: // DIV_RESULT
                case 0x40002A8: case 0x40002A9: case 0x40002AA: case 0x40002AB: // DIVREM_RESULT
                case 0x40002AC: case 0x40002AD: case 0x40002AE: case 0x40002AF: // DIVREM_RESULT 
                case 0x40002B0: case 0x40002B1: // SQRTCNT 
                case 0x40002B4: case 0x40002B5: case 0x40002B6: case 0x40002B7: // SQRT_RESULT
                case 0x40002B8: case 0x40002B9: case 0x40002BA: case 0x40002BB: // SQRT_PARAM
                case 0x40002BC: case 0x40002BD: case 0x40002BE: case 0x40002BF: // SQRT_PARAM
                    return Nds.Math.ReadHwio8(addr);

                case 0x4000300:
                    // Console.WriteLine("NDS9 POSTFLG read");
                    return Nds.HwControl9.Postflg;
                case 0x4000304: case 0x4000305: case 0x4000306: case 0x4000307: // POWCNT1 
                    return Nds.ReadHwio8Arm9(addr);
            }

            // Console.WriteLine($"NDS9: Unmapped MMIO read addr:{Hex(addr, 8)}");

            return 0;
        }

        public void WriteHwio8(bool debug, uint addr, byte val)
        {
            if (LogHwioAccesses)
            {
                lock (HwioWriteLog) {
                    if ((addr & ~1) != 0 && !debug)
                    {
                        uint count;
                        HwioWriteLog.TryGetValue(addr, out count);
                        HwioWriteLog[addr] = count + 1;
                    }
                }
            }

            if (addr >= 0x4000320 && addr < 0x40006A4) // 3D
            {
                // Console.Error.WriteLine($"8-bit or 16-bit write to 3D addr:{Hex(addr, 8)} val:{Hex(val, 2)}");
                return;
            }

            switch (addr)
            {
                // Engine A
                case 0x4000000: case 0x4000001: case 0x4000002: case 0x4000003: // DISPCNT A
                case 0x4000004: case 0x4000005: // DISPSTAT
                case 0x4000006: case 0x4000007: // VCOUNT
                case 0x4000008: case 0x4000009: // BG0CNT
                case 0x400000A: case 0x400000B: // BG1CNT
                case 0x400000C: case 0x400000D: // BG2CNT
                case 0x400000E: case 0x400000F: // BG3CNT
                case 0x4000010: case 0x4000011: case 0x4000012: case 0x4000013: // BG0OFS
                case 0x4000014: case 0x4000015: case 0x4000016: case 0x4000017: // BG1OFS
                case 0x4000018: case 0x4000019: case 0x400001A: case 0x400001B: // BG2OFS
                case 0x400001C: case 0x400001D: case 0x400001E: case 0x400001F: // BG3OFS
                case 0x4000020: case 0x4000021: case 0x4000022: case 0x4000023: // BG2PA/PB
                case 0x4000024: case 0x4000025: case 0x4000026: case 0x4000027: // BG2PC/PD
                case 0x4000028: case 0x4000029: case 0x400002A: case 0x400002B: // BG2X
                case 0x400002C: case 0x400002D: case 0x400002E: case 0x400002F: // BG2Y
                case 0x4000030: case 0x4000031: case 0x4000032: case 0x4000033: // BG3PA/PB
                case 0x4000034: case 0x4000035: case 0x4000036: case 0x4000037: // BG3PC/PD
                case 0x4000038: case 0x4000039: case 0x400003A: case 0x400003B: // BG3X
                case 0x400003C: case 0x400003D: case 0x400003E: case 0x400003F: // BG3Y
                case 0x4000040: case 0x4000041: case 0x4000042: case 0x4000043: // WINH
                case 0x4000044: case 0x4000045: case 0x4000046: case 0x4000047: // WINV
                case 0x4000048: case 0x4000049: case 0x400004A: case 0x400004B: // WININ/OUT
                case 0x400004C: case 0x400004D: // MOSAIC
                case 0x4000050: case 0x4000051: // BLDCNT
                case 0x4000052: case 0x4000053: // BLDALPHA
                case 0x4000054: case 0x4000055: // BLDY
                case 0x4000060: case 0x4000061: // DISP3DCNT
                case 0x4000064: case 0x4000065: case 0x4000066: case 0x4000067: // DISPCAPCNT
                case 0x400006C: case 0x400006D: // MASTER_BRIGHT

                // Engine B
                case 0x4001000: case 0x4001001: case 0x4001002: case 0x4001003: // DISPCNT A
                case 0x4001008: case 0x4001009: // BG0CNT
                case 0x400100A: case 0x400100B: // BG1CNT
                case 0x400100C: case 0x400100D: // BG2CNT
                case 0x400100E: case 0x400100F: // BG3CNT
                case 0x4001010: case 0x4001011: case 0x4001012: case 0x4001013: // BG0OFS
                case 0x4001014: case 0x4001015: case 0x4001016: case 0x4001017: // BG1OFS
                case 0x4001018: case 0x4001019: case 0x400101A: case 0x400101B: // BG2OFS
                case 0x400101C: case 0x400101D: case 0x400101E: case 0x400101F: // BG3OFS
                case 0x4001020: case 0x4001021: case 0x4001022: case 0x4001023: // BG2PA/PB
                case 0x4001024: case 0x4001025: case 0x4001026: case 0x4001027: // BG2PC/PD
                case 0x4001028: case 0x4001029: case 0x400102A: case 0x400102B: // BG2X
                case 0x400102C: case 0x400102D: case 0x400102E: case 0x400102F: // BG2Y
                case 0x4001030: case 0x4001031: case 0x4001032: case 0x4001033: // BG3PA/PB
                case 0x4001034: case 0x4001035: case 0x4001036: case 0x4001037: // BG3PC/PD
                case 0x4001038: case 0x4001039: case 0x400103A: case 0x400103B: // BG3X
                case 0x400103C: case 0x400103D: case 0x400103E: case 0x400103F: // BG3Y
                case 0x4001040: case 0x4001041: case 0x4001042: case 0x4001043: // WINH
                case 0x4001044: case 0x4001045: case 0x4001046: case 0x4001047: // WINV
                case 0x4001048: case 0x4001049: case 0x400104A: case 0x400104B: // WININ/OUT
                case 0x400104C: case 0x400104D: // MOSAIC
                case 0x4001050: case 0x4001051: // BLDCNT
                case 0x4001052: case 0x4001053: // BLDALPHA
                case 0x4001054: case 0x4001055: // BLDY
                case 0x400106C: case 0x400106D: // MASTER_BRIGHT
                    Nds.Ppu.WriteHwio8Arm9(addr, val); return;

                case 0x40000B0: case 0x40000B1: case 0x40000B2: case 0x40000B3: // DMA0SAD
                case 0x40000B4: case 0x40000B5: case 0x40000B6: case 0x40000B7: // DMA0DAD
                case 0x40000B8: case 0x40000B9: case 0x40000BA: case 0x40000BB: // DMA0CNT
                case 0x40000BC: case 0x40000BD: case 0x40000BE: case 0x40000BF: // DMA1SAD
                case 0x40000C0: case 0x40000C1: case 0x40000C2: case 0x40000C3: // DMA1DAD
                case 0x40000C4: case 0x40000C5: case 0x40000C6: case 0x40000C7: // DMA1CNT
                case 0x40000C8: case 0x40000C9: case 0x40000CA: case 0x40000CB: // DMA2SAD 
                case 0x40000CC: case 0x40000CD: case 0x40000CE: case 0x40000CF: // DMA2DAD
                case 0x40000D0: case 0x40000D1: case 0x40000D2: case 0x40000D3: // DMA2CNT
                case 0x40000D4: case 0x40000D5: case 0x40000D6: case 0x40000D7: // DMA3SAD
                case 0x40000D8: case 0x40000D9: case 0x40000DA: case 0x40000DB: // DMA3DAD
                case 0x40000DC: case 0x40000DD: case 0x40000DE: case 0x40000DF: // DMA3CNT
                case 0x40000E0: case 0x40000E1: case 0x40000E2: case 0x40000E3: // DMA0 Fill Data
                case 0x40000E4: case 0x40000E5: case 0x40000E6: case 0x40000E7: // DMA1 Fill Data
                case 0x40000E8: case 0x40000E9: case 0x40000EA: case 0x40000EB: // DMA2 Fill Data
                case 0x40000EC: case 0x40000ED: case 0x40000EE: case 0x40000EF: // DMA3 Fill Data
                    Nds.Dma9.WriteHwio8(addr, val); return;

                case 0x4000100: case 0x4000101: case 0x4000102: case 0x4000103: // Timer 0
                case 0x4000104: case 0x4000105: case 0x4000106: case 0x4000107: // Timer 1
                case 0x4000108: case 0x4000109: case 0x400010A: case 0x400010B: // Timer 2
                case 0x400010C: case 0x400010D: case 0x400010E: case 0x400010F: // Timer 3
                    Nds.Timers9.WriteHwio8(addr, val); return;

                case 0x4000180: case 0x4000181: case 0x4000182: case 0x4000183: // IPCSYNC
                case 0x4000184: case 0x4000185: case 0x4000186: case 0x4000187: // IPCFIFOCNT
                case 0x4000188: case 0x4000189: case 0x400018A: case 0x400018B: // IPCFIFOSEND
                    Nds.Ipcs[0].WriteHwio8(addr, val); return;

                case 0x40001A0: case 0x40001A1: // AUXSPICNT
                case 0x40001A2: case 0x40001A3: // AUXSPIDATA
                case 0x40001A4: case 0x40001A5: case 0x40001A6: case 0x40001A7: // ROMCTRL
                case 0x40001A8: case 0x40001A9: case 0x40001AA: case 0x40001AB: // Slot 1 Command 0-3
                case 0x40001AC: case 0x40001AD: case 0x40001AE: case 0x40001AF: // Slot 1 Command 4-7
                    Nds.Cartridge.WriteHwio8(false, addr, val); return;

                case 0x40001B0: case 0x40001B1: case 0x40001B2: case 0x40001B3: // Slot 1 KEY2 encryption seed
                case 0x40001B4: case 0x40001B5: case 0x40001B6: case 0x40001B7: 
                case 0x40001B8: case 0x40001B9: case 0x40001BA: case 0x40001BB: 
                    return;

                case 0x4000208: case 0x4000209: case 0x400020A: case 0x400020B: // IME
                case 0x4000210: case 0x4000211: case 0x4000212: case 0x4000213: // IE
                case 0x4000214: case 0x4000215: case 0x4000216: case 0x4000217: // IF
                    Nds.HwControl9.WriteHwio8(addr, val); return;

                case 0x4000204: case 0x4000205: // EXMEMCNT
                case 0x4000240: case 0x4000241: case 0x4000242: case 0x4000243: // VRAMCNT
                case 0x4000244: case 0x4000245: case 0x4000246: case 0x4000247: // VRAMCNT, WRAMCNT
                case 0x4000248: case 0x4000249: // VRAMCNT
                    Nds.MemoryControl.WriteHwio8Nds9(addr, val); return;

                case 0x4000280: case 0x4000281: case 0x4000282: case 0x4000283: // DIVCNT B3
                case 0x4000290: case 0x4000291: case 0x4000292: case 0x4000293: // DIV_NUMER
                case 0x4000294: case 0x4000295: case 0x4000296: case 0x4000297: // DIV_NUMER
                case 0x4000298: case 0x4000299: case 0x400029A: case 0x400029B: // DIV_DENOM
                case 0x400029C: case 0x400029D: case 0x400029E: case 0x400029F: // DIV_DENOM
                case 0x40002A0: case 0x40002A1: case 0x40002A2: case 0x40002A3: // DIV_RESULT
                case 0x40002A4: case 0x40002A5: case 0x40002A6: case 0x40002A7: // DIV_RESULT
                case 0x40002A8: case 0x40002A9: case 0x40002AA: case 0x40002AB: // DIVREM_RESULT
                case 0x40002AC: case 0x40002AD: case 0x40002AE: case 0x40002AF: // DIVREM_RESULT 
                case 0x40002B0: case 0x40002B1: // SQRTCNT 
                case 0x40002B4: case 0x40002B5: case 0x40002B6: case 0x40002B7: // SQRT_RESULT
                case 0x40002B8: case 0x40002B9: case 0x40002BA: case 0x40002BB: // SQRT_PARAM
                case 0x40002BC: case 0x40002BD: case 0x40002BE: case 0x40002BF: // SQRT_PARAM
                    Nds.Math.WriteHwio8(addr, val); return;

                case 0x4000300:
                    Console.WriteLine("NDS9 POSTFLG write");
                    Nds.HwControl9.Postflg = (byte)(val & 0b11);
                    return;
                case 0x4000304: case 0x4000305: case 0x4000306: case 0x4000307:// POWCNT1
                    Nds.WriteHwio8Arm9(addr, val);
                    return;
            }

            // Console.WriteLine($"NDS9: Unmapped MMIO write addr:{Hex(addr, 8)} val:{Hex(val, 2)}");
        }
    }
}
