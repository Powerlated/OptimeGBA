using System.Text;

namespace OptimeGBA
{
    public sealed class ProviderNds : Provider
    {
        public bool DirectBoot;

        public byte[] Bios7;
        public byte[] Bios9;
        public byte[] Rom;

        public string RomId;

        public ProviderNds(byte[] bios7, byte[] bios9, byte[] rom, string savPath, AudioCallback audioCallback)
        {
            Bios7 = bios7;
            Bios9 = bios9;
            Rom = rom;
            AudioCallback = audioCallback;
            SavPath = savPath;
        }
    }
}