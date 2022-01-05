using System.IO;
using System.Collections.Generic;

public class WavWriter {
    public const int BitsPerSample = 16;
    public const int Channels = 2;
    public int SampleRate;

    public int RecordBufferAt;

    public List<short> RecordBuffer = new List<short>();

    public WavWriter(int sampleRate) {
        SampleRate = sampleRate;
    }

    public void AddSample(short valL, short valR) {
        RecordBuffer.Add(valL);
        RecordBuffer.Add(valR);
        RecordBufferAt += 2;
    }

    public void Save(string path) {
        var file = File.OpenWrite(path);

        // RIFF header
        file.WriteByte(0x52);
        file.WriteByte(0x49);
        file.WriteByte(0x46);
        file.WriteByte(0x46);

        int size = RecordBuffer.Count * Channels * (BitsPerSample / 2) - 8 + 44;
        file.WriteByte((byte)(size >> 0));
        file.WriteByte((byte)(size >> 8));
        file.WriteByte((byte)(size >> 16));
        file.WriteByte((byte)(size >> 24));

        // WAVE
        file.WriteByte(0x57);
        file.WriteByte(0x41);
        file.WriteByte(0x56);
        file.WriteByte(0x45);

        // Subchunk1ID "fmt "
        file.WriteByte(0x66);
        file.WriteByte(0x6d);
        file.WriteByte(0x74);
        file.WriteByte(0x20);

        // Subchunk1Size
        file.WriteByte(16);
        file.WriteByte(0);
        file.WriteByte(0);
        file.WriteByte(0);

        // AudioFormat
        file.WriteByte(1);
        file.WriteByte(0);

        // 2 channels
        file.WriteByte(Channels);
        file.WriteByte(0);

        // Sample rate
        file.WriteByte((byte)(SampleRate >> 0));
        file.WriteByte((byte)(SampleRate >> 8));
        file.WriteByte((byte)(SampleRate >> 16));
        file.WriteByte((byte)(SampleRate >> 24));

        // ByteRate
        // SampleRate * NumChannels * BitsPerSample/8
        int byteRate = SampleRate * Channels * (BitsPerSample / 8);
        file.WriteByte((byte)(byteRate >> 0));
        file.WriteByte((byte)(byteRate >> 8));
        file.WriteByte((byte)(byteRate >> 16));
        file.WriteByte((byte)(byteRate >> 24));

        // BlockAlign
        // NumChannels * BitsPerSample / 8
        int blockAlign = Channels * (BitsPerSample / 8);
        file.WriteByte((byte)(blockAlign >> 0));
        file.WriteByte((byte)(blockAlign >> 8));

        // BitsPerSample
        file.WriteByte(16);
        file.WriteByte(0);

        // Subchunk2ID "data"
        file.WriteByte(0x64);
        file.WriteByte(0x61);
        file.WriteByte(0x74);
        file.WriteByte(0x61);

        // NumSamples * NumChannels * BitsPerSample/8
        int subchunk2Size = RecordBufferAt * 2 * (BitsPerSample / 8);
        file.WriteByte((byte)(subchunk2Size >> 0));
        file.WriteByte((byte)(subchunk2Size >> 8));
        file.WriteByte((byte)(subchunk2Size >> 16));
        file.WriteByte((byte)(subchunk2Size >> 24));

        for (int i = 0; i < RecordBufferAt; i++) {
            file.WriteByte((byte)(RecordBuffer[i] >> 0));
            file.WriteByte((byte)(RecordBuffer[i] >> 8));
        }
    }
}