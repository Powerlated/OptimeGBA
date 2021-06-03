using static Util;
using static OptimeGBA.Bits;
using static OptimeGBA.MemoryUtil;
using System;
namespace OptimeGBA
{
    public enum CartridgeState
    {
        Dummy,
        ReadCartridgeHeader,
        ReadRomChipId1,
        Dummy2,
        ReadRomChipId2,
        Key2DataRead,
        SecureAreaRead,
        ReadRomChipId3,
    }

    public class CartridgeNds
    {
        Nds Nds;
        byte[] Rom;

        public uint[] EncLutKeycodeLevel1 = new uint[0x412];
        public uint[] EncLutKeycodeLevel2 = new uint[0x412];
        public uint[] EncLutKeycodeLevel3 = new uint[0x412];

        public uint IdCode;

        public CartridgeNds(Nds nds)
        {
            Nds = nds;
            Rom = new byte[Nds.Provider.Rom.Length];
            Nds.Provider.Rom.CopyTo(Rom, 0);

            for (uint i = 0; i < 0x412; i++)
            {
                uint val = GetUint(Nds.Provider.Bios7, 0x30 + i * 4);
                EncLutKeycodeLevel1[i] = val;
                EncLutKeycodeLevel2[i] = val;
                EncLutKeycodeLevel3[i] = val;
            }

            if (Rom.Length >= 0x10)
            {
                IdCode = GetUint(Rom, 0x0C);
            }
            Console.WriteLine("Game ID: " + Hex(IdCode, 4));

            InitKeycode(EncLutKeycodeLevel1, 1);
            InitKeycode(EncLutKeycodeLevel2, 2);
            InitKeycode(EncLutKeycodeLevel3, 3);

            if (!Nds.Provider.DirectBoot && Rom.Length >= 0x8000)
            {
                Console.WriteLine("Encrypting first 2KB of secure area");
                SetUlong(Rom, 0x4000, 0x6A624F7972636E65); // Write in "encryObj"

                // Encrypt first 2K of the secure area with KEY1
                for (uint i = 0x4000; i < 0x4800; i += 8)
                {
                    SetUlong(Rom, i, Encrypt64(EncLutKeycodeLevel3, GetUlong(Rom, i)));
                }

                SetUlong(Rom, 0x4000, Encrypt64(EncLutKeycodeLevel3, GetUlong(Rom, 0x4000)));
            }
        }

        ulong PendingCommand;

        // some GBATek example
        // TODO: Replace this with something more realistic, maybe from a game DB
        public uint RomChipId = 0x00001FC2;

        // State
        public CartridgeState State;
        public uint DataPos;
        public uint BytesTransferred;
        public bool Key1Encryption;
        public bool Key2Encryption;

        public uint TransferLength;
        public uint PendingDummyWrites;

        public bool ReadyBit23;
        public byte BlockSize;
        public bool SlowTransferClock;
        public bool BusyBit31;

        // AUXSPICNT
        byte SpiBaudRate;
        bool SpiHoldChipSel = false;
        bool SpiBusy = false;
        bool Slot1SpiMode = false;
        bool TransferReadyIrq = false;
        bool Slot1Enable = false;

        // ROMCTRL
        byte ROMCTRLB0;
        byte ROMCTRLB1;
        bool ReleaseReset;

        // cart input
        uint InData;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x40001A0: // AUXSPICNT B0
                    val |= SpiBaudRate;
                    if (SpiHoldChipSel) val = BitSet(val, 6);
                    if (SpiBusy) val = BitSet(val, 7);
                    break;
                case 0x40001A1: // AUXSPICNT B0
                    if (Slot1SpiMode) val = BitSet(val, 5);
                    if (TransferReadyIrq) val = BitSet(val, 6);
                    if (Slot1Enable) val = BitSet(val, 7);
                    break;

                case 0x40001A4: // ROMCTRL B0
                    return ROMCTRLB0;
                case 0x40001A5: // ROMCTRL B1
                    return ROMCTRLB1;
                case 0x40001A6: // ROMCTRL B2
                    if (ReleaseReset) val = BitSet(val, 5);
                    if (ReadyBit23) val = BitSet(val, 7);
                    break;
                case 0x40001A7: // ROMCTRL B3
                    val |= BlockSize;
                    if (SlowTransferClock) val = BitSet(val, 3);
                    if (BusyBit31) val = BitSet(val, 7);
                    break;

                case 0x4100010: // From cartridge
                    InData = ReadData();
                    return (byte)(InData >> 0);
                case 0x4100011:
                    return (byte)(InData >> 8);
                case 0x4100012:
                    return (byte)(InData >> 16);
                case 0x4100013:
                    return (byte)(InData >> 24);
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x40001A0: // AUXSPICNT B0
                    SpiBaudRate = (byte)(val & 0b11);
                    SpiHoldChipSel = BitTest(val, 6);
                    SpiBusy = BitTest(val, 7);
                    return;
                case 0x40001A1: // AUXSPICNT B0
                    Slot1SpiMode = BitTest(val, 5);
                    TransferReadyIrq = BitTest(val, 6);
                    Slot1Enable = BitTest(val, 7);
                    return;

                case 0x40001A4: // ROMCTRL B0
                    ROMCTRLB0 = val;
                    break;
                case 0x40001A5: // ROMCTRL B1
                    ROMCTRLB1 = val;
                    break;
                case 0x40001A6: // ROMCTRL B2
                    if (BitTest(val, 5)) ReleaseReset = true;
                    break;
                case 0x40001A7: // ROMCTRL B3
                    BlockSize = (byte)(val & 0b111);
                    SlowTransferClock = BitTest(val, 3);

                    if (BitTest(val, 7) && !BusyBit31)
                    {
                        ProcessCommand();
                    }
                    break;
            }

            if (Slot1Enable)
            {
                switch (addr)
                {
                    case 0x40001A8: // Slot 1 Command out
                    case 0x40001A9:
                    case 0x40001AA:
                    case 0x40001AB:
                    case 0x40001AC:
                    case 0x40001AD:
                    case 0x40001AE:
                    case 0x40001AF:
                        int shiftBy = (int)((7 - (addr & 7)) * 8);
                        PendingCommand &= (ulong)(~(0xFFUL << shiftBy));
                        PendingCommand |= (ulong)val << shiftBy;
                        return;
                }
            }
        }

        public void ProcessCommand()
        {
            // TODO: Implement more commands
            var cmd = PendingCommand;

            if (Key1Encryption)
            {
                // Console.WriteLine("Decrypting command: " + Hex(cmd, 16));
                cmd = Decrypt64(EncLutKeycodeLevel2, cmd);
            }

            if (BlockSize == 0)
            {
                TransferLength = 0;
            }
            else if (BlockSize == 7)
            {
                TransferLength = 4;
            }
            else
            {
                TransferLength = 0x100U << BlockSize;

            }

            if (TransferLength != 0)
            {
                DataPos = 0;
                BytesTransferred = 0;
            }

            BusyBit31 = true;

            // Console.WriteLine("Transfer length: " + TransferLength);

            unsafe
            {
                Console.WriteLine("ARM7 R15: " + Hex(Nds.Nds7.Cpu.R[15], 8));
            }
            Console.WriteLine("Slot 1 Command: " + Hex(cmd, 16));

            if (cmd == 0x9F00000000000000)
            {
                State = CartridgeState.Dummy;
            }
            else if (cmd == 0x0000000000000000)
            {
                // Console.WriteLine("Slot 1: Putting up cartridge header");
                State = CartridgeState.ReadCartridgeHeader;
            }
            else if (cmd == 0x9000000000000000)
            {
                // Console.WriteLine("Slot 1: Putting up ROM chip ID 1");
                State = CartridgeState.ReadRomChipId1;
            }
            else if ((cmd & 0xFF00000000000000) == 0x3C00000000000000)
            {
                // Console.WriteLine("Slot 1: Enabled KEY1 encryption");
                State = CartridgeState.Dummy2;
                Key1Encryption = true;
            }
            else if ((cmd & 0xF000000000000000) == 0x2000000000000000)
            {
                // Console.WriteLine("Slot 1: Get Secure Area Block");
                State = CartridgeState.SecureAreaRead;
                DataPos = (uint)(((cmd >> 44) & 0xFFFF) * 0x1000);
            }
            else if ((cmd & 0xF000000000000000) == 0x4000000000000000)
            {
                // Console.WriteLine("Slot 1: Enable KEY2");
                State = CartridgeState.Dummy2;
            }
            else if ((cmd & 0xF000000000000000) == 0x1000000000000000)
            {
                // Console.WriteLine("Slot 1: Putting up ROM chip ID 2");
                State = CartridgeState.ReadRomChipId2;
            }
            else if ((cmd & 0xF000000000000000) == 0xA000000000000000)
            {
                // Console.WriteLine("Slot 1: Enter main data mode");
                State = CartridgeState.Dummy2;
                Key1Encryption = false;
            }
            else if ((cmd & 0xFF00000000FFFFFF) == 0xB700000000000000)
            {
                // On a real DS, KEY2 encryption is transparent to software,
                // as it is all handled in the hardware cartridge interface.
                // Plus, DS ROM dumps are usually KEY2 decrypted, so in most cases 
                // there's actually no need to actually handle KEY2 encryption in
                // an emulator.
                // Console.WriteLine("KEY2 data read");
                State = CartridgeState.Key2DataRead;

                DataPos = (uint)((cmd >> 24) & 0xFFFFFFFF);
                // Console.WriteLine("Addr: " + Hex(DataPos, 8));
            }
            else if (cmd == 0xB800000000000000)
            {
                // Console.WriteLine("Slot 1: Putting up ROM chip ID 3");
                State = CartridgeState.ReadRomChipId3;
            }
            else
            {
                throw new NotImplementedException("Slot 1: unimplemented command " + Hex(cmd, 16));
            }



            // If block size is zero, no transfer will take place, signal end.
            if (TransferLength == 0)
            {
                EndTransfer();
            }
            else
            {
                ReadyBit23 = true;

                // bool dma = false;
                // while (Nds.Nds7.Dma.Repeat((byte)DmaStartTimingNds7.Slot1)) {
                //     dma = true;
                // }

                // if (dma) {
                //     EndTransfer();
                // }
            }
        }

        // TODO: The BIOS isn't sending the command to read the secure area,
        // what is going on????????????
        public uint ReadData()
        {
            if (!ReadyBit23)
            {
                return 0xFFFFFFFF;
            }

            uint val = 0;
            switch (State)
            {
                case CartridgeState.Dummy:
                    val = 0xFFFFFFFF;
                    break;
                case CartridgeState.ReadCartridgeHeader:
                    // Repeatedly returns first 0x1000 bytes, with first 0x200 bytes filled
                    val = GetUint(Rom, DataPos & 0xFFF);
                    // Console.WriteLine("Read header byte " + DataPos);
                    break;
                case CartridgeState.ReadRomChipId1:
                    val = RomChipId;
                    // Console.WriteLine("Read ROM chip id 1 byte " + DataPos);
                    break;
                case CartridgeState.Dummy2:
                    val = 0xFFFFFFFF;
                    break;
                case CartridgeState.Key2DataRead:
                    // Console.WriteLine("Key2 data read");
                    val = GetUint(Rom, DataPos);
                    break;
                case CartridgeState.ReadRomChipId2:
                    val = RomChipId;
                    // Console.WriteLine("Read ROM chip id 2 byte " + DataPos);
                    break;
                case CartridgeState.ReadRomChipId3:
                    val = RomChipId;
                    // Console.WriteLine("Read ROM chip id 3 byte " + DataPos);
                    break;
                case CartridgeState.SecureAreaRead:
                    val = GetUint(Rom, DataPos);
                    break;
            }

            if (BusyBit31)
            {
                DataPos += 4;
                BytesTransferred += 4;
                if (BytesTransferred >= TransferLength)
                {
                    EndTransfer();
                }
            }
            else
            {
                return 0xFFFFFFFF;
            }

            return val;
        }

        public void EndTransfer()
        {
            // Console.WriteLine("Transfer complete");
            BusyBit31 = false;
            ReadyBit23 = false;

            if (TransferReadyIrq)
            {
                Nds.Nds7.HwControl.FlagInterrupt((uint)InterruptNds.Slot1DataTransferComplete);
            }
        }

        // From the Key1 Encryption section of GBATek.
        // Thanks Martin Korth.
        public static ulong Encrypt64(uint[] encLut, ulong val)
        {
            uint y = (uint)val;
            uint x = (uint)(val >> 32);
            for (uint i = 0; i < 0x10; i++)
            {
                uint z = encLut[i] ^ x;
                x = encLut[0x012 + (byte)(z >> 24)];
                x = encLut[0x112 + (byte)(z >> 16)] + x;
                x = encLut[0x212 + (byte)(z >> 8)] ^ x;
                x = encLut[0x312 + (byte)(z >> 0)] + x;
                x ^= y;
                y = z;
            }
            uint outLower = x ^ encLut[0x10];
            uint outUpper = y ^ encLut[0x11];

            return ((ulong)outUpper << 32) | outLower;
        }

        public static ulong Decrypt64(uint[] encLut, ulong val)
        {
            uint y = (uint)val;
            uint x = (uint)(val >> 32);
            for (uint i = 0x11; i >= 0x02; i--)
            {
                uint z = encLut[i] ^ x;
                x = encLut[0x012 + (byte)(z >> 24)];
                x = encLut[0x112 + (byte)(z >> 16)] + x;
                x = encLut[0x212 + (byte)(z >> 8)] ^ x;
                x = encLut[0x312 + (byte)(z >> 0)] + x;
                x ^= y;
                y = z;
            }
            uint outLower = x ^ encLut[0x1];
            uint outUpper = y ^ encLut[0x0];

            return ((ulong)outUpper << 32) | outLower;
        }

        // modulo is always 0x08
        public void ApplyKeycode(uint[] encLut, Span<uint> keyCode, uint modulo)
        {
            ulong encrypted1 = Encrypt64(encLut, ((ulong)keyCode[2] << 32) | keyCode[1]);
            keyCode[1] = (uint)encrypted1;
            keyCode[2] = (uint)(encrypted1 >> 32);
            ulong encrypted0 = Encrypt64(encLut, ((ulong)keyCode[1] << 32) | keyCode[0]);
            keyCode[0] = (uint)encrypted0;
            keyCode[1] = (uint)(encrypted0 >> 32);

            ulong scratch = 0;

            for (uint i = 0; i < 0x12; i++)
            {
                encLut[i] ^= BSwap32(keyCode[(int)(i % modulo)]);
            }

            // EncLut is stored in uint for convenience so iterate in uints as well
            for (uint i = 0; i < 0x412; i += 2)
            {
                scratch = Encrypt64(encLut, scratch);
                encLut[i + 0] = (uint)(scratch >> 32);
                encLut[i + 1] = (uint)scratch;
            }
        }

        public void InitKeycode(uint[] encLut, uint level)
        {
            Span<uint> keyCode = stackalloc uint[3];
            keyCode[0] = IdCode;
            keyCode[1] = IdCode / 2;
            keyCode[2] = IdCode * 2;

            // For game cartridge KEY1 decryption, modulo is always 2 (says 8 in GBATek)
            // but is 2 when divided by four to convert from byte to uint
            if (level >= 1) ApplyKeycode(encLut, keyCode, 2);
            if (level >= 2) ApplyKeycode(encLut, keyCode, 2);

            keyCode[1] *= 2;
            keyCode[2] /= 2;

            if (level >= 3) ApplyKeycode(encLut, keyCode, 2); //
        }

        public static uint BSwap32(uint val)
        {
            return
                ((val >> 24) & 0x000000FF) |
                ((val >> 8) & 0x0000FF00) |
                ((val << 8) & 0x00FF0000) |
                ((val << 24) & 0xFF000000);
        }
    }
}