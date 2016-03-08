using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MyoAutotune
{
    class ModulateProvider : IWaveProvider
    {
        private IWaveProvider stream1;
        private IWaveProvider stream2;

        private WaveBuffer waveBuffer1;
        private WaveBuffer waveBuffer2;

        public ModulateProvider(IWaveProvider stream1, IWaveProvider stream2)
        {
            this.stream1 = stream1;
            this.stream2 = stream2;
            this.waveBuffer1 = new WaveBuffer(8192);
            this.waveBuffer2 = new WaveBuffer(8192);
        }

        public WaveFormat WaveFormat
        {
            get { return stream1.WaveFormat;  }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            if (waveBuffer1 == null || waveBuffer1.MaxSize < count)
            {
                waveBuffer1 = new WaveBuffer(count);
            }
            if (waveBuffer2 == null || waveBuffer2.MaxSize < count)
            {
                waveBuffer2 = new WaveBuffer(count);
            }

            int read1 = stream1.Read(waveBuffer1, 0, count);
            int read2 = stream2.Read(waveBuffer2, 0, count);

            if (read1 > 0) read1 = count;

            int frames = read1 / sizeof(float);

            WaveBuffer outBuffer = new WaveBuffer(buffer);

            for (int i = 0; i < frames; i++)
            {
                outBuffer.FloatBuffer[i] = waveBuffer1.FloatBuffer[i] * waveBuffer2.FloatBuffer[i] * 4;
                //outBuffer.FloatBuffer[i] = waveBuffer1.FloatBuffer[i];
            }

            return frames * 4;
        }
    }
}
