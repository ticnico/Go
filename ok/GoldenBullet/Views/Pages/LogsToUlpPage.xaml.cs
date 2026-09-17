using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class LogsToUlpPage : Page
    {
        private string _selectedFolder;
        private string _saveFilePath;
        private CancellationTokenSource _cts;
        private readonly object _lockObj = new();

        private readonly List<string> _results = new();
        private readonly HashSet<string> _seenUrls = new();
        private int _processedFiles;
        private int _totalHits;
        private string _currentSavePath;
        private long _totalLinesProcessed;
        private string _currentFileName;

        private readonly ConcurrentQueue<string> _uiLogQueue = new();
        private readonly ConcurrentQueue<string> _hitQueue = new();

        private readonly string[] _targetFiles = { "passwords.txt", "all passwords.txt", "amazon.txt" };

        // Timers for real-time UI updates
        private DispatcherTimer _logFlushTimer;
        private DispatcherTimer _statsUpdateTimer;
        private bool _isRunning;
        private DateTime _startTime;

        // Progress animation variables (no pre-counting)
        private int _progressPulse;
        private bool _progressForward = true;

        public LogsToUlpPage()
        {
            InitializeComponent();
            InitializeTimers();
            ResetUiState();

            // Smooth scroll behavior
            OutputText.TextChanged += (s, e) =>
            {
                if (OutputText.IsFocused) return;
                OutputScrollViewer?.ScrollToEnd();
            };
        }

        private void InitializeTimers()
        {
            // Timer 1: Flush log messages and hits (every 250ms for responsive UI)
            _logFlushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _logFlushTimer.Tick += (s, e) => FlushUiUpdates();

            // Timer 2: Update stats labels and progress animation (every 100ms)
            _statsUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _statsUpdateTimer.Tick += (s, e) => UpdateStatsDisplay();
        }

        private void ResetUiState()
        {
            Dispatcher.Invoke(() =>
            {
                StartStopButton.Content = "▶ Start";
                StartStopButton.Style = (Style)Application.Current.Resources["StyledSuccessButton"];
                StartStopButton.IsEnabled = false;
                OpenButton.IsEnabled = true;

                // Reset all TextBlock labels
                HitLabel.Text = "Hits: 0";
                FilesScannedLabel.Text = "0";
                StatusLabel.Text = "Idle";
                CurrentFileLabel.Text = "—";
                LinesProcessedLabel.Text = "0";
                ElapsedLabel.Text = "00:00:00";

                // Reset other controls
                FolderPathEntry.Text = string.Empty;
                OutputText.Clear();

                // Reset progress bar to indeterminate-style pulse
                MainProgressBar.IsIndeterminate = false;
                MainProgressBar.Minimum = 0;
                MainProgressBar.Maximum = 100;
                MainProgressBar.Value = 0;
            });
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _selectedFolder = dialog.SelectedPath;

            // ⚡ NO pre-counting - avoids freeze on large folders
            // We'll use a pulsing progress bar instead of percentage

            Dispatcher.Invoke(() =>
            {
                FolderPathEntry.Text = _selectedFolder;
                StartStopButton.IsEnabled = true;
                OutputText.Clear();
                OutputText.AppendText($"📁 Selected folder: {_selectedFolder}\n");
                OutputText.AppendText($"🔍 Will scan for: {string.Join(", ", _targetFiles)} + login folders\n\n");

                // Reset stats
                HitLabel.Text = "Hits: 0";
                FilesScannedLabel.Text = "0";
                StatusLabel.Text = "Ready to start";
                CurrentFileLabel.Text = "—";
                LinesProcessedLabel.Text = "0";
                ElapsedLabel.Text = "00:00:00";

                // Reset progress bar to idle state
                MainProgressBar.IsIndeterminate = false;
                MainProgressBar.Value = 0;
            });

            lock (_lockObj)
            {
                _results.Clear();
                _seenUrls.Clear();
                _totalHits = 0;
                _processedFiles = 0;
                _totalLinesProcessed = 0;
            }

            while (_uiLogQueue.TryDequeue(out _)) { }
            while (_hitQueue.TryDequeue(out _)) { }
        }

        private async void StartStop_Click(object sender, RoutedEventArgs e)
        {
            if (StartStopButton.Content.ToString() == "▶ Start")
            {
                if (string.IsNullOrEmpty(_selectedFolder))
                {
                    OutputText.AppendText("⚠️ Please select a folder first.\n");
                    return;
                }

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Results To...",
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = $"TicniO_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt",
                    InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "ULP")
                };

                var defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Core", "ULP");
                Directory.CreateDirectory(defaultDir);

                bool? result = saveDialog.ShowDialog();

                if (result != true)
                {
                    OutputText.AppendText("❌ Save cancelled by user.\n");
                    return;
                }

                _saveFilePath = saveDialog.FileName;
                _cts = new CancellationTokenSource();
                _isRunning = true;
                _startTime = DateTime.Now;
                _progressPulse = 0;
                _progressForward = true;

                Dispatcher.Invoke(() =>
                {
                    StartStopButton.Content = "⏹ Stop";
                    StartStopButton.Style = (Style)Application.Current.Resources["StyledDangerButton"];
                    OpenButton.IsEnabled = false;
                    OutputText.AppendText($"🚀 Starting scan...\n");
                    OutputText.AppendText($"💾 Saving to: {_saveFilePath}\n\n");
                    StatusLabel.Text = "Processing...";

                    // Start pulsing progress bar (indeterminate style without pre-count)
                    MainProgressBar.IsIndeterminate = false;
                    MainProgressBar.Minimum = 0;
                    MainProgressBar.Maximum = 100;
                    MainProgressBar.Value = 0;
                });

                // Start both timers for real-time updates
                _logFlushTimer.Start();
                _statsUpdateTimer.Start();

                await Task.Run(() => WorkerThread_Process(_cts.Token));
            }
            else
            {
                _cts?.Cancel();

                Dispatcher.Invoke(() =>
                {
                    StartStopButton.IsEnabled = false;
                    OutputText.AppendText("🛑 Stopping...\n");
                    StatusLabel.Text = "Stopping...";
                });
            }
        }

        private void WorkerThread_Process(CancellationToken token)
        {
            try
            {
                _currentSavePath = _saveFilePath;

                var directory = Path.GetDirectoryName(_currentSavePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                QueueLog($"📍 Output file: {_currentSavePath}\n\n");

                SearchAndProcessFiles(token);

                if (!token.IsCancellationRequested)
                {
                    QueueLog($"\n✅ Done! Processed {_processedFiles} files.\n");
                    QueueLog($"📊 Total hits: {_totalHits}\n");
                    QueueLog($"💾 Results saved to:\n{_currentSavePath}\n");

                    Dispatcher.Invoke(() =>
                    {
                        OutputText.AppendText("\n💡 Tip: Right-click the file path above to open its folder.\n");
                    });
                }
                else
                {
                    QueueLog($"\n🔴 Stopped. Processed {_processedFiles} files, {_totalHits} hits saved.\n");
                    QueueLog($"💾 Partial results saved to:\n{_currentSavePath}\n");
                }
            }
            catch (OperationCanceledException)
            {
                QueueLog("\n🔴 Operation cancelled.\n");
            }
            catch (Exception ex)
            {
                QueueLog($"❌ ERROR: {ex.Message}\n");
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    _logFlushTimer.Stop();
                    _statsUpdateTimer.Stop();
                    FlushUiUpdates();
                    UpdateStatsDisplay(); // Final update
                    FinishOperation();
                });
            }
        }

        private void SearchAndProcessFiles(CancellationToken token)
        {
            try
            {
                // ⚡ Enumerate without counting first - no freeze!
                var files = Directory.EnumerateFiles(_selectedFolder, "*.txt", SearchOption.AllDirectories);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                foreach (var file in files)
                {
                    if (token.IsCancellationRequested)
                    {
                        QueueLog("🛑 Cancellation requested - stopping.\n");
                        return;
                    }

                    var fileName = Path.GetFileName(file).ToLower();
                    var dirName = Path.GetDirectoryName(file)?.ToLower() ?? "";

                    // Filter to target files only
                    if (!_targetFiles.Contains(fileName) &&
                        !(dirName.Contains("login") && fileName.EndsWith(".txt")))
                        continue;

                    try
                    {
                        // Update current file display BEFORE processing
                        _currentFileName = Path.GetFileName(file);

                        ProcessFile(file, token);

                        // Increment AFTER successful processing (thread-safe)
                        Interlocked.Increment(ref _processedFiles);

                        // Log progress periodically (don't spam UI)
                        int currentProcessed = _processedFiles;
                        if (currentProcessed % 50 == 0)
                        {
                            var elapsed = stopwatch.Elapsed.TotalSeconds;
                            var filesPerSec = elapsed > 0 ? currentProcessed / elapsed : 0;
                            QueueLog($"📄 Processed {currentProcessed:N0} files... {_totalHits:N0} hits ({filesPerSec:F1}/sec)\n");
                        }

                        // Small yield to keep UI responsive
                        if (currentProcessed % 100 == 0)
                        {
                            Thread.Sleep(5);
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }

                var totalTime = stopwatch.Elapsed;
                QueueLog($"\n✅ Completed! Processed {_processedFiles:N0} files in {totalTime:mm\\:ss}\n");
            }
            catch (UnauthorizedAccessException)
            {
                QueueLog("⚠️ Some folders were inaccessible (skipped).\n");
            }
            catch (Exception ex)
            {
                QueueLog($"❌ Enumeration error: {ex.Message}\n");
            }
        }

        private void ProcessFile(string filePath, CancellationToken token)
        {
            string url = null, user = null, passwd = null;
            int localLines = 0;

            string[] lines;
            try
            {
                using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8, true))
                {
                    lines = reader.ReadToEnd().Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                }
            }
            catch
            {
                return;
            }

            foreach (var line in lines)
            {
                if (token.IsCancellationRequested) return;

                localLines++;

                // Update lines counter periodically (batched for performance)
                if (localLines % 1000 == 0)
                {
                    Interlocked.Add(ref _totalLinesProcessed, 1000);
                }

                var lower = line.ToLowerInvariant();

                if (lower.Contains("url") && url == null)
                    url = SafeSplit(line, ':');
                else if (lower.Contains("user") && user == null)
                    user = SafeSplit(line, ':');
                else if (lower.Contains("pass") && passwd == null)
                    passwd = SafeSplit(line, ':');

                if (url != null && user != null && passwd != null)
                {
                    var result = $"{url}:{user}:{passwd}";

                    if (result.ToLower().StartsWith("android:") ||
                        result.Count(c => c == ' ') >= 10 ||
                        result.Contains("[NOT_SAVED]"))
                    {
                        url = user = passwd = null;
                        continue;
                    }

                    lock (_lockObj)
                    {
                        if (!_seenUrls.Contains(url))
                        {
                            _seenUrls.Add(url);
                            _results.Add(result);
                            _totalHits++;

                            File.AppendAllText(_currentSavePath, result + Environment.NewLine);
                            _hitQueue.Enqueue(result);
                        }
                    }

                    url = user = passwd = null;
                }
            }

            // Add remaining lines from last batch
            Interlocked.Add(ref _totalLinesProcessed, localLines % 1000);
        }

        private string SafeSplit(string line, char delimiter)
        {
            var parts = line.Split(new[] { delimiter }, 2, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1].Trim() : null;
        }

        private void QueueLog(string message)
        {
            _uiLogQueue.Enqueue(message);
        }

        /// <summary>
        /// Flushes pending log messages and hits to the UI (called by _logFlushTimer)
        /// </summary>
        private void FlushUiUpdates()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(FlushUiUpdates, DispatcherPriority.Render);
                return;
            }

            var contentToAdd = new System.Text.StringBuilder(512);
            bool hasContent = false;

            // Batch all pending log messages
            while (_uiLogQueue.TryDequeue(out var log))
            {
                contentToAdd.Append(log);
                hasContent = true;
            }

            // Batch all pending hits
            var hits = new List<string>();
            while (_hitQueue.TryDequeue(out var hit))
            {
                hits.Add(hit);
            }

            if (hits.Count > 0)
            {
                // Show only last 10 hits to avoid flooding UI
                var displayHits = hits.Skip(Math.Max(0, hits.Count - 10));
                foreach (var hit in displayHits)
                {
                    contentToAdd.AppendLine(hit);
                }

                // Update hit counter
                HitLabel.Text = $"Hits: {_totalHits}";
                hasContent = true;
            }

            // Append all content at once + scroll once
            if (hasContent)
            {
                OutputText.AppendText(contentToAdd.ToString());

                Dispatcher.InvokeAsync(() =>
                {
                    try { OutputScrollViewer?.ScrollToEnd(); }
                    catch { }
                }, DispatcherPriority.Render);
            }
        }

        /// <summary>
        /// Updates stats labels and progress animation in real-time (called by _statsUpdateTimer)
        /// No pre-counting needed - uses pulse animation for progress
        /// </summary>
        private void UpdateStatsDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateStatsDisplay, DispatcherPriority.Background);
                return;
            }

            // Update elapsed time
            var elapsed = DateTime.Now - _startTime;
            ElapsedLabel.Text = elapsed.ToString(@"hh\:mm\:ss");

            // Update files scanned (real-time from background thread - int reads are atomic)
            FilesScannedLabel.Text = _processedFiles.ToString("N0");

            // Update current file being processed (truncate if too long)
            if (!string.IsNullOrEmpty(_currentFileName))
            {
                CurrentFileLabel.Text = _currentFileName.Length > 30
                    ? "..." + _currentFileName.Substring(_currentFileName.Length - 27)
                    : _currentFileName;
            }

            // Update lines processed
            LinesProcessedLabel.Text = _totalLinesProcessed.ToString("N0");

            // ⚡ Progress bar: Pulse animation since we don't know total files
            if (_isRunning)
            {
                // Smooth back-and-forth pulse (0→100→0)
                if (_progressForward)
                {
                    _progressPulse += 3;
                    if (_progressPulse >= 100)
                    {
                        _progressPulse = 100;
                        _progressForward = false;
                    }
                }
                else
                {
                    _progressPulse -= 3;
                    if (_progressPulse <= 0)
                    {
                        _progressPulse = 0;
                        _progressForward = true;
                    }
                }
                MainProgressBar.Value = _progressPulse;

                // Update status with processing speed
                var filesPerSec = elapsed.TotalSeconds > 0 ? _processedFiles / elapsed.TotalSeconds : 0;
                StatusLabel.Text = $"⚡ {filesPerSec:F1} files/sec | 📄 {_processedFiles:N0} scanned";
            }
            else if (MainProgressBar.Value < 100)
            {
                // Finished - fill progress bar
                MainProgressBar.Value = 100;
            }
        }

        private void FinishOperation()
        {
            _isRunning = false;

            // Final stats update
            HitLabel.Text = $"Hits: {_totalHits}";
            FilesScannedLabel.Text = _processedFiles.ToString("N0");
            LinesProcessedLabel.Text = _totalLinesProcessed.ToString("N0");
            StatusLabel.Text = _cts?.IsCancellationRequested == true ? "⏹️ Stopped" : "✅ Done";

            // Reset button state
            StartStopButton.Content = "▶ Start";
            StartStopButton.Style = (Style)Application.Current.Resources["StyledSuccessButton"];
            StartStopButton.IsEnabled = !string.IsNullOrEmpty(_selectedFolder);
            OpenButton.IsEnabled = true;

            // Complete progress bar
            MainProgressBar.Value = 100;
        }
    }
}
