using System;
using NAudio.Dsp;
using static OptimeGBA.CoreUtil;

namespace OptimeGBA
{

    public class SoundgoodizerFilterChannel
    {
        // Each biquad filter has a slope of 12db/oct so 2 biquads chained gets us 24db/oct
        BiQuadFilter[] LowFilters = new BiQuadFilter[2];
        BiQuadFilter[] MidFilters = new BiQuadFilter[4];
        BiQuadFilter[] HighFilters = new BiQuadFilter[2];

        public float OutLow = 0;
        public float OutMid = 0;
        public float OutHigh = 0;

        public bool DbPerOct24;

        public SoundgoodizerFilterChannel(bool dbPerOct24, float sampleRate, float lowHz, float highHz)
        {
            DbPerOct24 = dbPerOct24;

            // q = 1/sqrt(2) maximally flat "butterworth" filter
            float q = 1F/(float)Math.Sqrt(2);
            LowFilters[0] = BiQuadFilter.LowPassFilter(sampleRate, lowHz, q);
            LowFilters[1] = BiQuadFilter.LowPassFilter(sampleRate, lowHz, q);

            MidFilters[0] = BiQuadFilter.HighPassFilter(sampleRate, lowHz, q);
            MidFilters[1] = BiQuadFilter.LowPassFilter(sampleRate, highHz, q);
            MidFilters[2] = BiQuadFilter.HighPassFilter(sampleRate, lowHz, q);
            MidFilters[3] = BiQuadFilter.LowPassFilter(sampleRate, highHz, q);

            HighFilters[0] = BiQuadFilter.HighPassFilter(sampleRate, highHz, q);
            HighFilters[1] = BiQuadFilter.HighPassFilter(sampleRate, highHz, q);
        }

        public void ChangeFilterParams(bool dbPerOct24, float sampleRate, float lowHz, float highHz)
        {
            DbPerOct24 = dbPerOct24;

            float q = 1F/(float)Math.Sqrt(2);
            LowFilters[0].SetLowPassFilter(sampleRate, lowHz, q);
            LowFilters[1].SetLowPassFilter(sampleRate, lowHz, q);

            MidFilters[0].SetHighPassFilter(sampleRate, lowHz, q);
            MidFilters[1].SetLowPassFilter(sampleRate, highHz, q);
            MidFilters[2].SetHighPassFilter(sampleRate, lowHz, q);
            MidFilters[3].SetLowPassFilter(sampleRate, highHz, q);

            HighFilters[0].SetHighPassFilter(sampleRate, highHz, q);
            HighFilters[1].SetHighPassFilter(sampleRate, highHz, q);
        }

        public void Process(float inVal)
        {
            OutLow = inVal;
            OutMid = inVal;
            OutHigh = inVal;

            for (int i = 0; i < (DbPerOct24 ? 2 : 1); i++)
            {
                OutLow = LowFilters[i].Transform(OutLow);
            }

            for (int i = 0; i < (DbPerOct24 ? 4 : 2); i++)
            {
                OutMid = MidFilters[i].Transform(OutMid);
            }

            for (int i = 0; i < (DbPerOct24 ? 2 : 1); i++)
            {
                OutHigh = HighFilters[i].Transform(OutHigh);
            }
        }
    }
    public class Soundgoodizer
    {
        public float MixLevel = 0.6F;

        public SoundgoodizerFilterChannel L;
        public SoundgoodizerFilterChannel R;

        public float OutL = 0;
        public float OutR = 0;

        SimpleCompressor CompressorLow;
        SimpleCompressor CompressorMid;
        SimpleCompressor CompressorHigh;
        SimpleCompressor CompressorMaster;

        public float PreGainLow = 1.78F;
        public float PreGainMid = 2.09F;
        public float PreGainHigh = 2.20F;
        public float PreGainMaster = 1;

        public float PostGainLow = 1.91F;
        public float PostGainMid = 1.00F;
        public float PostGainHigh = 1.40F;

        public bool DbPerOct24;
        public float SampleRate;
        public float LowHz;
        public float HighHz;

        // Default filter cutoffs based on Soundgoodizer Preset A from FL Studio
        public Soundgoodizer(float sampleRate) : this(true, sampleRate, 200, 3000) { }

        public Soundgoodizer(bool dbPerOct24, float sampleRate, float lowHz, float highHz)
        {
            DbPerOct24 = dbPerOct24;
            SampleRate = sampleRate;
            LowHz = lowHz;
            HighHz = highHz;

            L = new SoundgoodizerFilterChannel(dbPerOct24, sampleRate, lowHz, highHz);
            R = new SoundgoodizerFilterChannel(dbPerOct24, sampleRate, lowHz, highHz);

            // Compressor parameters also taken from Soundgoodizer Preset A            
            CompressorLow = new SimpleCompressor(2.0, 137.48, sampleRate);
            CompressorMid = new SimpleCompressor(2.0, 85.53, sampleRate);
            CompressorHigh = new SimpleCompressor(2.0, 85.53, sampleRate);
            CompressorMaster = new SimpleCompressor(2.0, 85.53, sampleRate);
        }

        public void ChangeFilterParams(bool dbPerOct24, float sampleRate, float lowHz, float highHz)
        {
            DbPerOct24 = dbPerOct24;
            SampleRate = sampleRate;
            LowHz = lowHz;
            HighHz = highHz;

            if (lowHz > highHz) Swap(ref highHz, ref lowHz);
            L.ChangeFilterParams(dbPerOct24, sampleRate, lowHz, highHz);
            R.ChangeFilterParams(dbPerOct24, sampleRate, lowHz, highHz);
        }

        public void Process(float inL, float inR)
        {
            L.Process(inL);
            R.Process(inR);

            // Apply pre-gain (Soundgoodizer Preset A)
            double outLowL = L.OutLow * PreGainLow;
            double outLowR = R.OutLow * PreGainLow;
            double outMidL = L.OutMid * PreGainMid;
            double outMidR = R.OutMid * PreGainMid;
            double outHighL = L.OutHigh * PreGainHigh;
            double outHighR = R.OutHigh * PreGainHigh;

            CompressorLow.Process(ref outLowL, ref outLowR);
            CompressorMid.Process(ref outMidL, ref outMidR);
            CompressorHigh.Process(ref outHighL, ref outHighR);

            // Apply post-gain (Soundgoodizer Preset A)
            outLowL *= PostGainLow;
            outLowR *= PostGainLow;
            outMidL *= PostGainMid;
            outMidR *= PostGainMid;
            outHighL *= PostGainHigh;
            outHighR *= PostGainHigh;

            double outL = outLowL + outMidL + outHighL;
            double outR = outLowR + outMidR + outHighR;

            outL *= PreGainMaster;
            outR *= PreGainMaster;

            CompressorMaster.Process(ref outL, ref outR);

            OutL = (float)(MixLevel * outL + (1 - MixLevel) * inL);
            OutR = (float)(MixLevel * outR + (1 - MixLevel) * inR);
        }
    }
}