// Inspired by Blargg's blip-buf

using System;

namespace OptimeGBA
{
    public class BlipBuf
    {
        const int KERNEL_RESOLUTION = 1024;

        float[] Kernel;
        int KernelSize = 0;

        float[] ChannelValsL;
        float[] ChannelValsR;
        double[] ChannelSample;
        double[] ChannelRealSample;

        float[] BufferL;
        float[] BufferR;

        int BufferPos = 0;
        int BufferSize = 0;

        public float CurrentValL = 0;
        public float CurrentValR = 0;

        double CurrentSampleInPos = 0;
        double CurrentSampleOutPos = 0;

        public BlipBuf(int kernelSize, bool normalize, int channels)
        {
            ChannelValsL = new float[channels];
            ChannelValsR = new float[channels];
            ChannelSample = new double[channels];
            ChannelRealSample = new double[channels];

            BufferSize = 32768;
            BufferL = new float[BufferSize];
            BufferR = new float[BufferSize];

            SetKernelSize(kernelSize, normalize, true);
        }

        public void SetKernelSize(int kernelSize, bool normalize, bool enabled)
        {
            Kernel = new float[kernelSize * KERNEL_RESOLUTION];
            KernelSize = kernelSize;

            if ((kernelSize & (kernelSize - 1)) != 0)
            {
                throw new ArgumentException("Kernel size not power of 2:" + kernelSize);
            }

            for (int i = 0; i < KERNEL_RESOLUTION; i++)
            {
                float sum = 0;
                for (int j = 0; j < kernelSize; j++)
                {
                    if (enabled)
                    {
                        float x = j - kernelSize / 2F;
                        x += (KERNEL_RESOLUTION - i - 1) / (float)KERNEL_RESOLUTION;
                        x *= (float)Math.PI;

                        float sinc = (float)Math.Sin(x) / x;
                        float lanzcosWindow = (float)Math.Sin((float)x / kernelSize) / ((float)x / kernelSize);

                        if (x == 0)
                        {
                            Kernel[i * kernelSize + j] = 1;
                        }
                        else
                        {
                            Kernel[i * kernelSize + j] = sinc * lanzcosWindow;
                        }

                        sum += Kernel[i * kernelSize + j];
                    }
                    else
                    {
                        if (j == kernelSize / 2)
                        {
                            Kernel[i * kernelSize + j] = 1;
                        }
                        else
                        {
                            Kernel[i * kernelSize + j] = 0;
                        }
                    }
                }

                if (normalize && enabled)
                {
                    for (int j = 0; j < kernelSize; j++)
                    {
                        Kernel[i * kernelSize + j] /= sum;
                    }
                }
            }
        }

        public void Reset()
        {
            BufferPos = 0;
            CurrentValL = 0;
            CurrentValR = 0;
            for (int i = 0; i < BufferSize; i++)
            {
                BufferL[i] = 0;
                BufferR[i] = 0;
            }
        }

        public void SetValue(int channel, double sample, float valL, float valR)
        {
            // Tracking to allow submitting value for different channels out of order 
            double realSample = sample;
            double dist = sample - ChannelRealSample[channel];
            sample = ChannelSample[channel] + dist;

            if (sample >= CurrentSampleInPos)
            {
                CurrentSampleInPos = sample;
            }
            
            if (sample < CurrentSampleOutPos)
            {
                Console.Error.WriteLine("Tried to set amplitude backward in time!");
                Console.WriteLine(System.Environment.StackTrace);
            }

            ChannelSample[channel] = sample;
            ChannelRealSample[channel] = realSample;

            if (valL != ChannelValsL[channel] || valR != ChannelValsR[channel])
            {
                float diffL = valL - ChannelValsL[channel];
                float diffR = valR - ChannelValsR[channel];

                int subsamplePos = (int)Math.Floor((sample % 1) * KERNEL_RESOLUTION);


                // Add our bandlimited impulse to the difference buffer
                int kBufPos = (BufferPos + (int)(Math.Floor(sample) - CurrentSampleOutPos)) % BufferSize;
                for (int i = 0; i < KernelSize; i++)
                {
                    float kernelVal = Kernel[KernelSize * subsamplePos + i];
                    BufferL[kBufPos] += kernelVal * diffL;
                    BufferR[kBufPos] += kernelVal * diffR;
                    kBufPos = (kBufPos + 1) % BufferSize;
                }
            }

            ChannelValsL[channel] = valL;
            ChannelValsR[channel] = valR;
        }

        public void ReadOutSample()
        {
            CurrentValL += BufferL[BufferPos];
            CurrentValR += BufferR[BufferPos];
            BufferL[BufferPos] = 0;
            BufferR[BufferPos] = 0;
            BufferPos = (BufferPos + 1) % BufferSize;
            CurrentSampleOutPos++;
        }
    }
}