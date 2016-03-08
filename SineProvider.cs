using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MyoAutotune
{
    public class SineWaveProvider32 : WaveProvider32
    {
        int sample;
        int sampleRate;

        public float amplitude;

        public float Frequency { get; set; }
        public float Amplitude { get { return amplitude; } set { amplitude = value; sample = 0; } }

        public SineWaveProvider32()
            : this(440, 1f)
        {
        }

        public SineWaveProvider32(float frequency, float amplitude)
        {
            Frequency = frequency;
            Amplitude = amplitude;
            sampleRate = WaveFormat.SampleRate;
        }

        public override int Read(float[] buffer, int offset, int sampleCount)
        {
            for (int n = 0; n < sampleCount; n++)
            {
                buffer[n + offset] = (float)(Amplitude * Math.Sin((2 * Math.PI * sample * Frequency) / sampleRate));
                sample++;
                if (sample >= sampleRate) sample = 0;
            }
            return sampleCount;
        }
    }
}
