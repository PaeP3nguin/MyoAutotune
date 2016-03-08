using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using NAudio.Wave;

using MyoSharp.Communication;
using MyoSharp.Device;
using MyoSharp.Exceptions;
using MyoSharp.Poses;
using System.Windows.Threading;
using MyoSharp.Math;

namespace MyoAutotune
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static MainWindow instance;

        private const float ROLL_THRESHOLD = 0.6f;
        private const float PITCH_THRESHOLD = 0.4f;
        private const int OUT_DEVICE = 0;

        private WaveIn micIn;
        private WaveOut speakerOut;
        private WaveFileWriter fileOut;

        private SineWaveProvider32 sineProvider;
        private AutoTuneWaveProvider autoProvider;

        private float scalingFactor = 8f;
        private Key lastKey = Key.A;
        private HashSet<Key> musical;

        private Pose lastPose;
        private QuaternionF initial;
        private QuaternionF last;
        private HandPosition lastPosition;

        public MainWindow()
        {
            instance = this;
            InitializeComponent();
            this.KeyDown += new KeyEventHandler(MainWindow_KeyDown);
            musical = new HashSet<Key>() {
                Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I,
                Key.O, Key.P, Key.OemOpenBrackets, Key.OemCloseBrackets
            };

            var channel = Channel.Create(
                ChannelDriver.Create(ChannelBridge.Create(),
                MyoErrorHandlerDriver.Create(MyoErrorHandlerBridge.Create())));
            var hub = Hub.Create(channel);
            channel.SetLockingPolicy(ChannelBridge.LockingPolicy.None);

            // listen for when the Myo connects
            hub.MyoConnected += (sender, e) =>
            {
                Console.WriteLine("Myo {0} has connected!", e.Myo.Handle);
                e.Myo.Vibrate(VibrationType.Short);
                e.Myo.PoseChanged += Myo_PoseChanged;
                e.Myo.OrientationDataAcquired += Myo_OrientationDataAcquired;
            };

            // listen for when the Myo disconnects
            hub.MyoDisconnected += (sender, e) =>
            {
                Console.WriteLine("Oh no! It looks like {0} arm Myo has disconnected!", e.Myo.Arm);
                e.Myo.PoseChanged -= Myo_PoseChanged;
                e.Myo.OrientationDataAcquired -= Myo_OrientationDataAcquired;
            };

            // start listening for Myo data
            channel.StartListening();
        }

        private void StartRecording(object sender, RoutedEventArgs e)
        {
            micIn = new WaveIn();
            micIn.DeviceNumber = 0;
            micIn.WaveFormat = new WaveFormat(44100, 1);

            Wave16toIeeeProvider micProvider = new Wave16toIeeeProvider(new WaveInProvider(micIn));

            autoProvider = new AutoTuneWaveProvider(micProvider);
            sineProvider = new SineWaveProvider32(440, 0.25f);
            sineProvider.SetWaveFormat(44100, 1); // 44.1kHz mono
            ModulateProvider modProvider = new ModulateProvider(autoProvider, sineProvider);

            changeTarget(lastKey);

            speakerOut = new WaveOut();
            //selectOut.DesiredLatency = 100;
            speakerOut.DeviceNumber = OUT_DEVICE;
            speakerOut.Init(modProvider);

            micIn.StartRecording();
            speakerOut.Play();
        }

        private void StopRecording(object sender, RoutedEventArgs e)
        {
            if (speakerOut != null)
            {
                speakerOut.Stop();
                speakerOut.Dispose();
                speakerOut = null;
            }
            if (micIn != null)
            {
                micIn.StopRecording();
                micIn.Dispose();
                micIn = null;
            }
        }

        private void demoMyo(object sender, RoutedEventArgs e)
        {
            sineProvider = new SineWaveProvider32(440, 0.25f);
            sineProvider.SetWaveFormat(44100, 1);

            changeTarget(lastKey);

            speakerOut = new WaveOut();
            //selectOut.DesiredLatency = 100;
            speakerOut.DeviceNumber = OUT_DEVICE;
            speakerOut.Init(sineProvider);
            speakerOut.Play();
        }

        private void StartMP3(object sender, RoutedEventArgs e)
        {
            BufferedWaveProvider fileProvider;

            using (Mp3FileReader reader = new Mp3FileReader("C:\\Users\\William\\Downloads\\voicein.mp3"))
            {
                if (16 != reader.WaveFormat.BitsPerSample)
                {
                    Console.Out.WriteLine("Only works with 16 bit audio");
                    return;
                }
                byte[] buffer = new byte[reader.Length];
                int read = reader.Read(buffer, 0, buffer.Length);

                fileProvider = new BufferedWaveProvider(reader.WaveFormat);
                fileProvider.AddSamples(buffer, 0, read);
            }

            autoProvider = new AutoTuneWaveProvider(new Wave16toIeeeProvider(fileProvider));
            sineProvider = new SineWaveProvider32(440, 0.25f);
            sineProvider.SetWaveFormat(44100, 1); // 16kHz mono
            ModulateProvider modProvider = new ModulateProvider(autoProvider, sineProvider);

            changeTarget(lastKey);

            using (fileOut = new WaveFileWriter("C:\\Users\\William\\Downloads\\voiceout.wav", fileProvider.WaveFormat))
            {
                modProvider.ToSampleProvider();
            }

            speakerOut = new WaveOut();
            //selectOut.DesiredLatency = 100;
            speakerOut.DeviceNumber = OUT_DEVICE;
            speakerOut.Init(modProvider);

            speakerOut.Play();
        }

        private void StopMP3(object sender, RoutedEventArgs e)
        {
            speakerOut.Stop();
            speakerOut.Dispose();
        }

        private void Myo_PoseChanged(object sender, PoseEventArgs e)
        {
            Console.WriteLine("{0} arm Myo detected {1} pose!", e.Myo.Arm, e.Myo.Pose);

            lastPose = e.Myo.Pose;

            switch (e.Myo.Pose)
            {
                case Pose.DoubleTap:
                    if (speakerOut == null)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => StartRecording(null, null)));
                    }
                    //else
                    //{
                    //    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => StopRecording(null, null)));
                    //}
                    break;
                case Pose.Rest:
                    changeTarget(lastPosition);
                    break;
                case Pose.Fist:
                    changeTarget(lastPosition);
                    break;
                case Pose.WaveIn:
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.Oem1)));
                    break;
                case Pose.FingersSpread:
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.Oem2)));
                    break;
                case Pose.WaveOut:
                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.Oem3)));
                    break;
                default:
                    break;
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => PoseText.Text = "Pose: " + lastPose));
        }

        private void Myo_OrientationDataAcquired(object sender, OrientationDataEventArgs e)
        {
            if (initial == null)
            {
                initial = e.Orientation;
            }

            QuaternionF quat = e.Orientation;
            //QuaternionF corr = quat * initial.Conjugate();
            float R = (float) e.Roll - QuaternionF.Roll(initial);
            float P = (float) e.Pitch - QuaternionF.Pitch(initial);
            float Y = (float) e.Yaw - QuaternionF.Yaw(initial);

            //Console.WriteLine(@"Roll: {0}, Pitch: {1}, Yaw: {1}", e.Roll, e.Pitch, e.Yaw);
            //Console.WriteLine(@"Roll: {0}, Pitch: {1}, Yaw: {1}", QuaternionF.Roll(quat), QuaternionF.Pitch(quat), QuaternionF.Yaw(quat));
            //Console.WriteLine(@"Roll: {0}, Pitch: {1}, Yaw: {1}", QuaternionF.Roll(corr), QuaternionF.Pitch(corr), QuaternionF.Yaw(corr));
            //Console.WriteLine(@"X: {0}, Y: {1}, Z: {1}", X, Y, Z);
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => RPYText.Text = String.Format(@"R: {0:0.00}, P: {1:0.00}, Y: {1:0.00}", R, P, Y)));
            //Console.WriteLine(@"Quat: {0}", quat);

            HandPosition pos = HandPosition.NA;

            if (P >= PITCH_THRESHOLD - 0.2f)
            {
                if (R >= ROLL_THRESHOLD)
                {
                    pos = HandPosition.UL;
                }
                else if (R <= -ROLL_THRESHOLD)
                {
                    pos = HandPosition.UR;
                }
                else
                {
                    pos = HandPosition.UM;
                }
            }
            else if (P  <= -PITCH_THRESHOLD)
            {
                if (R >= ROLL_THRESHOLD)
                {
                    pos = HandPosition.DL;
                }
                else if (R <= -ROLL_THRESHOLD)
                {
                    pos = HandPosition.DR;
                }
                else
                {
                    pos = HandPosition.DM;
                }
            }

            if (lastPosition != pos)
            {
                changeTarget(pos);
            }

            last = quat;
        }

        void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            changeTarget(e.Key);
        }

        void changeTarget(HandPosition pos)
        {
            switch (pos)
            {
                case HandPosition.DL:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.Q)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.OemCloseBrackets)));
                    }
                    break;
                case HandPosition.DM:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.W)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.OemOpenBrackets)));
                    }
                    break;
                case HandPosition.DR:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.E)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.P)));
                    }
                    break;
                case HandPosition.UL:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.Y)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.U)));
                    }
                    break;
                case HandPosition.UM:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.T)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.I)));
                    }
                    break;
                case HandPosition.UR:
                    if (lastPose != Pose.Fist)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.R)));
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => changeTarget(Key.O)));
                    }
                    break;
                default:
                    break;
            }

            if (lastPosition != pos)
            {
                lastPosition = pos;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => PositionText.Text = "Position: " + pos.ToString()));
            }
        }

        void changeTarget(Key k)
        {
            if (sineProvider == null)
            {
                return;
            }

            float target = 440f;

            switch (k)
            {
                case Key.Q:
                    target = 440f;
                    break;
                case Key.W:
                    target = 466.164f;
                    break;
                case Key.E:
                    target = 493.883f;
                    break;
                case Key.R:
                    target = 523.251f;
                    break;
                case Key.T:
                    target = 554.365f;
                    break;
                case Key.Y:
                    target = 587.330f;
                    break;
                case Key.U:
                    target = 622.254f;
                    break;
                case Key.I:
                    target = 659.255f;
                    break;
                case Key.O:
                    target = 698.456f;
                    break;
                case Key.P:
                    target = 739.989f;
                    break;
                case Key.OemOpenBrackets:
                    target = 783.991f;
                    break;
                case Key.OemCloseBrackets:
                    target = 830.609f;
                    break;
                case Key.Up:
                    scalingFactor /= 2;
                    changeTarget(lastKey);
                    break;
                case Key.Down:
                    scalingFactor *= 2;
                    changeTarget(lastKey);
                    break;
                case Key.Oem1:
                    scalingFactor = 8f;
                    changeTarget(lastKey);
                    break;
                case Key.Oem2:
                    scalingFactor = 4f;
                    changeTarget(lastKey);
                    break;
                case Key.Oem3:
                    scalingFactor = 2f;
                    changeTarget(lastKey);
                    break;
                case Key.A:
                    scalingFactor = 8f;
                    changeTarget(Key.P);
                    break;
                case Key.S:
                    scalingFactor = 4f;
                    changeTarget(Key.P);
                    break;
                case Key.D:
                    scalingFactor = 4f;
                    changeTarget(Key.Q);
                    break;
                case Key.F:
                    scalingFactor = 2f;
                    changeTarget(Key.Q);
                    break;
                case Key.G:
                    scalingFactor = 2f;
                    changeTarget(Key.P);
                    break;
                case Key.H:
                    scalingFactor = 2f;
                    changeTarget(Key.I);
                    break;
                case Key.J:
                    scalingFactor = 2f;
                    changeTarget(Key.Y);
                    break;
                case Key.K:
                    scalingFactor = 4f;
                    changeTarget(Key.E);
                    break;
                default:
                    break;
            }

            if (musical.Contains(k))
            {
                lastKey = k;
                sineProvider.Frequency = target / scalingFactor;

                if (autoProvider != null)
                {
                    autoProvider.TargetPitch = target / scalingFactor;
                }

                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => TargetPitchText.Text = "Target: " + target / scalingFactor));
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ScaleFactorText.Text = "Scale factor: " + scalingFactor));
        }

        private void calibrateMyo(object sender, RoutedEventArgs e)
        {
            initial = last;
        }

        enum HandPosition
        {
            UL, UM, UR, DL, DM, DR, NA
        }
    }
}
