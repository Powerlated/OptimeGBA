using static Util;
using static OptimeGBA.Bits;
using static OptimeGBA.MemoryUtil;
using System;
using System.Text;
namespace OptimeGBA
{
    public enum SpiEepromState
    {
        Ready,
        ReadStatus,
        WriteStatus,
        SetReadAddress,
        SetWriteAddress,
        ReadData,
        WriteData,
        Done,
    }

    public enum ExternalMemoryType
    {
        None,
        Eeprom,
        Flash,
        FlashWithInfrared
    }

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
        byte[] SecureArea = new byte[0x4000];

        public uint[] EncLutKeycodeLevel1 = new uint[0x412];
        public uint[] EncLutKeycodeLevel2 = new uint[0x412];
        public uint[] EncLutKeycodeLevel3 = new uint[0x412];

        public uint IdCode;
        public string IdString;

        public CartridgeNds(Nds nds)
        {
            Nds = nds;
            Rom = Nds.Provider.Rom;

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

                Span<byte> gameIdSpan = stackalloc byte[4];
                for (int i = 0; i < 4; i++)
                {
                    gameIdSpan[i] = GetByte(Rom, 0x0C + (uint)i);
                }

                IdString = Encoding.ASCII.GetString(gameIdSpan);
                Console.WriteLine("Game ID: " + IdString);
            }

            InitKeycode(EncLutKeycodeLevel1, 1);
            InitKeycode(EncLutKeycodeLevel2, 2);
            InitKeycode(EncLutKeycodeLevel3, 3);

            if (!Nds.Provider.DirectBoot && Rom.Length >= 0x8000 && GetUint(Rom, 0x4000) == 0xE7FFDEFF)
            {
                for (uint i = 0; i < 0x4000; i++)
                {
                    SecureArea[i] = Rom[0x4000 + i];
                }
                Console.WriteLine("Encrypting first 2KB of secure area");
                SetUlong(SecureArea, 0x0000, 0x6A624F7972636E65); // Write in "encryObj"

                // Encrypt first 2K of the secure area with KEY1
                for (uint i = 0x0000; i < 0x0800; i += 8)
                {
                    // Console.WriteLine("Encrypted ulong at " + Hex(i, 16));
                    ulong raw = GetUlong(SecureArea, i);
                    ulong encrypted = Encrypt64(EncLutKeycodeLevel3, raw);
                    SetUlong(SecureArea, i, encrypted);
                    // Console.WriteLine("Before:" + Hex(raw, 16));
                    // Console.WriteLine("After :" + Hex(encrypted, 16));
                }

                Console.WriteLine(Hex(GetUint(SecureArea, 0x0010), 8));

                // Double-encrypt KEY1
                SetUlong(SecureArea, 0x0000, Encrypt64(EncLutKeycodeLevel2, GetUlong(SecureArea, 0x0000)));
            }

            for (uint i = 0; i < ExternalMemory.Length; i++)
            {
                ExternalMemory[i] = 0xFF;
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
        public byte SpiBaudRate;
        public bool SpiChipSelHold = false;
        public bool SpiBusy = false;
        public bool Slot1SpiMode = false;
        public bool TransferReadyIrq = false;
        public bool Slot1Enable = false;

        // Shared External Memory State
        public SpiEepromState SpiEepromState;
        public byte SpiOutData;
        public uint SpiAddress;
        public uint SpiBytesWritten;
        public bool ExternalMemoryWriteEnable;
        // only one game called art academy uses more than 1MB 
        public byte[] ExternalMemory = new byte[1048576];

        // EEPROM state
        public byte EepromWriteProtect;

        // Flash state
        // From Nocash's original DS 
        // TODO: use more realistic flash ID
        byte[] FlashId = new byte[] { 0x20, 0x40, 0x12 };
        public SpiFlashState SpiFlashState;
        public byte FlashIdIndex;

        // ROMCTRL
        byte ROMCTRLB0;
        byte ROMCTRLB1;
        bool ReleaseReset;

        // cart input
        uint InData;

        public byte ReadHwio8(bool fromArm7, uint addr)
        {
            byte val = 0;
            if (fromArm7 == Nds.MemoryControl.Nds7Slot1AccessRights)
            {
                switch (addr)
                {
                    case 0x40001A0: // AUXSPICNT B0
                        val |= SpiBaudRate;
                        if (SpiChipSelHold) val = BitSet(val, 6);
                        if (SpiBusy) val = BitSet(val, 7);
                        // Console.WriteLine("AUXSPICNT B0 read");
                        break;
                    case 0x40001A1: // AUXSPICNT B1
                        if (Slot1SpiMode) val = BitSet(val, 5);
                        if (TransferReadyIrq) val = BitSet(val, 6);
                        if (Slot1Enable) val = BitSet(val, 7);
                        break;

                    case 0x40001A2: // AUXSPIDATA
                        return SpiOutData;

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
                        if (Slot1Enable)
                        {
                            ReadData(fromArm7);
                        }
                        return (byte)(InData >> 0);
                    case 0x4100011:
                        return (byte)(InData >> 8);
                    case 0x4100012:
                        return (byte)(InData >> 16);
                    case 0x4100013:
                        return (byte)(InData >> 24);
                }
            }
            else
            {
                Console.WriteLine((fromArm7 ? "ARM7" : "ARM9") + " tried to read from Slot 1 @ " + Hex(addr, 8));
            }

            return val;
        }

        public void WriteHwio8(bool fromArm7, uint addr, byte val)
        {
            if (fromArm7 == Nds.MemoryControl.Nds7Slot1AccessRights)
            {
                switch (addr)
                {
                    case 0x40001A0: // AUXSPICNT B0
                        SpiBaudRate = (byte)(val & 0b11);
                        SpiChipSelHold = BitTest(val, 6);
                        SpiBusy = BitTest(val, 7);
                        return;
                    case 0x40001A1: // AUXSPICNT B1
                        Slot1SpiMode = BitTest(val, 5);
                        TransferReadyIrq = BitTest(val, 6);
                        Slot1Enable = BitTest(val, 7);
                        return;

                    case 0x40001A2: // AUXSPIDATA
                        SpiTransferTo(val);
                        break;

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

                        if (BitTest(val, 7) && !BusyBit31 && Slot1Enable)
                        {
                            ProcessCommand(fromArm7);
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
                            if (Slot1Enable)
                            {
                                int shiftBy = (int)((7 - (addr & 7)) * 8);
                                PendingCommand &= (ulong)(~(0xFFUL << shiftBy));
                                PendingCommand |= (ulong)val << shiftBy;
                            }
                            return;
                    }
                }
            }
            else
            {
                Console.WriteLine((fromArm7 ? "ARM7" : "ARM9") + " tried to read from Slot 1 @ " + Hex(addr, 8));
            }
        }

        public void ProcessCommand(bool fromArm7)
        {
            ulong cmd = PendingCommand;
            if (Key1Encryption)
            {
                cmd = Decrypt64(EncLutKeycodeLevel2, cmd);
            }

            // Console.WriteLine("Slot 1 CMD: " + Hex(cmd, 16));

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
                // Console.WriteLine("Secure area read pos: " + Hex(DataPos, 8));
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
                // throw new NotImplementedException("Slot 1: unimplemented command " + Hex(cmd, 16));
            }
            // If block size is zero, no transfer will take place, signal end.
            if (TransferLength == 0)
            {
                FinishTransfer();
            }
            else
            {
                ReadyBit23 = true;

                // Trigger Slot 1 DMA
                Nds.Scheduler.AddEventRelative(SchedulerId.None, 0, RepeatCartridgeTransfer);
                // Console.WriteLine("Trigger slot 1 DMA, Dest: " + Hex(Nds.Dma7.Ch[3].DmaDest, 8));
            }
        }

        public void ReadData(bool fromArm7)
        {
            if (!ReadyBit23)
            {
                InData = 0;
                return;
            }

            uint val = 0xFFFFFFFF;

            switch (State)
            {
                case CartridgeState.Dummy: // returns all 1s
                    break;
                case CartridgeState.ReadCartridgeHeader:
                    val = GetUint(Rom, DataPos & 0xFFF);
                    break;
                case CartridgeState.ReadRomChipId1:
                case CartridgeState.ReadRomChipId2:
                case CartridgeState.ReadRomChipId3:
                    val = RomChipId;
                    break;
                case CartridgeState.Key2DataRead:
                    // Console.WriteLine("Key2 data read");
                    if (DataPos < Rom.Length)
                    {
                        if (DataPos < 0x8000)
                        {
                            DataPos = 0x8000 + (DataPos & 0x1FF);
                        }
                        val = GetUint(Rom, DataPos);
                    }
                    break;
                case CartridgeState.SecureAreaRead:
                    val = GetUint(SecureArea, DataPos - 0x4000);
                    // Console.WriteLine("Secure area read: Pos: " + Hex(DataPos, 8) + " Val: " + Hex(val, 4));
                    break;

                default:
                    throw new NotImplementedException("Slot 1: bad state");
            }


            DataPos += 4;
            BytesTransferred += 4;
            if (BytesTransferred >= TransferLength)
            {
                FinishTransfer();
            }
            else
            {
                // TODO: Slot 1 DMA transfers
                Nds.Scheduler.AddEventRelative(SchedulerId.None, 0, RepeatCartridgeTransfer);
            }

            InData = val;
        }

        public void RepeatCartridgeTransfer(long cyclesLate)
        {
            // Console.WriteLine(Hex(Nds.Dma7.Ch[3].DmaDest, 8));
            if (Nds.MemoryControl.Nds7Slot1AccessRights)
            {
                Nds.Dma7.Repeat((byte)DmaStartTimingNds7.Slot1);
            }
            else
            {
                Nds.Dma9.Repeat((byte)DmaStartTimingNds9.Slot1);
            }
        }

        public void FinishTransfer()
        {
            ReadyBit23 = false;
            BusyBit31 = false;

            if (TransferReadyIrq)
            {
                if (Nds.MemoryControl.Nds7Slot1AccessRights)
                {
                    Nds.HwControl7.FlagInterrupt((uint)InterruptNds.Slot1DataTransferComplete);
                }
                else
                {
                    Nds.HwControl9.FlagInterrupt((uint)InterruptNds.Slot1DataTransferComplete);

                }
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

        public void SpiTransferTo(byte val)
        {
            // currently only EEPROM support
            if (Slot1Enable)
            {
                var saveType = ExternalMemoryType.Eeprom;
                // TODO: use a game DB to get memory type
                switch (saveType)
                {
                    case ExternalMemoryType.None:
                        break;
                    case ExternalMemoryType.Eeprom:
                        switch (SpiEepromState)
                        {
                            case SpiEepromState.Ready:
                                switch (val)
                                {
                                    case 0x06: // Write Enable
                                        ExternalMemoryWriteEnable = true;
                                        SpiEepromState = SpiEepromState.Ready;
                                        break;
                                    case 0x04: // Write Disable
                                        ExternalMemoryWriteEnable = false;
                                        SpiEepromState = SpiEepromState.Ready;
                                        break;
                                    case 0x5: // Read Status Register
                                        SpiEepromState = SpiEepromState.ReadStatus;
                                        break;
                                    case 0x1: // Write Status Register
                                        SpiEepromState = SpiEepromState.WriteStatus;
                                        break;
                                    case 0x9F: // Read JEDEC ID (returns 0xFF on EEPROM/FLASH)
                                        SpiOutData = 0xFF;
                                        break;
                                    case 0x3: // Read
                                        SpiEepromState = SpiEepromState.SetReadAddress;
                                        SpiAddress = 0;
                                        SpiBytesWritten = 0;
                                        break;
                                    case 0x2: // Write
                                        SpiEepromState = SpiEepromState.SetWriteAddress;
                                        SpiAddress = 0;
                                        SpiBytesWritten = 0;
                                        break;
                                }
                                break;
                            case SpiEepromState.ReadStatus:
                                byte status = 0;
                                if (ExternalMemoryWriteEnable) status = BitSet(status, 1);
                                status |= (byte)(EepromWriteProtect << 2);
                                SpiOutData = status;
                                break;
                            case SpiEepromState.WriteStatus:
                                ExternalMemoryWriteEnable = BitTest(val, 1);
                                EepromWriteProtect = (byte)((val >> 2) & 0b11);
                                break;
                            case SpiEepromState.SetReadAddress:
                                SpiAddress <<= 8;
                                SpiAddress |= val;
                                if (++SpiBytesWritten == 2)
                                {
                                    SpiEepromState = SpiEepromState.ReadData;
                                }
                                break;
                            case SpiEepromState.ReadData:
                                SpiOutData = ExternalMemory[SpiAddress];
                                SpiAddress++;
                                break;
                            case SpiEepromState.SetWriteAddress:
                                SpiAddress <<= 8;
                                SpiAddress |= val;
                                if (++SpiBytesWritten == 2)
                                {
                                    SpiEepromState = SpiEepromState.WriteData;
                                }
                                break;
                            case SpiEepromState.WriteData:
                                ExternalMemory[SpiAddress] = val;
                                SpiOutData = 0;
                                SpiAddress++;
                                break;
                        }
                        break;
                    case ExternalMemoryType.FlashWithInfrared:
                        switch (SpiFlashState)
                        {
                            case SpiFlashState.TakePrefix:
                                if (val == 0)
                                {
                                    SpiFlashState = SpiFlashState.Ready;
                                    Console.WriteLine("Flash with IR command");
                                }
                                break;
                            case SpiFlashState.Ready:
                                // Console.WriteLine("SPI: Receive command! " + Hex(val, 2));
                                SpiOutData = 0x00;
                                switch (val)
                                {
                                    case 0x06:
                                        ExternalMemoryWriteEnable = true;
                                        break;
                                    case 0x04:
                                        ExternalMemoryWriteEnable = false;
                                        break;
                                    case 0x9F:
                                        SpiFlashState = SpiFlashState.Identification;
                                        SpiAddress = 0;
                                        break;
                                    case 0x03:
                                        SpiFlashState = SpiFlashState.ReceiveAddress;
                                        SpiAddress = 0;
                                        SpiBytesWritten = 0;
                                        break;
                                    case 0x0B:
                                        throw new NotImplementedException("slot1 flash fast read");
                                    case 0x0A:
                                        throw new NotImplementedException("slot1 flash write");
                                    case 0x02:
                                        throw new NotImplementedException("slot1 flash program");
                                    case 0x05: // Identification
                                               // Console.WriteLine("SPI ID");
                                        SpiAddress = 0;
                                        SpiOutData = 0x00;
                                        break;
                                    case 0x00:
                                        break;
                                        // default:
                                        // throw new NotImplementedException("SPI: Unimplemented command: " + Hex(val, 2));
                                }
                                break;
                            case SpiFlashState.ReceiveAddress:
                                // Console.WriteLine("SPI: Address byte write: " + Hex(val, 2));
                                SpiAddress <<= 8;
                                SpiAddress |= val;
                                if (++SpiBytesWritten == 3)
                                {
                                    SpiBytesWritten = 0;
                                    SpiFlashState = SpiFlashState.Reading;
                                    // Console.WriteLine("SPI: Address written: " + Hex(Address, 6));
                                }
                                break;
                            case SpiFlashState.Reading:
                                // Console.WriteLine("SPI: Read from address: " + Hex(Address, 6));
                                // Nds7.Cpu.Error("SPI");
                                SpiOutData = ExternalMemory[SpiAddress];
                                SpiAddress++;
                                SpiAddress &= 0xFFFFFF;
                                break;
                            case SpiFlashState.Identification:
                                SpiOutData = FlashId[SpiAddress];
                                SpiAddress++;
                                SpiAddress %= 3;
                                break;
                        }
                        break;
                }

                if (!SpiChipSelHold)
                {
                    SpiEepromState = SpiEepromState.Ready;
                    SpiFlashState = SpiFlashState.TakePrefix;
                }
            }
        }

        public byte[] GetSave()
        {
            return ExternalMemory;
        }

        public void LoadSave(byte[] sav)
        {
            sav.CopyTo(ExternalMemory, 0);
        }
    }
}