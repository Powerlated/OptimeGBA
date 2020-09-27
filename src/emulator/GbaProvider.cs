public delegate void AudioCallback(short[] stereo16BitInterleavedData);

namespace OptimeGBA
{
    public class GbaProvider
    {
        public bool OutputAudio = true;
        public bool BootBios = false;

        public byte[] Bios;
        public byte[] Rom;
        public AudioCallback AudioCallback;

        public string SavPath;

        public GbaProvider(byte[] bios, byte[] rom, string romPath, AudioCallback audioCallback)
        {
            Bios = bios;
            Rom = rom;
            AudioCallback = audioCallback;
            SavPath = romPath;
        }
    }
}