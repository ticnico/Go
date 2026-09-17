using System;
using System.Windows;
using System.Windows.Threading;

namespace GoldenBullet
{
    public partial class SplashScreen : Window
    {
        private DispatcherTimer progressTimer;
        private int currentProgress = 0;

        public SplashScreen()
        {
            InitializeComponent();
            InitializeProgressTimer();

            // ✅ FIX: Start the timer so the progress bar moves automatically
            progressTimer.Start();
        }

        private void InitializeProgressTimer()
        {
            progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            progressTimer.Tick += ProgressTimer_Tick;
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            // Slowly increment progress if App.xaml.cs isn't controlling it yet
            if (currentProgress < 100)
            {
                currentProgress += 1;
                UpdateUI(currentProgress);
            }
            else
            {
                progressTimer.Stop();
            }
        }

        private void UpdateUI(int progress)
        {
            ProgressBar.Value = progress;
            ProgressText.Text = $"{progress}%";
        }

        /// <summary>
        /// Call this from App.xaml.cs to set exact progress based on real loading steps.
        /// </summary>
        public void SetProgress(int value, string statusMessage = null)
        {
            // Stop the automatic timer if the main app is taking control
            if (value > currentProgress + 10)
            {
                progressTimer.Stop();
            }

            currentProgress = value;
            UpdateUI(value);

            if (!string.IsNullOrEmpty(statusMessage))
            {
                StatusText.Text = statusMessage;
            }

            if (value >= 100)
            {
                progressTimer.Stop();
            }
        }

        public void UpdateStatus(string message)
        {
            StatusText.Text = message;
        }
    }
}
