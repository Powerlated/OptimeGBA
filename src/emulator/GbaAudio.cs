using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{
    public class GBAAudio
    {
        GBA Gba;
        public GBAAudio(GBA gba)
        {
            Gba = gba;
        }

        public byte[] FifoABuffer = new byte[32];
        public uint FifoAReadPos = 0;
        public uint FifoAWritePos = 0;
        public uint FifoABytes = 0;
        public byte[] FifoBBuffer = new byte[32];
        public uint FifoBReadPos = 0;
        public uint FifoBWritePos = 0;
        public uint FifoBBytes = 0;

        public uint FifoATotalPops = 0;
        public uint FifoBTotalPops = 0;
        public uint FifoAEmptyPops = 0;
        public uint FifoBEmptyPops = 0;

        public byte FifoACurrentByte = 0;
        public byte FifoBCurrentByte = 0;

        uint BiasLevel = 0x100;
        uint AmplitudeRes;

        // SOUNDCNT_H
        uint SoundVolume = 0; // 0-1
        bool DmaSoundAVolume = false; // 2
        bool DmaSoundBVolume = false; // 3

        bool DmaSoundAEnableRight = false; // 8
        bool DmaSoundAEnableLeft = false; // 9
        bool DmaSoundATimerSelect = false; // 10

        bool DmaSoundBEnableRight = false; // 11
        bool DmaSoundBEnableLeft = false; // 12
        bool DmaSoundBTimerSelect = false; // 13

        public byte ReadHwio8(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000082: // SOUNDCNT_H B0
                    val |= (byte)((SoundVolume >> 0) & 0b11); // 0-1
                    if (DmaSoundAVolume) val = BitSet(val, 2); // 2
                    if (DmaSoundBVolume) val = BitSet(val, 3); // 3
                    break;
                case 0x4000083: // SOUNDCNT_H B1
                    if (DmaSoundAEnableRight) val = BitSet(val, 8 - 8); // 8
                    if (DmaSoundAEnableLeft) val = BitSet(val, 9 - 8); // 9
                    if (DmaSoundATimerSelect) val = BitSet(val, 10 - 8); // 10
                    if (DmaSoundBEnableRight) val = BitSet(val, 12 - 8); // 12
                    if (DmaSoundBEnableLeft) val = BitSet(val, 13 - 8); // 13
                    if (DmaSoundBTimerSelect) val = BitSet(val, 14 - 8); // 14
                    break;

                case 0x4000088: // SOUNDBIAS B0
                    val |= (byte)(BiasLevel << 1);
                    break;
                case 0x4000089: // SOUNDBIAS B1
                    val |= (byte)(BiasLevel >> 7);
                    val |= (byte)(AmplitudeRes << 6);
                    break;
            }

            return val;
        }

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000082: // SOUNDCNT_H B0
                    SoundVolume = (uint)(val & 0b11); // 0-1
                    DmaSoundAVolume = BitTest(val, 2); // 2
                    DmaSoundBVolume = BitTest(val, 3); // 3
                    break;
                case 0x4000083: // SOUNDCNT_H B1
                    DmaSoundAEnableRight = BitTest(val, 8 - 8); // 8
                    DmaSoundAEnableLeft = BitTest(val, 9 - 8); // 9
                    DmaSoundATimerSelect = BitTest(val, 10 - 8); // 10
                    if (BitTest(val, 11 - 8)) DmaSoundAReset();
                    DmaSoundBEnableRight = BitTest(val, 12 - 8); // 12
                    DmaSoundBEnableLeft = BitTest(val, 13 - 8); // 13
                    DmaSoundBTimerSelect = BitTest(val, 14 - 8); // 14
                    if (BitTest(val, 15 - 8)) DmaSoundBReset();
                    break;
                case 0x4000088: // SOUNDBIAS B0
                    BiasLevel &= 0b110000000;
                    BiasLevel |= (uint)((val >> 1) & 0b1111111);
                    break;
                case 0x4000089: // SOUNDBIAS B1
                    BiasLevel &= 0b001111111;
                    BiasLevel |= (uint)((val & 0b11) << 7);

                    AmplitudeRes &= 0;
                    AmplitudeRes |= (uint)((val >> 6) & 0b11);
                    break;

                case 0x40000A0:
                    InsertFifoA(val);
                    break;
                case 0x40000A1:
                    InsertFifoA(val);
                    break;
                case 0x40000A2:
                    InsertFifoA(val);
                    break;
                case 0x40000A3:
                    InsertFifoA(val);
                    break;

                case 0x40000A4:
                    InsertFifoB(val);
                    break;
                case 0x40000A5:
                    InsertFifoB(val);
                    break;
                case 0x40000A6:
                    InsertFifoB(val);
                    break;
                case 0x40000A7:
                    InsertFifoB(val);
                    break;

            }
        }

        bool CollectSamples = true;

        const uint SampleMax = 512;
        uint SampleTimer = 0;
        const uint SampleBufferSize = 512;
        short[] SampleBuffer = new short[SampleBufferSize];
        uint SampleBufferPos = 0;
        public void Tick(uint cycles)
        {
            if (CollectSamples)
            {
                SampleTimer += cycles;
                if (SampleTimer >= SampleMax)
                {
                    SampleTimer -= SampleMax;

                    short left = 0;
                    short right = 0;

                    short a = (short)(DmaSoundAVolume ? (sbyte)FifoACurrentByte * 2 : (sbyte)FifoACurrentByte * 1);
                    short b = (short)(DmaSoundBVolume ? (sbyte)FifoBCurrentByte * 2 : (sbyte)FifoBCurrentByte * 1);
                    if (DmaSoundAEnableLeft) left += a;
                    if (DmaSoundBEnableLeft) left += a;
                    if (DmaSoundAEnableRight) right += a;
                    if (DmaSoundBEnableRight) right += a;

                    SampleBuffer[SampleBufferPos + 0] = (short)(left * 64);
                    SampleBuffer[SampleBufferPos + 1] = (short)(right * 64);
                    SampleBufferPos += 2;

                    if (SampleBufferPos > SampleBufferSize - 1)
                    {
                        Gba.Provider.AudioCallback(SampleBuffer);
                        SampleBufferPos = 0;
                    }
                }
            }
        }

        public void InsertFifoA(byte data)
        {
            if (FifoABytes < 32)
            {
                FifoABytes++;
                FifoABuffer[FifoAWritePos++] = data;
                FifoAWritePos &= 31;
            }
        }
        public void InsertFifoB(byte data)
        {
            if (FifoBBytes < 32)
            {
                FifoBBytes++;
                FifoBBuffer[FifoBWritePos++] = data;
                FifoBWritePos &= 31;
            }
        }

        public byte PopFifoA()
        {
            byte data = 0;
            if (FifoABytes > 0)
            {
                FifoABytes--;
                data = FifoABuffer[FifoAReadPos++];
                FifoAReadPos &= 31;
                FifoATotalPops++;
            }
            else
            {
                FifoAEmptyPops++;
            }
            return data;
        }
        public byte PopFifoB()
        {
            byte data = 0;
            if (FifoBBytes > 0)
            {
                FifoBBytes--;
                data = FifoBBuffer[FifoBReadPos++];
                FifoBReadPos &= 31;
                FifoBTotalPops++;
            }
            else
            {
                FifoBEmptyPops++;
            }
            return data;
        }

        public void TimerOverflowFifoA()
        {
            FifoACurrentByte = PopFifoA();
            if (FifoABytes <= 16)
            {
                Gba.Dma.ExecuteFifoA();
            }
        }
        public void TimerOverflowFifoB()
        {
            FifoBCurrentByte = PopFifoB();
            if (FifoBBytes <= 16)
            {
                Gba.Dma.ExecuteFifoB();
            }
        }

        public void DmaSoundAReset()
        {
            FifoACurrentByte = 0;
            FifoABytes = 0;
        }
        public void DmaSoundBReset()
        {
            FifoBCurrentByte = 0;
            FifoBBytes = 0;
        }

        // Called when Timer 0 or 1 overflows.
        public void TimerOverflow(uint timerId)
        {
            if (timerId == 0)
            {
                if (!DmaSoundATimerSelect)
                {
                    TimerOverflowFifoA();
                }
                if (!DmaSoundBTimerSelect)
                {
                    TimerOverflowFifoB();
                }
            }
            else if (timerId == 1)
            {
                if (DmaSoundATimerSelect)
                {
                    TimerOverflowFifoA();
                }
                if (DmaSoundBTimerSelect)
                {
                    TimerOverflowFifoB();
                }
            }
        }
    }
}