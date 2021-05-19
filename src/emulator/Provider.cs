namespace OptimeGBA
{
    public delegate void AudioCallback(short[] stereo16BitInterleavedData);
 
    public abstract class Provider {
        public bool OutputAudio = true;
        public AudioCallback AudioCallback;
        public string SavPath;
    }
}