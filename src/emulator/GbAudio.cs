using System;
using System.Runtime.InteropServices;
using System.IO;
using static SDL2.SDL;
using OptimeGBA;
using static OptimeGBA.Bits;
namespace OptimeGBA
{
    public sealed class GbAudio
    {
        public const int OUTPUT_SAMPLE_RATE = 32768;

        public static float[] SIN_LUT = GenerateSineLut();
        public static readonly float[] PULSE_DUTY_VALUE_ARRAY = new float[] { 0.125F, 0.25F, 0.5F, 0.75F };

        static float[] GenerateSineLut()
        {
            int entries = 65536;
            float xPerEntry = ((float)Math.PI * 2) / entries;
            float[] array = new float[entries];

            float x = 0;
            for (int i = 0; i < entries; i++)
            {
                array[i] = (float)Math.Sin(x);
                x += xPerEntry;
            }

            return array;
        }

        public static byte[] SEVEN_BIT_NOISE = GenerateNoiseBuffer(true);
        public static byte[] FIFTEEN_BIT_NOISE = GenerateNoiseBuffer(false);
        public static float[] DAC_TABLE = GenerateDACTable();
        public static int SAMPLE_RATE = 32768; // Digital mixing rate is 32768 Hz on GBA
        public static int SAMPLE_TIME_MAX = 4194304 / SAMPLE_RATE;

        readonly double CAPACITOR_FACTOR = Math.Pow(0.999958, (4194304 / SAMPLE_RATE)); // DMG

        public static float[] GenerateDACTable()
        {
            float[] table = new float[32];
            for (int i = 0; i < 16; i++)
            {
                table[i] = ((i / 15) * 2) - 1;
            }
            return table;
        }

        public static byte[] GenerateNoiseBuffer(bool putBitBack)
        {
            uint seed = 0xFF;

            byte[] waveTable = new byte[32768];
            for (int i = 0; i < 32768; i++)
            {
                waveTable[i] = (byte)((seed & 1) ^ 1);

                int bit = (int)((seed) ^ (seed >> 1));
                bit &= 1;

                seed = (uint)(seed >> 1) | (uint)(bit << 14);

                if (putBitBack == true)
                {
                    seed &= ~BIT_7;
                    seed |= (uint)(bit << 6);
                }
            }
            return waveTable;
        }


        public short Out1;
        public short Out2;

        readonly static uint[][] PULSE_DUTY = new uint[][] {
            new uint[] {1, 1, 1, 1, 1, 1, 1, 0},
            new uint[] {0, 1, 1, 1, 1, 1, 1, 0},
            new uint[] {0, 1, 1, 1, 1, 0, 0, 0},
            new uint[] {1, 0, 0, 0, 0, 0, 0, 1},
        };

        uint Pulse1_pos;
        uint Pulse1_width = 2;
        uint Pulse1_period = 8192;
        int Pulse1_timer;

        public bool enabled = false;

        int ticksEnvelopePulse1 = 0;
        int ticksEnvelopePulse2 = 0;
        int ticksEnvelopeNoise = 0;

        int clockPulse1FreqSweep = 0;
        bool freqSweepEnabled = false;

        int frameSequencerStep = 0;

        byte[] soundRegisters = new byte[65536];

        void advanceFrameSequencer()
        {
            if (this.enabled)
            {
                // 512Hz Frame Sequencer
                switch (this.frameSequencerStep)
                {
                    case 0:
                    case 4:
                        this.frameSequencerLength();
                        this.update();
                        break;
                    case 2:
                    case 6:
                        this.frameSequencerLength();
                        this.frameSequencerFrequencySweep();
                        this.update();
                        break;
                    case 7:
                        this.frameSequencerVolumeEnvelope();
                        this.update();
                        break;
                    default:
                        break;
                }

                this.frameSequencerStep++; this.frameSequencerStep &= 0b111;
            }
        }

        // #region Channel params
        public bool pulse1_enabled = false;
        public int pulse1_width = 3;
        public bool pulse1_dacEnabled = false;
        public bool pulse1_lengthEnable = false;
        public int pulse1_lengthCounter = 0;
        public int pulse1_frequencyUpper = 0;
        public int pulse1_frequencyLower = 0;
        public int pulse1_volume = 0;
        public bool pulse1_volumeEnvelopeUp = false;
        public int pulse1_volumeEnvelopeSweep = 4;
        public int pulse1_volumeEnvelopeStart = 0;
        public bool pulse1_outputLeft = false;
        public bool pulse1_outputRight = false;
        public int pulse1_freqSweepPeriod = 0;
        public bool pulse1_freqSweepUp = false;
        public int pulse1_freqSweepShift = 0;
        public bool pulse1_updated = true;
        void pulse1_trigger()
        {
            if (this.pulse1_lengthCounter == 0 || this.pulse1_lengthEnable == false)
            {
                this.pulse1_lengthCounter = 64;
            }
            this.pulse1_volume = this.pulse1_volumeEnvelopeStart;
            if (this.pulse1_dacEnabled)
            {
                this.pulse1_enabled = true;
            }
            this.clockPulse1FreqSweep = 0;

            this.freqSweepEnabled = this.pulse1_freqSweepShift != 0 || this.pulse1_freqSweepPeriod != 0;
            this.reloadPulse1Period();
            this.updatePulse1Val();
            this.pulse1SynthTime = 0;
        }
        public float pulse1_getFrequencyHz()
        {
            float frequency = (this.pulse1_frequencyUpper << 8) | this.pulse1_frequencyLower;
            return 131072 / (2048 - frequency);
        }

        public bool pulse2_enabled = false;
        public int pulse2_width = 3;
        public bool pulse2_dacEnabled = false;
        public bool pulse2_lengthEnable = false;
        public int pulse2_lengthCounter = 0;
        public int pulse2_frequencyUpper = 0;
        public int pulse2_frequencyLower = 0;
        public int pulse2_volume = 0;
        public bool pulse2_volumeEnvelopeUp = false;
        public int pulse2_volumeEnvelopeSweep = 4;
        public int pulse2_volumeEnvelopeStart = 0;
        public bool pulse2_outputLeft = false;
        public bool pulse2_outputRight = false;
        public bool pulse2_updated = true;
        void pulse2_trigger()
        {
            if (this.pulse2_lengthCounter == 0 || this.pulse2_lengthEnable == false)
            {
                this.pulse2_lengthCounter = 64;
            }
            this.pulse2_volume = this.pulse2_volumeEnvelopeStart;
            if (this.pulse2_dacEnabled)
            {
                this.pulse2_enabled = true;
            }
            this.reloadPulse2Period();
            this.updatePulse2Val();
            this.pulse2SynthTime = 0;
        }
        public float pulse2_getFrequencyHz()
        {
            float frequency = (this.pulse2_frequencyUpper << 8) | this.pulse2_frequencyLower;
            return 131072 / (2048 - frequency);
        }

        public bool wave_bank = false;
        public bool wave_dimension = true;
        public bool wave_enabled = false;
        public bool wave_dacEnabled = false;
        public bool wave_lengthEnable = true;
        public int wave_lengthCounter = 0;
        public int wave_frequencyUpper = 0;
        public int wave_frequencyLower = 0;
        public int wave_volume = 0;
        public int wave_oldVolume = 0;
        public byte[] wave_waveTable0 = new byte[32];
        public byte[] wave_waveTable1 = new byte[32];
        public bool wave_outputLeft = false;
        public bool wave_outputRight = false;
        public float wave_getFrequencyHz()
        {
            float frequency = (this.wave_frequencyUpper << 8) | this.wave_frequencyLower;
            return 65536 / (2048 - frequency);
        }

        public bool noise_enabled = false;
        public int noise_divisorCode = 0;
        public bool noise_lengthEnable = false;
        public int noise_lengthCounter = 0;
        public bool noise_dacEnabled = false;
        public int noise_volume = 0;
        public bool noise_volumeEnvelopeUp = false;
        public int noise_volumeEnvelopeSweep = 4;
        public int noise_volumeEnvelopeStart = 0;
        public bool noise_outputLeft = false;
        public bool noise_outputRight = false;
        public int noise_shiftClockFrequency = 0;
        public bool noise_counterStep = false;
        public int noise_envelopeSweep = 0;

        // #endregion


        bool nightcoreMode = false;

        bool vinLeftEnable = false;
        bool vinRightEnable = false;
        public int leftMasterVol = 0;
        public int rightMasterVol = 0;
        public int leftMasterVolMul = 0;
        public int rightMasterVolMul = 0;

        public byte pulse1Val = 0;
        public byte pulse2Val = 0;
        public byte waveVal = 0;
        public byte noiseVal = 0;

        int pulse1Pos = 0;
        int pulse2Pos = 0;
        int wavePos = 0;
        int noisePos = 0;

        float pulse1SynthTime = 0;
        float pulse2SynthTime = 0;

        int pulse1FreqTimer = 0;
        int pulse2FreqTimer = 0;
        int waveFreqTimer = 0;
        int noiseFreqTimer = 0;

        int pulse1Period = 0;
        int pulse2Period = 0;
        int wavePeriod = 0;
        int noisePeriod = 0;

        float capacitor1 = 0;
        float capacitor2 = 0;

        void calcPulse1Period()
        {
            this.pulse1Period = (2048 - ((this.pulse1_frequencyUpper << 8) | this.pulse1_frequencyLower)) * 4;
            if (this.nightcoreMode) this.pulse1Period = (int)(this.pulse1Period * 0.5);
        }
        void calcPulse2Period()
        {
            this.pulse2Period = (2048 - ((this.pulse2_frequencyUpper << 8) | this.pulse2_frequencyLower)) * 4;
            if (this.nightcoreMode) this.pulse2Period = (int)(this.pulse2Period * 0.5);
        }
        void calcWavePeriod()
        {
            this.wavePeriod = (2048 - ((this.wave_frequencyUpper << 8) | this.wave_frequencyLower)) * 2;
            if (this.nightcoreMode) this.wavePeriod = (int)(this.wavePeriod * 0.5);
        }
        void calcNoisePeriod()
        {
            int[] noiseDivider = new int[] { 8, 16, 32, 48, 64, 80, 96, 112 };
            this.noisePeriod = (noiseDivider[this.noise_divisorCode] << this.noise_shiftClockFrequency);
        }

        void reloadPulse1Period() { this.pulse1FreqTimer = this.pulse1Period; }
        void reloadPulse2Period() { this.pulse2FreqTimer = this.pulse2Period; }
        void reloadWavePeriod() { this.waveFreqTimer = this.wavePeriod; }
        void reloadNoisePeriod() { this.noiseFreqTimer = this.noisePeriod; }

        void noise_trigger()
        {
            if (this.noise_dacEnabled)
                this.noise_enabled = true;

            if (this.noise_lengthCounter == 0 || this.noise_lengthEnable == false)
            {
                this.noise_lengthCounter = 64;
            }
            this.noise_volume = this.noise_volumeEnvelopeStart;
            this.updateNoiseVal();
        }

        void wave_trigger()
        {
            if (this.wave_lengthCounter == 0 || this.wave_lengthEnable == false)
            {
                this.wave_lengthCounter = 256;
            }
            if (this.wave_dacEnabled)
            {
                this.wave_enabled = true;
            }
            this.updateWaveVal();
        }

        public bool enable1Out = true;
        public bool enable2Out = true;
        public bool enable3Out = true;
        public bool enable4Out = true;

        void updatePulse1Val()
        {
            this.pulse1Val = (byte)(this.pulse1_enabled ? PULSE_DUTY[this.pulse1_width][this.pulse1Pos] * this.pulse1_volume : 0);
        }
        void updatePulse2Val()
        {
            this.pulse2Val = (byte)(this.pulse2_enabled ? PULSE_DUTY[this.pulse2_width][this.pulse2Pos] * this.pulse2_volume : 0);
        }
        void updateWaveVal()
        {
            int[] waveShift = new int[] { 4, 0, 1, 2 };
            if (this.wave_dimension)
            {
                if (this.wavePos < 32)
                {
                    int pos = this.wavePos - 0;
                    this.waveVal = (byte)(this.wave_enabled ? this.wave_waveTable0[this.wavePos] >> waveShift[this.wave_volume] : 0);
                }
                else
                {
                    int pos = this.wavePos - 32;
                    this.waveVal = (byte)(this.wave_enabled ? this.wave_waveTable1[this.wavePos] >> waveShift[this.wave_volume] : 0);
                }
            }
            else
            {
                if (this.wave_bank)
                {
                    this.waveVal = (byte)(this.wave_enabled ? this.wave_waveTable1[this.wavePos] >> waveShift[this.wave_volume] : 0);
                }
                else
                {
                    this.waveVal = (byte)(this.wave_enabled ? this.wave_waveTable0[this.wavePos] >> waveShift[this.wave_volume] : 0);
                }
            }
        }
        void updateNoiseVal()
        {
            this.noiseVal = (byte)(this.noise_enabled ? (this.noise_counterStep ? SEVEN_BIT_NOISE[this.noisePos] : FIFTEEN_BIT_NOISE[this.noisePos]) * this.noise_volume : 0);
        }

        uint pendingCycles = 0;

        const uint FrameSequencerMax = 8192;
        uint FrameSequencerTimer = 0;

        float SamplePulse(float duty, float time, float frequencyHz)
        {
            float sin1 = 0;
            float sin2 = 0;

            float nyquist = OUTPUT_SAMPLE_RATE / 2;
            float harmonics = (float)Math.Ceiling(nyquist / frequencyHz);

            for (int n = 1; n < harmonics; n++)
            {
                int component = n * 65535;
                sin1 += SIN_LUT[(int)((time - 0) * component) & 65535] / n;
                sin2 += SIN_LUT[(int)((time - duty) * component) & 65535] / n;
            }

            // sin1 += sinLut[(time * 65535) & 65535];
            // sin2 += sinLut[(time * 65535) & 65535];

            return (float)((sin1 - sin2) + (duty * (Math.PI) - ((Math.PI / 2)) * 1.275));
        }       


        public void Tick(uint cycles)
        {
            this.pendingCycles += cycles;

            this.FrameSequencerTimer += cycles;
            if (this.FrameSequencerTimer >= FrameSequencerMax)
            {
                this.FrameSequencerTimer -= FrameSequencerMax;

                this.advanceFrameSequencer();
            }

            if (this.enabled)
            {
                if (this.pulse1_enabled) this.pulse1FreqTimer -= (int)cycles;
                if (this.pulse2_enabled) this.pulse2FreqTimer -= (int)cycles;
                if (this.wave_enabled) this.waveFreqTimer -= (int)cycles;
                if (this.noise_enabled) this.noiseFreqTimer -= (int)cycles;

                float in1 = 0;
                float in2 = 0;

                // Note: -1 value when disabled is the DAC DC offset

                this.pulse1SynthTime += this.pulse1_getFrequencyHz() / OUTPUT_SAMPLE_RATE;
                this.pulse2SynthTime += this.pulse2_getFrequencyHz() / OUTPUT_SAMPLE_RATE;

                if (this.pulse1_dacEnabled && this.pulse1_enabled && this.pulse1Period > 0)
                {
                    float rawWave = SamplePulse(PULSE_DUTY_VALUE_ARRAY[this.pulse1_width], this.pulse1SynthTime, this.pulse1_getFrequencyHz());
                    float val = (float)(rawWave * (float)this.pulse1_volume) / 2;
                    if (this.enable1Out)
                    {
                        if (this.pulse1_outputLeft) in1 += val;
                        if (this.pulse1_outputRight) in2 += val;
                    }
                }
                if (this.pulse2_dacEnabled && this.pulse2_enabled && this.pulse2Period > 0)
                {
                    float rawWave = SamplePulse(PULSE_DUTY_VALUE_ARRAY[this.pulse2_width], this.pulse2SynthTime, this.pulse2_getFrequencyHz());
                    float val = (float)(rawWave * (float)this.pulse2_volume) / 2;
                    if (this.enable2Out)
                    {
                        if (this.pulse2_outputLeft) in1 += val;
                        if (this.pulse2_outputRight) in2 += val;
                    }
                }
                if (this.wave_dacEnabled)
                {
                    if (this.wavePeriod > 0)
                    {
                        while (this.waveFreqTimer < 0)
                        {
                            this.waveFreqTimer += this.wavePeriod;

                            this.wavePos++;

                            if (this.wave_dimension)
                            {
                                this.wavePos &= 63;
                            }
                            else
                            {
                                this.wavePos &= 31;
                            }

                            this.updateWaveVal();
                        }
                    }
                    if (this.enable3Out)
                    {
                        if (this.wave_outputLeft) in1 += this.waveVal;
                        if (this.wave_outputRight) in2 += this.waveVal;
                    }
                }
                if (this.noise_dacEnabled)
                {
                    if (this.noisePeriod > 0)
                    {
                        while (this.noiseFreqTimer < 0)
                        {
                            this.noiseFreqTimer += this.noisePeriod;

                            this.noisePos++;
                            this.noisePos &= 32767;

                            this.updateNoiseVal();
                        }
                    }
                    if (this.enable4Out)
                    {
                        if (this.noise_outputLeft) in1 += this.noiseVal;
                        if (this.noise_outputRight) in2 += this.noiseVal;
                    }
                }

                in1 *= this.leftMasterVolMul;
                in2 *= this.rightMasterVolMul;

                Out1 = (short)(in1 * PsgFactor);
                Out2 = (short)(in2 * PsgFactor);
            }

            // The wave sample is visible to the CPU, so it's gotta be updated immediately
        }

        public uint PsgFactor = 8;

        void update()
        {
            // this.fastForwardSound((int)this.pendingCycles);
            this.pendingCycles = 0;
        }

        private void frameSequencerFrequencySweep()
        {
            // writeDebug("Frequency sweep")
            int actualPeriod = this.pulse1_freqSweepPeriod;
            if (actualPeriod == 0) actualPeriod = 8;
            if (this.clockPulse1FreqSweep >= actualPeriod)
            {
                this.clockPulse1FreqSweep = 0;
                if (this.freqSweepEnabled == true)
                {
                    this.applyFrequencySweep();
                }

                // writeDebug("abs(Range): " + diff);
                // writeDebug("Resulting frequency: " + this.pulse1_frequencyHz);

            }

            this.clockPulse1FreqSweep++;
        }

        private void applyFrequencySweep()
        {
            uint freq = (uint)((this.pulse1_frequencyUpper << 8) | this.pulse1_frequencyLower);
            uint diff = freq >> this.pulse1_freqSweepShift;
            uint newFreq = this.pulse1_freqSweepUp ? freq + diff : freq - diff;
            freq = newFreq;
            if (newFreq > 2047)
            {
                this.pulse1_enabled = false;
            }
            if (this.pulse1_freqSweepPeriod != 0 && this.pulse1_freqSweepShift != 0)
            {

                this.pulse1_frequencyLower = (int)(freq & 0xFF);
                this.pulse1_frequencyUpper = (int)((freq >> 8) & 0xFF);
            }

            this.calcPulse1Period();
            this.updatePulse1Val();
        }

        private void frameSequencerVolumeEnvelope()
        {
            this.ticksEnvelopePulse1--;
            if (this.ticksEnvelopePulse1 <= 0)
            {
                if (this.pulse1_volumeEnvelopeSweep != 0)
                {
                    if (this.pulse1_volumeEnvelopeUp == true)
                    {
                        if (this.pulse1_volume < 15)
                        {
                            this.pulse1_volume++;
                        }
                    }
                    else
                    {
                        if (this.pulse1_volume > 0)
                        {
                            this.pulse1_volume--;
                        }
                    }
                    this.updatePulse1Val();
                }
                this.ticksEnvelopePulse1 = this.pulse1_volumeEnvelopeSweep;
            }

            this.ticksEnvelopePulse2--;
            if (this.ticksEnvelopePulse2 <= 0)
            {
                if (this.pulse2_volumeEnvelopeSweep != 0)
                {
                    if (this.pulse2_volumeEnvelopeUp == true)
                    {
                        if (this.pulse2_volume < 15)
                        {
                            this.pulse2_volume++;
                        }
                    }
                    else
                    {
                        if (this.pulse2_volume > 0)
                        {
                            this.pulse2_volume--;
                        }
                    }
                    this.updatePulse2Val();
                }
                this.ticksEnvelopePulse2 = this.pulse2_volumeEnvelopeSweep;
            }

            this.ticksEnvelopeNoise--;
            if (this.ticksEnvelopeNoise <= 0)
            {
                if (this.noise_volumeEnvelopeSweep != 0)
                {
                    if (this.noise_volumeEnvelopeUp == true)
                    {
                        if (this.noise_volume < 15)
                        {
                            this.noise_volume++;
                        }
                    }
                    else
                    {
                        if (this.noise_volume > 0)
                        {
                            this.noise_volume--;
                        }
                    }
                    this.updateNoiseVal();
                }
                this.ticksEnvelopeNoise = this.noise_volumeEnvelopeSweep;
            }
        }

        private void frameSequencerLength()
        {
            this.clockPulse1Length();
            this.clockPulse2Length();
            this.clockWaveLength();
            this.clockNoiseLength();
        }

        private void clockPulse1Length()
        {
            if (this.pulse1_lengthEnable == true && this.pulse1_lengthCounter > 0)
            {
                this.pulse1_lengthCounter--;
                if (this.pulse1_lengthCounter == 0)
                {
                    this.pulse1_enabled = false;
                    this.updatePulse1Val();
                }
            }
        }

        private void clockPulse2Length()
        {
            if (this.pulse2_lengthEnable == true && this.pulse2_lengthCounter > 0)
            {
                this.pulse2_lengthCounter--;
                if (this.pulse2_lengthCounter == 0)
                {
                    this.pulse2_enabled = false;
                    this.updatePulse2Val();
                }
            }
        }

        private void clockWaveLength()
        {
            if (this.wave_lengthEnable == true && this.wave_lengthCounter > 0)
            {
                this.wave_lengthCounter--;
                if (this.wave_lengthCounter == 0)
                {
                    this.wave_enabled = false;
                    this.updateWaveVal();
                }
            }
        }

        private void clockNoiseLength()
        {
            if (this.noise_lengthEnable == true && this.noise_lengthCounter > 0)
            {
                this.noise_lengthCounter--;
                if (this.noise_lengthCounter == 0)
                {
                    this.noise_enabled = false;
                    this.updateNoiseVal();
                }
            }
        }

        public void WriteHwio8(uint addr, byte value)
        {
            if (this.enabled)
            {
                switch (addr)
                {
                    // Pulse 1
                    case 0x60: // NR10
                        this.pulse1_freqSweepPeriod = (value & 0b01110000) >> 4; // in 128ths of a second (0-7)
                        this.pulse1_freqSweepUp = ((value >> 3) & 1) == 0; // 0 == Add, 1 = Sub
                        this.pulse1_freqSweepShift = (value & 0b111); // 0-7; 
                        this.updatePulse1Val();
                        break;
                    case 0x62: // NR11
                        this.pulse1_width = value >> 6;
                        this.pulse1_lengthCounter = 64 - (value & 0b111111);
                        this.pulse1Val = (byte)PULSE_DUTY[this.pulse1_width][this.pulse1Pos];
                        this.updatePulse1Val();
                        break;
                    case 0x63: // NR12
                        {
                            bool newUp = ((value >> 3) & 1) == 1;
                            if (this.pulse1_enabled)
                            {
                                if (this.pulse1_volumeEnvelopeSweep == 0)
                                {
                                    if (this.pulse1_volumeEnvelopeUp)
                                    {
                                        this.pulse1_volume += 1;
                                        this.pulse1_volume &= 0xF;
                                    }
                                    else
                                    {
                                        this.pulse1_volume += 2;
                                        this.pulse1_volume &= 0xF;
                                    }
                                }

                                if (this.pulse1_volumeEnvelopeUp != newUp)
                                    this.pulse1_volume = 0;
                            }

                            this.pulse1_volumeEnvelopeStart = (value >> 4) & 0xF;
                            this.pulse1_volumeEnvelopeUp = newUp;
                            this.pulse1_volumeEnvelopeSweep = value & 0b111;
                            this.pulse1_dacEnabled = (value & 0b11111000) != 0;
                            if (!this.pulse1_dacEnabled) this.pulse1_enabled = false;
                            this.updatePulse1Val();
                        }
                        break;
                    case 0x64: // NR13 Low bits
                        this.pulse1_frequencyLower = value;
                        this.calcPulse1Period();
                        this.updatePulse1Val();
                        break;
                    case 0x65: // NR14
                        this.pulse1_frequencyUpper = value & 0b111;
                        this.pulse1_lengthEnable = ((value >> 6) & 1) != 0;
                        // If the next step does not clock the length counter
                        if (
                            ((this.frameSequencerStep + 1) & 7) == 1 ||
                            ((this.frameSequencerStep + 1) & 7) == 3 ||
                            ((this.frameSequencerStep + 1) & 7) == 5 ||
                            ((this.frameSequencerStep + 1) & 7) == 7
                        )
                        {
                            this.clockPulse1Length();
                        }
                        if (((value >> 7) & 1) != 0)
                        {
                            this.pulse1_trigger();
                        }
                        this.calcPulse1Period();
                        this.updatePulse1Val();
                        break;

                    // Pulse 2
                    case 0x68: // NR21
                        this.pulse2_width = value >> 6;
                        this.pulse2_lengthCounter = 64 - (value & 0b111111);
                        this.pulse2Val = (byte)PULSE_DUTY[this.pulse2_width][this.pulse2Pos];
                        this.updatePulse2Val();
                        break;
                    case 0x69: // NR22
                        {
                            bool newUp = ((value >> 3) & 1) == 1;

                            if (this.pulse2_enabled)
                            {
                                if (this.pulse2_volumeEnvelopeSweep == 0)
                                {
                                    if (this.pulse2_volumeEnvelopeUp)
                                    {
                                        this.pulse2_volume += 1;
                                        this.pulse2_volume &= 0xF;
                                    }
                                    else
                                    {
                                        this.pulse2_volume += 2;
                                        this.pulse2_volume &= 0xF;
                                    }
                                }

                                if (this.pulse2_volumeEnvelopeUp != newUp)
                                    this.pulse2_volume = 0;
                            }

                            this.pulse2_volumeEnvelopeStart = (value >> 4) & 0xF;
                            this.pulse2_volumeEnvelopeUp = newUp;
                            this.pulse2_volumeEnvelopeSweep = value & 0b111;
                            this.pulse2_dacEnabled = (value & 0b11111000) != 0;
                            if (!this.pulse2_dacEnabled) this.pulse2_enabled = false;
                        }
                        this.updatePulse2Val();
                        break;
                    case 0x6C: // NR23
                        this.pulse2_frequencyLower = value;
                        this.calcPulse2Period();
                        this.updatePulse2Val();
                        break;
                    case 0x6D: // NR24
                        this.pulse2_frequencyUpper = value & 0b111;
                        this.pulse2_lengthEnable = ((value >> 6) & 1) != 0;
                        // If the next step does not clock the length counter
                        if (
                            ((this.frameSequencerStep + 1) & 7) == 1 ||
                            ((this.frameSequencerStep + 1) & 7) == 3 ||
                            ((this.frameSequencerStep + 1) & 7) == 5 ||
                            ((this.frameSequencerStep + 1) & 7) == 7
                        )
                        {
                            this.clockPulse2Length();
                        }
                        if (((value >> 7) & 1) != 0)
                        {
                            this.pulse2_trigger();
                        }
                        this.calcPulse2Period();
                        this.updatePulse2Val();
                        break;

                    // Wave
                    case 0x70: // NR30
                        this.wave_dimension = BitTest(value, 5);
                        this.wave_bank = BitTest(value, 6);
                        this.wave_dacEnabled = BitTest(value, 7);
                        if (!this.wave_dacEnabled) this.wave_enabled = false;
                        this.updateWaveVal();
                        break;
                    case 0x72: // NR31
                        this.wave_lengthCounter = 256 - value;
                        this.updateWaveVal();
                        break;
                    case 0x73: // NR32
                        this.wave_volume = (value >> 5) & 0b11;
                        this.updateWaveVal();
                        break;
                    case 0x74: // NR33
                        this.wave_frequencyLower = value;
                        this.calcWavePeriod();
                        this.updateWaveVal();
                        break;
                    case 0x75: // NR34
                        this.wave_frequencyUpper = value & 0b111;
                        // If the next step does not clock the length counter
                        if (
                            ((this.frameSequencerStep + 1) & 7) == 1 ||
                            ((this.frameSequencerStep + 1) & 7) == 3 ||
                            ((this.frameSequencerStep + 1) & 7) == 5 ||
                            ((this.frameSequencerStep + 1) & 7) == 7
                        )
                        {
                            this.clockWaveLength();
                        }
                        if (((value >> 7) & 1) != 0)
                        {
                            this.wave_trigger();
                            this.wavePos = 0;
                            this.reloadWavePeriod();
                        }
                        this.wave_lengthEnable = ((value >> 6) & 1) != 0;
                        this.calcWavePeriod();
                        this.updateWaveVal();
                        break;

                    // Noise
                    case 0x78: // NR41
                        this.noise_lengthCounter = 64 - (value & 0b111111); // 6 bits
                        this.updateNoiseVal();
                        break;
                    case 0x79: // NR42
                        this.noise_volume = (value >> 4) & 0xF;
                        this.noise_volumeEnvelopeStart = (value >> 4) & 0xF;
                        this.noise_volumeEnvelopeUp = ((value >> 3) & 1) == 1;
                        this.noise_volumeEnvelopeSweep = value & 0b111;
                        this.noise_dacEnabled = (value & 0b11111000) != 0;
                        if (!this.noise_dacEnabled) this.noise_enabled = false;
                        this.updateNoiseVal();
                        break;
                    case 0x7C: // NR43
                        this.noise_shiftClockFrequency = (value >> 4) & 0xF;
                        this.noise_counterStep = ((value >> 3) & 1) != 0;
                        this.noise_divisorCode = (value & 0b111);
                        this.calcNoisePeriod();
                        this.updateNoiseVal();
                        break;
                    case 0x7D: // NR44
                               // If the next step does not clock the length counter
                        if (
                            ((this.frameSequencerStep + 1) & 7) == 1 ||
                            ((this.frameSequencerStep + 1) & 7) == 3 ||
                            ((this.frameSequencerStep + 1) & 7) == 5 ||
                            ((this.frameSequencerStep + 1) & 7) == 7
                        )
                        {
                            this.clockWaveLength();
                        }
                        if (((value >> 7) & 1) != 0)
                        {
                            this.noise_trigger();
                            this.noisePos = 0;
                            this.reloadNoisePeriod();
                        }
                        this.noise_lengthEnable = ((value >> 6) & 1) != 0;
                        this.updateNoiseVal();
                        break;

                    case 0x80: // NR50
                        this.vinLeftEnable = (value & BIT_7) != 0;
                        this.vinRightEnable = (value & BIT_3) != 0;
                        this.leftMasterVol = (value >> 4) & 0b111;
                        this.rightMasterVol = (value >> 0) & 0b111;
                        this.leftMasterVolMul = this.leftMasterVol / 7;
                        this.rightMasterVolMul = this.rightMasterVol / 7;
                        break;

                    // Panning
                    case 0x81: // NR51
                        this.noise_outputRight = (value & BIT_7) != 0;
                        this.wave_outputRight = (value & BIT_6) != 0;
                        this.pulse2_outputRight = (value & BIT_5) != 0;
                        this.pulse1_outputRight = (value & BIT_4) != 0;

                        this.noise_outputLeft = (value & BIT_3) != 0;
                        this.wave_outputLeft = (value & BIT_2) != 0;
                        this.pulse2_outputLeft = (value & BIT_1) != 0;
                        this.pulse1_outputLeft = (value & BIT_0) != 0;
                        break;
                }

                if (addr >= 0x90 && addr <= 0x9F)
                {
                    addr -= 0x90;

                    if (!this.wave_bank) // Non-selected wave bank will be written to
                    {
                        this.wave_waveTable1[(addr * 2) + 0] = (byte)(value >> 4);
                        this.wave_waveTable1[(addr * 2) + 1] = (byte)(value & 0xF);
                    }
                    else
                    {
                        this.wave_waveTable0[(addr * 2) + 0] = (byte)(value >> 4);
                        this.wave_waveTable0[(addr * 2) + 1] = (byte)(value & 0xF);
                    }
                }
                else
                {
                    this.soundRegisters[addr - 0x60] = value;
                }
            }

            if (addr == 0x84)
            {
                // Control
                if (((value >> 7) & 1) != 0)
                {
                    // writeDebug("Enabled sound");
                    this.enabled = true;
                    this.frameSequencerStep = 0;

                    Console.WriteLine("Enabled PSGs!");
                }
                else
                {
                    Console.WriteLine("Disabled PSGs...");

                    // Disable and write zeros on everything upon main disabling
                    this.noise_enabled = false;
                    this.wave_enabled = false;
                    this.pulse2_enabled = false;
                    this.pulse1_enabled = false;

                    this.noise_dacEnabled = false;
                    this.wave_dacEnabled = false;
                    this.pulse2_dacEnabled = false;
                    this.pulse1_dacEnabled = false;

                    this.WriteHwio8(0xFF10, 0);
                    this.WriteHwio8(0xFF11, 0);
                    this.WriteHwio8(0xFF12, 0);
                    this.WriteHwio8(0xFF13, 0);
                    this.WriteHwio8(0xFF14, 0);

                    this.WriteHwio8(0xFF16, 0);
                    this.WriteHwio8(0xFF17, 0);
                    this.WriteHwio8(0xFF18, 0);
                    this.WriteHwio8(0xFF19, 0);

                    this.WriteHwio8(0xFF1A, 0);
                    this.WriteHwio8(0xFF1B, 0);
                    this.WriteHwio8(0xFF1C, 0);
                    this.WriteHwio8(0xFF1D, 0);
                    this.WriteHwio8(0xFF1D, 0);

                    this.WriteHwio8(0xFF20, 0);
                    this.WriteHwio8(0xFF21, 0);
                    this.WriteHwio8(0xFF22, 0);
                    this.WriteHwio8(0xFF23, 0);

                    this.pulse1Pos = 0;
                    this.pulse2Pos = 0;
                    this.wavePos = 0;
                    this.noisePos = 0;

                    this.pulse1FreqTimer = 0;
                    this.pulse2FreqTimer = 0;
                    this.waveFreqTimer = 0;
                    this.noiseFreqTimer = 0;

                    for (int i = 0xFF10; i <= 0xFF25; i++)
                    {
                        this.soundRegisters[i - 0xFF10] = 0;
                    }
                    this.enabled = false;
                }
            }

            this.update();
        }

        public byte ReadHwio8(uint addr)
        {
            if (addr >= 0x60 && addr <= 0x9F)
            {
                byte i = this.soundRegisters[addr - 0x60];

                if (addr >= 0xFF27 && addr <= 0xFF2F) return 0xFF;

                if (addr >= 0x90 && addr <= 0x9F)
                {
                    uint tableAddr = addr - 0x90;
                    if (!this.wave_bank) // Non-selected wave bank will be written to
                    {
                        byte upper = this.wave_waveTable1[(tableAddr * 2) + 0];
                        byte lower = this.wave_waveTable1[(tableAddr * 2) + 1];
                        return (byte)((upper << 4) | lower);
                    }
                    else
                    {
                        byte upper = this.wave_waveTable0[(tableAddr * 2) + 0];
                        byte lower = this.wave_waveTable0[(tableAddr * 2) + 1];
                        return (byte)((upper << 4) | lower);
                    }
                }

                if (addr == 0x84)
                { // NR52
                    i = 0;
                    if (this.enabled) i |= (byte)BIT_7;
                    i |= 0b01110000;
                    if (this.noise_enabled && this.noise_dacEnabled) i |= (byte)BIT_3;
                    if (this.wave_enabled && this.wave_dacEnabled) i |= (byte)BIT_2;
                    if (this.pulse2_enabled && this.pulse2_dacEnabled) i |= (byte)BIT_1;
                    if (this.pulse1_enabled && this.pulse1_dacEnabled) i |= (byte)BIT_0;
                    return i;
                }

                switch (addr)
                {
                    case 0x60: i |= 0x80; break; // NR10
                    case 0x62: i |= 0x3F; break; // NR11
                    case 0x63: i |= 0x00; break; // NR12
                    case 0x64: i |= 0xFF; break; // NR13
                    case 0x65: i |= 0xBF; break; // NR14

                    case 0x66: i |= 0xFF; break; // Unused
                    case 0x68: i |= 0x3F; break; // NR21
                    case 0x69: i |= 0x00; break; // NR22
                    case 0x6C: i |= 0xFF; break; // NR23
                    case 0x6D: i |= 0xBF; break; // NR24

                    case 0x70: i |= 0x7F; break; // NR30
                    case 0x72: i |= 0xFF; break; // NR31
                    case 0x73: i |= 0x9F; break; // NR32
                    case 0x74: i |= 0xFF; break; // NR33
                    case 0x75: i |= 0xBF; break; // NR34

                    case 0x76: i |= 0xFF; break; // Unused
                    case 0x78: i |= 0xFF; break; // NR41
                    case 0x79: i |= 0x00; break; // NR42
                    case 0x7C: i |= 0x00; break; // NR43
                    case 0x7E: i |= 0xBF; break; // NR44

                    case 0x80: i |= 0x00; break;
                    case 0x81: i |= 0x00; break;
                    case 0x82: i |= 0xFF; break;
                }

                return i;
            }

            // // PCM12
            // if (addr == 0xFF76)
            // {
            //     this.update();
            //     return (byte)(this.pulse1Val | (this.pulse2Val << 4));
            // }
            // // PCM34
            // if (addr == 0xFF77)
            // {
            //     this.update();
            //     return (byte)(this.waveVal | (this.noiseVal << 4));
            // }

            return 0xFF;
        }

        void reset()
        {
            this.enabled = false;

            this.ticksEnvelopePulse1 = 0;
            this.ticksEnvelopePulse2 = 0;
            this.ticksEnvelopeNoise = 0;

            this.clockPulse1FreqSweep = 0;
            this.freqSweepEnabled = false;

            this.frameSequencerStep = 0;

            this.soundRegisters = new byte[64];

            this.pulse1_enabled = false;
            this.pulse1_width = 3;
            this.pulse1_dacEnabled = false;
            this.pulse1_lengthEnable = false;
            this.pulse1_lengthCounter = 0;
            this.pulse1_frequencyUpper = 0;
            this.pulse1_frequencyLower = 0;
            this.pulse1_volume = 0;
            this.pulse1_volumeEnvelopeUp = false;
            this.pulse1_volumeEnvelopeSweep = 4;
            this.pulse1_volumeEnvelopeStart = 0;
            this.pulse1_outputLeft = false;
            this.pulse1_outputRight = false;
            this.pulse1_freqSweepPeriod = 0;
            this.pulse1_freqSweepUp = false;
            this.pulse1_freqSweepShift = 0;
            this.pulse1_updated = true;
            this.pulse2_enabled = false;
            this.pulse2_width = 3;
            this.pulse2_dacEnabled = false;
            this.pulse2_lengthEnable = false;
            this.pulse2_lengthCounter = 0;
            this.pulse2_frequencyUpper = 0;
            this.pulse2_frequencyLower = 0;
            this.pulse2_volume = 0;
            this.pulse2_volumeEnvelopeUp = false;
            this.pulse2_volumeEnvelopeSweep = 4;
            this.pulse2_volumeEnvelopeStart = 0;
            this.pulse2_outputLeft = false;
            this.pulse2_outputRight = false;
            this.pulse2_updated = true;
            this.wave_enabled = false;
            this.wave_dacEnabled = false;
            this.wave_lengthEnable = true;
            this.wave_lengthCounter = 0;
            this.wave_frequencyUpper = 0;
            this.wave_frequencyLower = 0;
            this.wave_volume = 0;
            this.wave_oldVolume = 0;
            this.wave_waveTable0 = new byte[32];
            this.wave_waveTable1 = new byte[32];
            this.wave_outputLeft = false;
            this.wave_outputRight = false;
            this.noise_enabled = false;
            this.noise_divisorCode = 0;
            this.noise_lengthEnable = false;
            this.noise_lengthCounter = 0;
            this.noise_dacEnabled = false;
            this.noise_volume = 0;
            this.noise_volumeEnvelopeUp = false;
            this.noise_volumeEnvelopeSweep = 4;
            this.noise_volumeEnvelopeStart = 0;
            this.noise_outputLeft = false;
            this.noise_outputRight = false;
            this.noise_shiftClockFrequency = 0;
            this.noise_counterStep = false;
            this.noise_envelopeSweep = 0;

            this.pulse1Val = 0;
            this.pulse2Val = 0;
            this.waveVal = 0;
            this.noiseVal = 0;

            this.pulse1FreqTimer = 0;
            this.pulse2FreqTimer = 0;
            this.waveFreqTimer = 0;
            this.noiseFreqTimer = 0;

            this.updatePulse1Val();
            this.updatePulse2Val();
            this.updateWaveVal();
            this.updateNoiseVal();

            this.vinLeftEnable = false;
            this.vinRightEnable = false;
            this.leftMasterVol = 0;
            this.rightMasterVol = 0;
            this.leftMasterVolMul = 0;
            this.rightMasterVolMul = 0;
        }
    }
}