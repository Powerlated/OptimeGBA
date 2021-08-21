using System;
using static Util;

namespace OptimeGBA
{
    public class Cp15
    {
        Nds Nds;

        public Cp15(Nds nds)
        {
            Nds = nds;
        }

        public uint ControlRegister;

        public uint DataTcmSettings;
        public uint InstTcmSettings;

        public void TransferTo(uint opcode1, uint rdVal, uint cRn, uint cRm, uint opcode2)
        {
            uint reg = ((cRn & 0xF) << 8) | ((cRm & 0xF) << 4) | (opcode2 & 0x7);

            switch (reg)
            {
                case 0x100:
                    ControlRegister = rdVal;
                    ControlRegister |= 0b00000000000000000000000001111000;
                    ControlRegister &= 0b00000000000011111111000010000101;
                    Nds.Mem9.UpdateTcmSettings();
                    break;

                case 0x704:
                case 0x782:
                    Nds.Cpu9.Halted = true;
                    break;

                case 0x910:
                    DataTcmSettings = rdVal;
                    Nds.Mem9.UpdateTcmSettings();
                    break;
                case 0x911:
                    InstTcmSettings = rdVal;
                    Nds.Mem9.UpdateTcmSettings();
                    break;

                default:
                    // Console.WriteLine($"UNIMPLEMENTED TO CP15 {opcode1},C{cRn},C{cRm},{opcode2}: {HexN(rdVal, 8)}");
                    break;

            }
        }

        public uint TransferFrom(uint opcode1, uint cRn, uint cRm, uint opcode2)
        {
            uint val = 0;

            uint reg = ((cRn & 0xF) << 8) | ((cRm & 0xF) << 4) | (opcode2 & 0x7);
            switch (reg)
            {
                case 0x000: // ID register
                    val = 0x41059461;
                    break;
                case 0x001:
                    val = 0x0F0D2112;
                    break;
                case 0x100:
                    val = ControlRegister;
                    break;
                case 0x910:
                    val = DataTcmSettings;
                    break;
                case 0x911:
                    val = InstTcmSettings;
                    break;

                default:
                    Console.WriteLine($"UNIMPLEMENTED FROM CP15 {opcode1},C{cRn},C{cRm},{opcode2}");
                    break;
            }


            return val;
        }
    }
}