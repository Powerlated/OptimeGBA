public delegate void AudioCallback(short[] stereo16BitInterleavedData);

namespace OptimeGBA
{
    public class GbaProvider
    {
        public byte[] Bios = new byte[16384];
        public byte[] Rom = new byte[33554432];

        public AudioCallback AudioCallback;
        public bool OutputAudio = true;

        public GbaProvider(byte[] bios, byte[] rom, AudioCallback audioCallback)
        {
            bios.CopyTo(Bios, 0);
            rom.CopyTo(Rom, 0);
            AudioCallback = audioCallback;
        }
    }
}