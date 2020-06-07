using OpenTK;
using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.IO;
using ManagedBass;

namespace DMSharpEmulator
{
    public class Playback
    {
        static readonly string filename = Path.Combine(Path.Combine("Data", "Audio"), "the_ring_that_fell.wav");

        public static ALFormat GetSoundFormat(int channels, int bits)
        {
            switch (channels)
            {
                case 1: return bits == 8 ? ALFormat.Mono8 : ALFormat.Mono16;
                case 2: return bits == 8 ? ALFormat.Stereo8 : ALFormat.Stereo16;
                default: throw new NotSupportedException("The specified sound format is not supported.");
            }
        }

        public static void Play()
        {
            using (AudioContext context = new AudioContext())
            {
                int buffer = AL.GenBuffer();
                int source = AL.GenSource();
                int state;

                int channels, bits_per_sample, sample_rate;
                channels = 1;
                bits_per_sample = 8;
                sample_rate = 44100;

                byte[] sound_data = new byte[sample_rate * 2];

                for (var i = 0; i < sound_data.Length; i++)
                {
                    sound_data[i] = (byte)(((Math.Sin(i / 90) + 127) * 4));
                }

                AL.BufferData(buffer, GetSoundFormat(channels, bits_per_sample), sound_data, sound_data.Length, sample_rate);
                AL.Source(source, ALSourcei.Buffer, buffer);

                 AL.SourcePlay(source);

                Thread.Sleep(2000);
                AL.BufferData(buffer, GetSoundFormat(channels, bits_per_sample), sound_data, sound_data.Length, sample_rate);
                AL.Source(source, ALSourcei.Buffer, buffer);



                AL.BufferData(buffer, GetSoundFormat(channels, bits_per_sample), sound_data, sound_data.Length, sample_rate);
                AL.Source(source, ALSourcei.Buffer, buffer);
               

                Trace.Write("Playing");

                // Query the source to find out when it stops playing.
                do
                {
                    Thread.Sleep(250);
                    Trace.Write(".");
                    AL.GetSource(source, ALGetSourcei.SourceState, out state);

                }
                while ((ALSourceState)state == ALSourceState.Playing);



                Trace.WriteLine("");

                AL.SourceStop(source);
                AL.DeleteSource(source);
                AL.DeleteBuffer(buffer);
            }
        }
    }
}