using static OptimeGBA.Bits;
using static OptimeGBA.MemoryUtil;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
namespace OptimeGBA
{
    public class CustomSample
    {
        public short[] Data; // In PCM16
        public uint LoopPoint; // In samples
        public uint RepeatMode;

        public CustomSample(short[] data, uint loopPoint, uint repeatMode)
        {
            Data = data;
            LoopPoint = loopPoint;
            RepeatMode = repeatMode;
        }
    }

    public class AudioChannelNds
    {
        // SOUNDxCNT
        public uint Volume;
        public byte VolumeDiv;
        public bool Hold;
        public byte Pan;
        public byte PulseDuty;
        public byte RepeatMode;
        public byte Format;
        public bool Playing;

        public uint SOUNDSAD;
        public uint SOUNDTMR;
        public uint SOUNDPNT;
        public uint SOUNDLEN;

        public uint SamplePos;
        public uint Timer;

        public uint Interval;
        public int CurrentValue;
        public int AdpcmIndex;
        public int AdpcmLoopValue;
        public int AdpcmLoopIndex;
        public uint AdpcmLoopCurrentData;
        public uint CurrentData;

        public bool DebugEnable = true;
        public uint DebugAdpcmSaved;
        public uint DebugAdpcmRestored;

        // Sample Replacement
        public CustomSample CustomSample;
    }

    public class NdsAudio
    {
        Nds Nds;

        public NdsAudio(Nds nds)
        {
            Nds = nds;

            if (Nds.Cartridge.IdString != null && CustomSamples.ContainsKey(Nds.Cartridge.IdString))
            {
                CustomSampleSet = CustomSamples[Nds.Cartridge.IdString];
            }

            for (uint i = 0; i < 16; i++)
            {
                Channels[i] = new AudioChannelNds();
            }

            Sample(0);
        }

        public static Dictionary<string, Dictionary<uint, CustomSample>> CustomSamples = new Dictionary<string, Dictionary<uint, CustomSample>>();

        // Thanks: https://rosettacode.org/wiki/Fast_Fourier_transform#C.23
        /* Performs a Bit Reversal Algorithm on a postive integer 
        * for given number of bits
        * e.g. 011 with 3 bits is reversed to 110 */
        public static int BitReverse(int n, int bits)
        {
            int reversedN = n;
            int count = bits - 1;

            n >>= 1;
            while (n > 0)
            {
                reversedN = (reversedN << 1) | (n & 1);
                count--;
                n >>= 1;
            }

            return ((reversedN << count) & ((1 << bits) - 1));
        }

        /* Uses Cooley-Tukey iterative in-place algorithm with radix-2 DIT case
        * assumes no of points provided are a power of 2 */
        public static void FFT(Complex[] buffer)
        {
            int bits = (int)Math.Log(buffer.Length, 2);
            for (int j = 1; j < buffer.Length; j++)
            {
                int swapPos = BitReverse(j, bits);
                if (swapPos <= j)
                {
                    continue;
                }
                var temp = buffer[j];
                buffer[j] = buffer[swapPos];
                buffer[swapPos] = temp;
            }

            for (int N = 2; N <= buffer.Length; N <<= 1)
            {
                for (int i = 0; i < buffer.Length; i += N)
                {
                    for (int k = 0; k < N / 2; k++)
                    {

                        int evenIndex = i + k;
                        int oddIndex = i + k + (N / 2);
                        var even = buffer[evenIndex];
                        var odd = buffer[oddIndex];

                        double term = -2 * Math.PI * k / (double)N;
                        Complex exp = new Complex(Math.Cos(term), Math.Sin(term)) * odd;

                        buffer[evenIndex] = even + exp;
                        buffer[oddIndex] = even - exp;

                    }
                }
            }
        }

        static NdsAudio()
        {
            // Custom audio file names 
            // <28_bit_addr>-o[comment].wav
            // <28_bit_addr>-l-<loop_point_sample>[comment].wav
            // 28_bit_addr is in hexadecimal
            // loop_point_sample is in decimal
            if (Directory.Exists("samples"))
            {
                Console.WriteLine("Loading custom samples...");

                string[] gameDirs = Directory.GetDirectories("samples");

                foreach (string gameDir in gameDirs)
                {
                    Console.WriteLine(gameDir);
                    // Since Path.GetDirectoryName chops everything after the first path delineator,
                    // use Path.GetFileName instead
                    string gameCode = Path.GetFileName(gameDir);

                    if (gameCode.Length != 4)
                    {
                        continue;
                    }

                    Console.WriteLine(@"Found " + gameCode);

                    Dictionary<uint, CustomSample> gameDictionary = new Dictionary<uint, CustomSample>();
                    CustomSamples.Add(gameCode, gameDictionary);

                    string[] files = Directory.GetFiles(gameDir);
                    foreach (string file in files)
                    {
                        Console.WriteLine(Path.GetFileName(file));

                        string name = Path.GetFileNameWithoutExtension(file);
                        string[] nameArr = name.Split("-");
                        uint addr;
                        if (!uint.TryParse(nameArr[0], NumberStyles.HexNumber, null, out addr))
                        {
                            Console.WriteLine("Error: Error parsing source address in filename");
                            continue;
                        }

                        uint loopPoint = 0;
                        uint repeatMode;
                        if (nameArr[1][0] == 'o')
                        {
                            Console.WriteLine("One-shot");
                            repeatMode = 2;
                        }
                        else if (nameArr[1][0] == 'l')
                        {
                            Console.WriteLine("Loop");
                            repeatMode = 1;

                            if (!uint.TryParse(nameArr[2], out loopPoint))
                            {
                                Console.WriteLine("Error: Error parsing loop point in filename");
                                continue;
                            }
                        }
                        else
                        {
                            Console.WriteLine("Error: Unsupported repeat mode, use \"o\" or \"r\".");
                            continue;
                        }

                        byte[] data = File.ReadAllBytes(file);

                        uint channels = GetUshort(data, 22);
                        uint sampleRate = GetUint(data, 24);
                        uint bitsPerSample = GetUshort(data, 34);
                        uint subchunk2Size = GetUint(data, 40);

                        Console.WriteLine("Sample rate: " + sampleRate);
                        Console.WriteLine("Bit depth: " + sampleRate);
                        if (!(bitsPerSample == 8 || bitsPerSample == 16))
                        {
                            Console.WriteLine("Error: Unsupported bit depth, please use 8 or 16");
                            continue;
                        }

                        Console.WriteLine("Channels: " + sampleRate);
                        if (channels != 1)
                        {
                            Console.WriteLine("Error: Unsupported channel count, please use mono only");
                        }

                        uint bytesPerSample = bitsPerSample / 8;
                        uint sampleCount = subchunk2Size / bytesPerSample;
                        short[] samples = new short[sampleCount];

                        for (int i = 0; i < sampleCount; i++)
                        {
                            if (bytesPerSample == 8)
                            {
                                samples[i] = (short)((data[44 + i] - 0x7F) << 8);
                            }
                            else
                            {
                                samples[i] = (short)GetUshort(data, (uint)(44 + i * 2));
                            }
                        }

                        uint fftSize = sampleCount;

                        // Round up to next power of 2, then cut
                        fftSize--;
                        fftSize |= fftSize >> 1;
                        fftSize |= fftSize >> 2;
                        fftSize |= fftSize >> 4;
                        fftSize |= fftSize >> 8;
                        fftSize |= fftSize >> 16;
                        fftSize++;
                        fftSize >>= 4;

                        Console.WriteLine("FFT size: " + fftSize);

                        Complex[] fftData = new Complex[fftSize]; // Round up

                        for (int i = 0; i < fftSize; i++)
                        {
                            fftData[i] = (double)samples[i] / 32768D;
                        }

                        FFT(fftData);

                        int largestBinIndex = 0;
                        double largestBinMagnitude = 0;
                        for (int i = 1; i < fftData.Length / 2 - 1; i++)
                        {
                            double magnitude = Math.Sqrt(Math.Pow(fftData[i].Real, 2) + Math.Pow(fftData[i].Imaginary, 2));

                            if (magnitude > largestBinMagnitude)
                            {
                                largestBinMagnitude = magnitude;
                                largestBinIndex = i;
                            }
                        }

                        Console.WriteLine("largest bin: " + largestBinIndex);
                        Console.WriteLine("mag: " + largestBinMagnitude);
                        Console.WriteLine("Fundamental hz: " + ((double)largestBinIndex * (double)sampleRate / (double)fftSize));

                        gameDictionary.Add(addr, new CustomSample(samples, loopPoint, repeatMode));
                    }
                }
            }
            else
            {
                Console.WriteLine("No \"samples\" directory present.");
            }
        }

        public AudioChannelNds[] Channels = new AudioChannelNds[16];

        public Dictionary<uint, CustomSample> CustomSampleSet;

        public static double CyclesPerSample = 33513982D / 32768D;
        public double SampleTimer;
        public const uint SampleBufferMax = 256;
        public short[] SampleBuffer = new short[SampleBufferMax];
        public uint SampleBufferPos = 0;

        public static sbyte[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8 };
        public static short[] AdpcmTable = {
            0x0007, 0x0008, 0x0009, 0x000A, 0x000B, 0x000C, 0x000D, 0x000E, 0x0010, 0x0011, 0x0013, 0x0015,
            0x0017, 0x0019, 0x001C, 0x001F, 0x0022, 0x0025, 0x0029, 0x002D, 0x0032, 0x0037, 0x003C, 0x0042,
            0x0049, 0x0050, 0x0058, 0x0061, 0x006B, 0x0076, 0x0082, 0x008F, 0x009D, 0x00AD, 0x00BE, 0x00D1,
            0x00E6, 0x00FD, 0x0117, 0x0133, 0x0151, 0x0173, 0x0198, 0x01C1, 0x01EE, 0x0220, 0x0256, 0x0292,
            0x02D4, 0x031C, 0x036C, 0x03C3, 0x0424, 0x048E, 0x0502, 0x0583, 0x0610, 0x06AB, 0x0756, 0x0812,
            0x08E0, 0x09C3, 0x0ABD, 0x0BD0, 0x0CFF, 0x0E4C, 0x0FBA, 0x114C, 0x1307, 0x14EE, 0x1706, 0x1954,
            0x1BDC, 0x1EA5, 0x21B6, 0x2515, 0x28CA, 0x2CDF, 0x315B, 0x364B, 0x3BB9, 0x41B2, 0x4844, 0x4F7E,
            0x5771, 0x602F, 0x69CE, 0x7462, 0x7FFF
        };

        // SOUNDCNT
        public byte MasterVolume;
        public uint LeftOutputFrom;
        public uint RightOutputFrom;
        public bool Ch1ToMixer;
        public bool Ch3ToMixer;
        public bool MasterEnable;

        // SOUNDBIAS
        public ushort SOUNDBIAS;

        // SNDCAPCNT
        byte SNDCAP0CNT;
        byte SNDCAP1CNT;

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;

            switch (addr)
            {
                case 0x4000500: // SOUNDCNT B0
                    val |= (byte)(MasterVolume & 0x7FU);
                    break;
                case 0x4000501: // SOUNDCNT B1
                    val |= (byte)((LeftOutputFrom & 0b11) << 0);
                    val |= (byte)((RightOutputFrom & 0b11) << 2);
                    if (Ch1ToMixer) val = BitSet(val, 4);
                    if (Ch3ToMixer) val = BitSet(val, 5);
                    if (MasterEnable) val = BitSet(val, 7);
                    break;

                case 0x4000504:
                    return (byte)(SOUNDBIAS >> 0);
                case 0x4000505:
                    return (byte)(SOUNDBIAS >> 8);

                case 0x4000508:
                    return SNDCAP0CNT;
                case 0x4000509:
                    return SNDCAP1CNT;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000500: // SOUNDCNT B0
                    MasterVolume = (byte)(val & 0x7FU);
                    break;
                case 0x4000501: // SOUNDCNT B1
                    LeftOutputFrom = (byte)((val >> 0) & 0b11);
                    RightOutputFrom = (byte)((val >> 2) & 0b11);
                    Ch1ToMixer = BitTest(val, 4);
                    Ch3ToMixer = BitTest(val, 5);
                    MasterEnable = BitTest(val, 7);
                    break;

                case 0x4000504:
                    SOUNDBIAS &= 0xFF00;
                    SOUNDBIAS |= (ushort)(val << 0);
                    break;
                case 0x4000505:
                    SOUNDBIAS &= 0x00FF;
                    SOUNDBIAS |= (ushort)(val << 8);
                    break;

                case 0x4000508:
                    SNDCAP0CNT = val;
                    break;
                case 0x4000509:
                    SNDCAP1CNT = val;
                    break;
            }
        }

        public byte ReadHwio8Channels(uint addr)
        {
            var c = Channels[(addr >> 4) & 0xF];

            byte val = 0;

            switch (addr & 0xF)
            {
                case 0x0:
                    val |= (byte)(c.Volume & 0x7F);
                    break;
                case 0x1:
                    val |= c.VolumeDiv;
                    if (c.Hold) val = BitSet(val, 7);
                    break;
                case 0x2:
                    val |= c.Pan;
                    break;
                case 0x3:
                    val |= c.PulseDuty;
                    val |= (byte)(c.RepeatMode << 3);
                    val |= (byte)(c.Format << 5);
                    if (c.Playing) val = BitSet(val, 7);
                    break;
            }

            return val;
        }

        public void WriteHwio8Channels(uint addr, byte val)
        {
            var c = Channels[(addr >> 4) & 0xF];

            switch (addr & 0xF)
            {
                case 0x0:
                    c.Volume = (byte)(val & 0x7F);
                    break;
                case 0x1:
                    c.VolumeDiv = (byte)(val & 3);
                    c.Hold = BitTest(val, 7);
                    break;
                case 0x2:
                    c.Pan = (byte)(val & 0x7F);
                    break;
                case 0x3:
                    c.PulseDuty = (byte)(val & 7);
                    c.RepeatMode = (byte)((val >> 3) & 3);
                    c.Format = (byte)((val >> 5) & 3);
                    if (!c.Playing && BitTest(val, 7))
                    {
                        StartChannel(c);
                    }
                    c.Playing = BitTest(val, 7);
                    break;

                case 0x4:
                case 0x5:
                case 0x6:
                case 0x7:
                    c.SOUNDSAD = SetByteIn(c.SOUNDSAD, val, addr & 3) & 0x7FFFFFC;

                    if (c.Playing)
                    {
                        StartChannel(c);
                    }
                    break;
                case 0x8:
                case 0x9:
                    c.SOUNDTMR = SetByteIn(c.SOUNDTMR, val, addr & 1);
                    c.Interval = 2 * (0x10000 - c.SOUNDTMR);
                    break;
                case 0xA:
                case 0xB:
                    c.SOUNDPNT = SetByteIn(c.SOUNDPNT, val, addr & 1);
                    break;
                case 0xC:
                case 0xD:
                case 0xE:
                case 0xF:
                    c.SOUNDLEN = SetByteIn(c.SOUNDLEN, val, addr & 3) & 0x3FFFFF;
                    break;
            }
        }

        public void StartChannel(AudioChannelNds c)
        {
            c.SamplePos = 0;
            c.Timer = 0;
            c.CurrentValue = 0;

            if (CustomSampleSet != null)
            {
                // Exclude PSGs from sample replacement
                if (c.Format != 3)
                {
                    c.CustomSample = CustomSampleSet.GetValueOrDefault(c.SOUNDSAD);
                }
                else
                {
                    c.CustomSample = null;
                }
                // Console.WriteLine($"Custom sample: addr:{Util.Hex(c.SOUNDSAD, 7)}");
            }
        }

        uint x = 0;
        public void Sample(long cyclesLate)
        {
            SampleTimer += CyclesPerSample;
            uint cyclesThisSample = (uint)SampleTimer;
            SampleTimer -= cyclesThisSample;

            long left = 0;
            long right = 0;

            for (uint i = 0; i < 16; i++)
            {
                var c = Channels[i];

                if (c.Playing)
                {
                    c.Timer += cyclesThisSample;
                    while (c.Timer >= c.Interval && c.Interval != 0)
                    {
                        c.Timer -= c.Interval;

                        if (c.CustomSample == null)
                        {
                            // Advance sample
                            switch (c.Format)
                            {
                                case 0: // PCM8
                                    if (c.SamplePos >= (c.SOUNDPNT + c.SOUNDLEN) * 4)
                                    {
                                        switch (c.RepeatMode)
                                        {
                                            case 1: // Infinite 
                                                c.SamplePos = c.SOUNDPNT * 4;
                                                break;
                                            case 2: // One-shot
                                                c.Playing = false;
                                                if (!c.Hold)
                                                {
                                                    c.CurrentValue = 0;
                                                }
                                                break;
                                        }
                                    }

                                    if ((c.SamplePos & 3) == 0)
                                    {
                                        c.CurrentData = Nds.Mem7.Read32(c.SOUNDSAD + c.SamplePos);
                                    }

                                    c.CurrentValue = (short)((byte)c.CurrentData << 8);
                                    c.CurrentData >>= 8;

                                    c.SamplePos++;
                                    break;
                                case 1: // PCM16
                                    if (c.SamplePos >= (c.SOUNDPNT + c.SOUNDLEN) * 2)
                                    {
                                        switch (c.RepeatMode)
                                        {
                                            case 1: // Infinite 
                                                c.SamplePos = c.SOUNDPNT * 2;
                                                break;
                                            case 2: // One-shot
                                                c.Playing = false;
                                                if (!c.Hold)
                                                {
                                                    c.CurrentValue = 0;
                                                }
                                                break;
                                        }
                                    }

                                    if ((c.SamplePos & 1) == 0)
                                    {
                                        c.CurrentData = Nds.Mem7.Read32(c.SOUNDSAD + c.SamplePos * 2);
                                    }

                                    c.CurrentValue = (short)c.CurrentData;
                                    c.CurrentData >>= 16;

                                    c.SamplePos++;
                                    break;
                                case 2: // IMA-ADPCM
                                    if ((c.SamplePos & 7) == 0)
                                    {
                                        c.CurrentData = Nds.Mem7.Read32(c.SOUNDSAD + c.SamplePos / 2);
                                        // ADPCM header
                                        if (c.SamplePos == 0)
                                        {
                                            c.CurrentValue = (short)c.CurrentData;
                                            // Console.WriteLine("header set " + x++);
                                            // Console.WriteLine("interval: " + Util.Hex(c.Interval, 8));
                                            c.AdpcmIndex = Math.Clamp((int)(c.CurrentData >> 16), 0, 88);
                                        }
                                        // Console.WriteLine("addr: " + Util.Hex(c.Source, 8));
                                    }
                                    if (c.SamplePos > 7)
                                    {
                                        // End of sound, loop or stop
                                        if (c.SamplePos >= (c.SOUNDPNT + c.SOUNDLEN) * 8)
                                        {
                                            switch (c.RepeatMode)
                                            {
                                                case 1: // Infinite 
                                                    c.SamplePos = c.SOUNDPNT * 8;
                                                    c.CurrentValue = c.AdpcmLoopValue;
                                                    c.AdpcmIndex = c.AdpcmLoopIndex;
                                                    c.CurrentData = c.AdpcmLoopCurrentData;
                                                    // Console.WriteLine($"Ch{i}: Loaded at " + c.SampleNum);

                                                    c.DebugAdpcmRestored = c.SamplePos;
                                                    break;
                                                case 2: // One-shot
                                                    c.Playing = false;
                                                    if (!c.Hold)
                                                    {
                                                        c.CurrentValue = 0;
                                                    }
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            byte data = (byte)(c.CurrentData & 0xF);

                                            short tableVal = AdpcmTable[c.AdpcmIndex];
                                            int diff = tableVal / 8;
                                            if ((data & 1) != 0) diff += tableVal / 4;
                                            if ((data & 2) != 0) diff += tableVal / 2;
                                            if ((data & 4) != 0) diff += tableVal / 1;

                                            if ((data & 8) == 8)
                                            {
                                                c.CurrentValue = Math.Max((int)c.CurrentValue - diff, -0x7FFF);
                                            }
                                            else
                                            {
                                                c.CurrentValue = Math.Min((int)c.CurrentValue + diff, 0x7FFF);
                                            }
                                            c.AdpcmIndex = Math.Clamp(c.AdpcmIndex + IndexTable[data & 7], 0, 88);

                                            c.CurrentData >>= 4;

                                            // Save value and ADPCM table index for loop
                                            if (c.SamplePos == c.SOUNDPNT * 8)
                                            {
                                                c.AdpcmLoopValue = c.CurrentValue;
                                                c.AdpcmLoopIndex = c.AdpcmIndex;
                                                c.AdpcmLoopCurrentData = c.CurrentData;

                                                c.DebugAdpcmSaved = c.SamplePos;
                                                // Console.WriteLine($"Ch{i}: Saved at " + c.SampleNum);
                                            }
                                        }
                                    }
                                    c.SamplePos++;
                                    break;
                                case 3: // Pulse / Noise
                                    if (((c.SamplePos ^ 7) & 7) <= c.PulseDuty)
                                    {
                                        c.CurrentValue = -0x7FFF;
                                    }
                                    else
                                    {
                                        c.CurrentValue = 0x7FFF;
                                    }
                                    c.SamplePos++;
                                    break;
                            }
                        }
                        else
                        {
                            c.CurrentValue = c.CustomSample.Data[c.SamplePos];
                            c.SamplePos++;

                            if (c.SamplePos >= c.CustomSample.Data.Length)
                            {
                                switch (c.RepeatMode)
                                {
                                    case 1: // Infinite 
                                        c.SamplePos = c.CustomSample.LoopPoint;
                                        break;
                                    case 2: // One-shot
                                        c.Playing = false;
                                        if (!c.Hold)
                                        {
                                            c.CurrentValue = 0;
                                        }
                                        break;
                                }
                            }
                        }
                    }

                    if (c.DebugEnable)
                    {
                        uint effectiveVol = c.Volume;
                        if (effectiveVol == 127) effectiveVol++;
                        left += ((((long)c.CurrentValue * (16 >> c.VolumeDiv)) * effectiveVol) * (127 - c.Pan)) >> 10;
                        right += ((((long)c.CurrentValue * (16 >> c.VolumeDiv)) * effectiveVol) * c.Pan) >> 10;
                    }
                }
            }

            // 28 bits now, after mixing all channels
            // add master volume to get 35 bits
            // add 
            // strip 19 to get 16 bits for our short output
            uint effectiveMasterVol = MasterVolume;
            if (effectiveMasterVol == 127) effectiveMasterVol++;
            SampleBuffer[SampleBufferPos++] = (short)((left * effectiveMasterVol) >> 16);
            SampleBuffer[SampleBufferPos++] = (short)((right * effectiveMasterVol) >> 16);

            if (SampleBufferPos >= SampleBufferMax)
            {
                SampleBufferPos = 0;

                Nds.Provider.AudioCallback(SampleBuffer);
            }


            Nds.Scheduler.AddEventRelative(SchedulerId.ApuSample, (cyclesThisSample - cyclesLate) + 1, Sample);
        }
    }
}