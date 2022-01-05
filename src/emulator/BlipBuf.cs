// Inspired by Blargg's blip-buf

using System;

namespace OptimeGBA
{
    public class BlipBuf
    {
        const int KERNEL_RESOLUTION = 1024;

        double[] Kernel;
        int KernelSize = 0;

        double[] ChannelValsL;
        double[] ChannelValsR;
        double[] ChannelSample;
        double[] ChannelRealSample;

        double[] BufferL;
        double[] BufferR;

        int BufferPos = 0;
        int BufferSize = 0;

        public double CurrentValL = 0;
        public double CurrentValR = 0;

        double CurrentSampleInPos = 0;
        double CurrentSampleOutPos = 0;

        public BlipBuf(int kernelSize, bool normalize, int channels)
        {
            ChannelValsL = new double[channels];
            ChannelValsR = new double[channels];
            ChannelSample = new double[channels];
            ChannelRealSample = new double[channels];

            BufferSize = 32768;
            BufferL = new double[BufferSize];
            BufferR = new double[BufferSize];

            SetKernelSize(kernelSize, normalize, true);
        }

        public void SetKernelSize(int kernelSize, bool normalize, bool enabled)
        {
            Kernel = new double[kernelSize * KERNEL_RESOLUTION];
            KernelSize = kernelSize;

            if ((kernelSize & (kernelSize - 1)) != 0)
            {
                throw new ArgumentException("Kernel size not power of 2:" + kernelSize);
            }

            for (int i = 0; i < KERNEL_RESOLUTION; i++)
            {
                double sum = 0;
                for (int j = 0; j < kernelSize; j++)
                {
                    if (enabled)
                    {
                        double x = j - kernelSize / 2D;
                        x += (KERNEL_RESOLUTION - i - 1) / KERNEL_RESOLUTION;
                        x *= 2;

                        double sinc = Math.Sin(x) / x;
                        double lanzcosWindow = Math.Sin((double)x / kernelSize) / ((double)x / kernelSize);

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

        public void SetValue(int channel, double sample, double valL, double valR)
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
                throw new ArgumentException("Tried to set amplitude backward in time");
            }

            ChannelSample[channel] = sample;
            ChannelRealSample[channel] = realSample;

            if (valL != ChannelValsL[channel] || valR != ChannelValsR[channel])
            {
                double diffL = valL - ChannelValsL[channel];
                double diffR = valR - ChannelValsR[channel];

                int subsamplePos = (int)Math.Floor((sample % 1) * KERNEL_RESOLUTION);

                // Add our bandlimited impulse to the difference buffer
                int kBufPos = (BufferPos + (int)(Math.Floor(sample) - CurrentSampleOutPos)) % BufferSize;
                for (int i = 0; i < KernelSize; i++)
                {
                    double kernelVal = Kernel[KernelSize * subsamplePos + i];
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