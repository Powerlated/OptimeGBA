using System;
using static Util;

namespace OptimeGBA
{
    public class Cp15
    {
        Nds Nds;

        public Cp15(Nds nds) {
            Nds = nds;
        }
        
        public uint DataTcmSettings;
        public uint InstTcmSettings;

        public void TransferTo(uint opcode1, uint rdVal, uint cRn, uint cRm, uint opcode2)
        {
            // Console.WriteLine($"TO CP15 {opcode1},C{cRn},C{cRm},{opcode2}: {HexN(rdVal, 8)}");

            uint reg = ((cRn & 0xF) << 8) | ((cRm & 0xF) << 4) | (opcode2 & 0x7);

            switch (reg)
            {
                case 0x910:
                    DataTcmSettings = rdVal;
                    Nds.Nds9.Mem.UpdateTcmSettings();
                    break;
                case 0x911:
                    InstTcmSettings = rdVal;
                    Nds.Nds9.Mem.UpdateTcmSettings();
                    break;
            }
        }

        public uint TransferFrom(uint opcode1, uint cRn, uint cRm, uint opcode2)
        {
            uint val = 0;

            uint reg = ((cRn & 0xF) << 8) | ((cRm & 0xF) << 4) | (opcode2 & 0x7);
            switch (reg)
            {
                case 0x910:
                    val = DataTcmSettings;
                    break;
                case 0x911:
                    val = InstTcmSettings;
                    break;
            }

            // Console.WriteLine($"FROM CP15 {opcode1},C{cRn},C{cRm},{opcode2}: {HexN(val, 8)}");

            return val;
        }
    }
}