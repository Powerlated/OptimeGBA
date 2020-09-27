using System;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public enum FlashState
    {
        InitialState,
        PreCommand0,
        PreCommand1,

        FullErase,
        EraseSector,
        PrepareWriteByte,
        SetBank,
    }

    public enum FlashStateSecondary
    {
        Ready,
        PrepareEraseCommand
    }

    public class Flash : SaveProvider
    {
        FlashState State = FlashState.InitialState;
        FlashStateSecondary StateSecondary = FlashStateSecondary.Ready;

        bool IdentificationMode = false;
        bool Bank1 = false;
        bool PrepareSetBank = false;
        bool PrepareWrite = false;

        byte[] Memory = new byte[131072];

        public override byte Read8(uint addr)
        {
            if (IdentificationMode)
            {
                // Return Sanyo IDs in identification mode
                switch (addr)
                {
                    case 0xE000000: return 0x62;
                    case 0xE000001: return 0x13;
                }
            }

            addr -= 0xE000000;
            if (Bank1) addr += 0x10000;
            if (addr < Memory.Length)
            {
                return Memory[addr];
            }

            return 0;
        }

        public override void Write8(uint addr, byte val)
        {
            if (PrepareSetBank)
            {
                Bank1 = (val & 1) != 0 ? true : false;
                PrepareSetBank = false;
            }

            if (PrepareWrite)
            {
                addr -= 0xE000000;
                if (Bank1) addr += 0x10000;
                if (addr < Memory.Length)
                {
                    // Writes can only clear bits
                    Memory[addr] &= val;
                    Dirty = true;
                }
                PrepareWrite = false;
            }
            else
            {
                switch (State)
                {
                    case FlashState.InitialState:
                        if (addr == 0xE005555 && val == 0xAA)
                        {
                            State = FlashState.PreCommand0;
                        }
                        break;
                    case FlashState.PreCommand0:
                        if (addr == 0xE002AAA && val == 0x55)
                        {
                            State = FlashState.PreCommand1;
                        }
                        break;
                    case FlashState.PreCommand1:
                        switch (StateSecondary)
                        {
                            case FlashStateSecondary.Ready:
                                if (addr == 0xE005555)
                                {
                                    switch (val)
                                    {
                                        case 0x90:
                                            IdentificationMode = true;
                                            break;
                                        case 0xF0:
                                            IdentificationMode = false;
                                            break;
                                        case 0x80:
                                            StateSecondary = FlashStateSecondary.PrepareEraseCommand;
                                            break;
                                        case 0xB0:
                                            PrepareSetBank = true;
                                            break;
                                        case 0xA0:
                                            PrepareWrite = true;
                                            break;
                                    }
                                }
                                break;
                            case FlashStateSecondary.PrepareEraseCommand:
                                switch (val)
                                {
                                    // Erase everything
                                    case 0x10:
                                        if (addr == 0xE005555)
                                        {
                                            for (uint i = 0; i < Memory.Length; i++)
                                            {
                                                Memory[i] = 0xFF;
                                            }
                                            Dirty = true;
                                        }
                                        StateSecondary = FlashStateSecondary.Ready;
                                        break;
                                    // Erase 4 KB
                                    case 0x30:
                                        uint page = addr & 0xF000;
                                        if (Bank1) page += 0x10000;
                                        for (uint i = 0; i < 0x1000; i++)
                                        {
                                            if (i < Memory.Length)
                                            {
                                                Memory[page + i] = 0xFF;
                                            }
                                        }
                                        Dirty = true;
                                        StateSecondary = FlashStateSecondary.Ready;
                                        break;
                                }
                                break;
                        }
                        State = FlashState.InitialState;
                        break;
                }
            }
        }

        public override byte[] GetSave()
        {
            return Memory;
        }

        public override void LoadSave(byte[] save)
        {
            save.CopyTo(Memory, 0);
        }
    }
}