// this class based on code from awesomebox, a project created by by Ravi Parikh and Keegan Poppen, used with permission
// http://decabear.com/awesomebox.html
using System;
using NAudio.Wave;
using Pitch;
using System.Collections.Generic;
using System.Linq;
using NAudio.Dsp;

namespace MyoAutotune
{
    public class AutoTuneWaveProvider : IWaveProvider
    {
        private IWaveProvider source;
        private SmbPitchShifter pitchShifter;
        private PitchTracker pitchDetector;
        private WaveBuffer waveBuffer;
        private AutoTuneSettings autoTuneSettings;

        private float pitch;
        private List<float> detectedPitches;
        private float alpha = 1.00f;

        public float TargetPitch { get; set; }

        public AutoTuneSettings Settings
        {
            get { return this.autoTuneSettings; }
        }

        public AutoTuneWaveProvider(IWaveProvider source) :
            this(source, new AutoTuneSettings())
        {
        }

        public AutoTuneWaveProvider(IWaveProvider source, AutoTuneSettings autoTuneSettings)
        {
            this.autoTuneSettings = autoTuneSettings;
            this.autoTuneSettings.SnapMode = false;
            if (source.WaveFormat.SampleRate != 44100)
                throw new ArgumentException("AutoTune only works at 44.1kHz");
            if (source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                throw new ArgumentException("AutoTune only works on IEEE floating point audio data");
            if (source.WaveFormat.Channels != 1)
                throw new ArgumentException("AutoTune only works on mono input sources");

            this.source = source;
            this.pitchDetector = new PitchTracker();
            pitchDetector.SampleRate = source.WaveFormat.SampleRate;
            pitchDetector.RecordPitchRecords = false;
            pitchDetector.PitchRecordsPerSecond = 10;
            pitchDetector.PitchDetected += pitchDetector_PitchDetected;
            this.pitchShifter = new SmbPitchShifter(Settings, source.WaveFormat.SampleRate);
            this.waveBuffer = new WaveBuffer(8192);
            this.detectedPitches = new List<float>();
        }

        void pitchDetector_PitchDetected(PitchTracker sender, PitchTracker.PitchRecord pitchRecord)
        {
            detectedPitches.Add(pitchRecord.Pitch);
        }

        private float previousPitch;
        private int release;
        private int maxHold = 1;

        public int Read(byte[] buffer, int offset, int count)
        {
            if (waveBuffer == null || waveBuffer.MaxSize < count)
            {
                waveBuffer = new WaveBuffer(count);
            }

            int bytesRead = source.Read(waveBuffer, 0, count);

            // the last bit sometimes needs to be rounded up:
            if (bytesRead > 0) bytesRead = count;

            int frames = bytesRead / sizeof(float);

            pitchDetector.ProcessBuffer(waveBuffer.FloatBuffer.Take(frames).ToArray());

            pitch = (1 - alpha) * previousPitch + alpha * detectedPitches.Average();

            detectedPitches.Clear();

            MainWindow.instance.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() => MainWindow.instance.DetectedPitchText.Text = "Detected: " + pitch));

            if (pitch == 0f && release < maxHold)
            {
                pitch = previousPitch;
                release++;
            }
            else
            {
                this.previousPitch = pitch;
                release = 0;
            }

            WaveBuffer outBuffer = new WaveBuffer(buffer);

            pitchShifter.ShiftPitch(waveBuffer.FloatBuffer, pitch, TargetPitch, outBuffer.FloatBuffer, frames);

            return frames * 4;
        }

        public WaveFormat WaveFormat
        {
            get { return source.WaveFormat; }
        }
    }
}
