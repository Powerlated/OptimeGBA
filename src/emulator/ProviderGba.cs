using System.Text;


namespace OptimeGBA
{
    public sealed class ProviderGba : Provider
    {
        public bool BootBios = false;

        public byte[] Bios;
        public byte[] Rom;

        public string RomId;

        public ProviderGba(byte[] bios, byte[] rom, string savPath, AudioCallback audioCallback)
        {
            Bios = bios;
            Rom = rom;
            AudioCallback = audioCallback;
            SavPath = savPath;

            if (rom.Length >= 0xAC + 4)
            {
                RomId = Encoding.ASCII.GetString(Rom, 0xAC, 4);
            }
        }
    }
}