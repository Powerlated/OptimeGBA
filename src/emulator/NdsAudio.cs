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

        public long DebugStartTicks;
    }

    public class NdsAudio
    {
        Nds Nds;

        public NdsAudio(Nds nds)
        {
            Nds = nds;

            for (uint i = 0; i < 16; i++)
            {
                Channels[i] = new AudioChannelNds();
            }

            Sample(0);
        }

        public AudioChannelNds[] Channels = new AudioChannelNds[16];

        public const int SampleRate = 32768;

        public bool EnableBlipBufResampling = true;
        public BlipBuf BlipBuf = new BlipBuf(32, true, 16);

        public bool Record = false;
        public WavWriter WavWriter = new WavWriter(SampleRate);
        public WavWriter WavWriterSinc = new WavWriter(SampleRate);

        public int SampleTimer = 0;

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
                    Console.WriteLine(Util.Hex(Nds.Cpu7.GetCurrentInstrAddr(), 8));
                    // Console.WriteLine(Nds.MemoryControl.SharedRamControl);

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

            c.DebugStartTicks = Nds.Scheduler.CurrentTicks;
        }

        public void Sample(long cyclesLate)
        {
            long left = 0;
            long right = 0;

            for (int i = 0; i < 16; i++)
            {
                var c = Channels[i];

                if (c.Playing)
                {
                    c.Timer += 1024;
                    while (c.Timer >= c.Interval && c.Interval != 0)
                    {
                        c.Timer -= c.Interval;

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

                        if (EnableBlipBufResampling)
                        {
                            long timeTicks = Nds.Scheduler.CurrentTicks - cyclesLate + 1024 - (c.Timer % 1024);
                            double timeSec = (double)timeTicks / 33513982D;
                            double timeSample = (double)timeTicks / (33513982D / (double)SampleRate);

                            if (c.DebugEnable)
                            {
                                uint effectiveVol = c.Volume;
                                if (effectiveVol == 127) effectiveVol++;
                                long leftCh = ((((long)c.CurrentValue * (16 >> c.VolumeDiv)) * effectiveVol) * (127 - c.Pan)) >> 10;
                                long rightCh = ((((long)c.CurrentValue * (16 >> c.VolumeDiv)) * effectiveVol) * c.Pan) >> 10;

                                BlipBuf.SetValue(i, timeSample, leftCh, rightCh);
                            }
                            else
                            {
                                BlipBuf.SetValue(i, timeSample, 0, 0);
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

            // Decimate samples to 32768 hz 
            // Since 33513982 hz / 1024 ≅ 32728.498 hz
            SampleTimer += SampleRate * 1024;
            while (SampleTimer >= 33513982)
            {
                SampleTimer -= 33513982;

                BlipBuf.ReadOutSample();

                // 28 bits now, after mixing all channels
                // add master volume to get 35 bits
                // add 
                // strip 19 to get 16 bits for our short output
                uint effectiveMasterVol = MasterVolume;
                if (effectiveMasterVol == 127) effectiveMasterVol++;

                short leftFinalSinc = (short)(((long)BlipBuf.CurrentValL * effectiveMasterVol) >> 16);
                short rightFinalSinc = (short)(((long)BlipBuf.CurrentValR * effectiveMasterVol) >> 16);

                short leftFinal = (short)((left * effectiveMasterVol) >> 16);
                short rightFinal = (short)((right * effectiveMasterVol) >> 16);

                if (EnableBlipBufResampling)
                {
                    SampleBuffer[SampleBufferPos++] = leftFinalSinc;
                    SampleBuffer[SampleBufferPos++] = rightFinalSinc;
                }
                else
                {
                    SampleBuffer[SampleBufferPos++] = leftFinal;
                    SampleBuffer[SampleBufferPos++] = rightFinal;
                }

                if (Record)
                {
                    WavWriterSinc.AddSample(leftFinalSinc, rightFinalSinc);
                    WavWriter.AddSample(leftFinal, rightFinal);
                }

                if (SampleBufferPos >= SampleBufferMax)
                {
                    SampleBufferPos = 0;

                    Nds.Provider.AudioCallback(SampleBuffer);
                }
            }

            Nds.Scheduler.AddEventRelative(SchedulerId.ApuSample, 1024 - cyclesLate, Sample);
        }
    }
}