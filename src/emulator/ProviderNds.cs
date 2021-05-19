using System.Text;

namespace OptimeGBA
{
    public sealed class ProviderNds : Provider
    {
        public bool BootBios = false;

        public byte[] Bios;
        public byte[] Rom;

        public string RomId;

        public ProviderNds(byte[] bios, byte[] rom, string savPath, AudioCallback audioCallback)
        {
            Bios = bios;
            Rom = rom;
            AudioCallback = audioCallback;
            SavPath = savPath;
        }
    }
}