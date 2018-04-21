using System.ComponentModel;
using System.Windows.Threading;

namespace SpeechUI
{
    using System.Windows;
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using Google.Cloud.Speech.V1;
    using System.Diagnostics;

    public partial class MainWindow : Window
    {
        private CancellationTokenSource cancelRecordingTokenSource;
        private DispatcherTimer timer;
        private Stopwatch stopWatch;
        public string TimeElapsed { get; set; }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            if (btnRecord.IsChecked == true)
            {
                if (cancelRecordingTokenSource != null)
                    cancelRecordingTokenSource.Dispose();
                cancelRecordingTokenSource = new CancellationTokenSource();
                txtSpeech.Text = String.Empty;
                this.btnRecord.Content = "Recording";
                StreamingMicRecognizeAsync(45);
                StartTimer();
            }
            else
            {
                StopTimer();
                cancelRecordingTokenSource.Cancel();
                this.btnRecord.Content = "Start Recording";
            }
        }

        private void StopTimer()
        {
            timer.Stop();
            stopWatch.Stop();
        }

        private void StartTimer()
        {
            timer = new DispatcherTimer();
            timer.Tick += dispatcherTimerTick_;
            timer.Interval = new TimeSpan(0, 0, 0, 0, 1);
            stopWatch = new Stopwatch();
            stopWatch.Start();
            timer.Start();
        }

        private void dispatcherTimerTick_(object sender, EventArgs e)
        {
            var totalSecondsElapsed = (stopWatch.Elapsed.TotalMilliseconds) / 1000;
            timeElapsed.Text = totalSecondsElapsed.ToString("N1") + " seconds.";
        }

        private async Task<string> StreamingMicRecognizeAsync(int seconds)
        {
            string responses = string.Empty;
            try
            {
                if (NAudio.Wave.WaveIn.DeviceCount < 1)
                {
                    MessageBox.Show("No microphone!");
                    return "No micrphone found.";
                }
                var speech = SpeechClient.Create();
                var streamingCall = speech.StreamingRecognize();
                // Write the initial request with the config.
                await streamingCall.WriteAsync(
                    new StreamingRecognizeRequest()
                    {
                        StreamingConfig = new StreamingRecognitionConfig()
                        {
                            Config = new RecognitionConfig()
                            {
                                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                                SampleRateHertz = 16000,
                                LanguageCode = "en",
                            },
                            InterimResults = false,
                        }
                    });

                // Print responses as they arrive.
                Task printResponses = Task.Run(async () =>
                {
                    StringBuilder builder = new StringBuilder();
                    while (await streamingCall.ResponseStream.MoveNext(default(CancellationToken)))
                    {
                        foreach (var result in streamingCall.ResponseStream
                            .Current.Results)
                        {
                            foreach (var alternative in result.Alternatives)
                            {
                                builder.Append(alternative.Transcript);
                            }
                        }
                    }

                    txtSpeech.Dispatcher.Invoke(() =>
                    {
                        txtSpeech.Text = builder.ToString();
                    }
                                
                    );
                });

                // Read from the microphone and stream to API.
                object writeLock = new object();
                bool writeMore = true;
                var waveIn = new NAudio.Wave.WaveInEvent();
                waveIn.DeviceNumber = 0;
                waveIn.WaveFormat = new NAudio.Wave.WaveFormat(16000, 1);
                waveIn.DataAvailable +=
                    (object sender, NAudio.Wave.WaveInEventArgs args) =>
                    {
                        lock (writeLock)
                        {
                            if (!writeMore) return;
                            streamingCall.WriteAsync(
                                new StreamingRecognizeRequest()
                                {
                                    AudioContent = Google.Protobuf.ByteString.CopyFrom(args.Buffer, 0, args.BytesRecorded)
                                }).Wait();
                        }
                    };

                try
                {
                    waveIn.StartRecording();
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cancelRecordingTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    waveIn.StopRecording();
                }

                lock (writeLock) writeMore = false;
                await streamingCall.WriteCompleteAsync();
                await printResponses;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
            return responses;
        }
    }
}