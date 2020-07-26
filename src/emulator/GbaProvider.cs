public delegate void AudioCallback(short[] stereo16BitInterleavedData);

namespace OptimeGBA
{
    public class GbaProvider
    {
        public bool OutputAudio = true;
        public byte[] Bios;
        public byte[] Rom;
        public AudioCallback AudioCallback;

        public GbaProvider(byte[] bios, byte[] rom, AudioCallback audioCallback)
        {
            Bios = bios;
            Rom = rom;
            AudioCallback = audioCallback;
        }
    }
}