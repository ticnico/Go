using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class KeywordRemoverPage : Page
    {
        private CancellationTokenSource _cancellationTokenSource;
        private string _filePath;
        private string _folderPath;
        private List<string> _keywords = new List<string>();
        private int _totalHits;
        private readonly object _lockObj = new object();
        private bool _isProcessing = false;

        // ⏱️ Elapsed time tracking
        private DateTime _startTime;
        private DispatcherTimer _elapsedTimer;

        // 📁 Keyword file size limit: 5MB
        private const long MAX_KEYWORD_FILE_SIZE_BYTES = 5 * 1024 * 1024; // 5MB

        public KeywordRemoverPage()
        {
            InitializeComponent();
            InitializeElapsedTimer();
            ResetUIState();
        }

        // ⏱️ Timer for real-time elapsed time updates
        private void InitializeElapsedTimer()
        {
            _elapsedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _elapsedTimer.Tick += (s, e) =>
            {
                if (_isProcessing)
                {
                    var elapsed = DateTime.Now - _startTime;
                    ElapsedLabel.Text = elapsed.ToString(@"hh\:mm\:ss");
                }
            };
        }

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            });
        }

        private void ResetUIState()
        {
            Dispatcher.Invoke(() =>
            {
                _isProcessing = false;
                StartButton.Content = "Start Cleaning";
                StartButton.Style = (Style)Application.Current.Resources["StyledSuccessButton"];
                StartButton.IsEnabled = true;

                // Reset status and elapsed labels
                StatusLabel.Text = "Idle";
                ElapsedLabel.Text = "00:00:00";
            });
        }

        private void SetProcessingState()
        {
            Dispatcher.Invoke(() =>
            {
                _isProcessing = true;
                _startTime = DateTime.Now;
                StartButton.Content = "Stop Cleaning";
                StartButton.Style = (Style)Application.Current.Resources["StyledDangerButton"];

                // Update status and start elapsed timer
                StatusLabel.Text = "Processing...";
                _elapsedTimer.Start();
            });
        }

        private void SetStoppingState()
        {
            Dispatcher.Invoke(() =>
            {
                StartButton.Content = "Saving...";
                StartButton.Style = (Style)Application.Current.Resources["StyledWarningButton"];
                StartButton.IsEnabled = false;

                // Update status
                StatusLabel.Text = "Stopping...";
            });
        }

        private void LoadKeywordsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (dialog.ShowDialog() != true) return;

            try
            {
                // 📁 CHECK FILE SIZE LIMIT (5MB)
                var fileInfo = new FileInfo(dialog.FileName);

                if (fileInfo.Length > MAX_KEYWORD_FILE_SIZE_BYTES)
                {
                    var sizeMB = fileInfo.Length / 1024.0 / 1024.0;
                    MessageBox.Show(
                        $"Keyword file is too large!\n\n" +
                        $"File size: {sizeMB:F2} MB\n" +
                        $"Maximum allowed: 5 MB\n\n" +
                        $"Please use a smaller file or split your keywords into multiple files.",
                        "File Size Limit Exceeded",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    LogMessage($"❌ Keyword file rejected: {fileInfo.Name} ({sizeMB:F2} MB > 5 MB limit)");
                    return;
                }

                var lines = File.ReadAllLines(dialog.FileName);
                _keywords = lines.Select(l => l.Trim().ToLower()).Where(l => !string.IsNullOrEmpty(l)).ToList();

                KeywordEntry.Text = string.Join(", ", _keywords.Take(100)) + (_keywords.Count > 100 ? " ..." : "");

                var sizeKB = fileInfo.Length / 1024.0;
                LogMessage($"📥 Loaded {_keywords.Count:N0} keywords from {fileInfo.Name} ({sizeKB:F1} KB)");
                MessageBox.Show($"{_keywords.Count:N0} keywords loaded.", "Keywords Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Failed to load keywords: {ex.Message}");
                MessageBox.Show($"Failed to load keywords:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (dialog.ShowDialog() != true) return;

            _filePath = dialog.FileName;
            _folderPath = null;
            FilePathEntry.Text = _filePath;
        }

        private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _folderPath = dialog.SelectedPath;
            _filePath = null;
            FilePathEntry.Text = $"[Folder] {_folderPath}";

            var txtFiles = Directory.GetFiles(_folderPath, "*.txt");
            FileCounterLabel.Text = $"Files processed: 0 / {txtFiles.Length}";
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isProcessing)
            {
                // Start processing
                if (string.IsNullOrEmpty(_filePath) && string.IsNullOrEmpty(_folderPath))
                {
                    MessageBox.Show("Please select a .txt file or a folder.", "No File or Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var entryText = KeywordEntry.Text.Trim().ToLower();
                if (string.IsNullOrEmpty(entryText))
                {
                    MessageBox.Show("Please enter a keyword or load a keyword list.", "No Keyword", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _keywords = entryText.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
                if (!_keywords.Any())
                {
                    MessageBox.Show("Please enter at least one valid keyword.", "No Keyword", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SetProcessingState();

                _cancellationTokenSource = new CancellationTokenSource();
                _totalHits = 0;

                try
                {
                    if (_folderPath != null)
                        await Task.Run(() => CleanFolder(_cancellationTokenSource.Token));
                    else
                        await Task.Run(() => CleanFile(_filePath, _cancellationTokenSource.Token));
                }
                catch (OperationCanceledException)
                {
                    LogMessage("Cleaning stopped.");
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] {ex.Message}");
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                    ResetUIState();
                }
            }
            else
            {
                // Stop processing
                LogMessage("🛑 Stop requested. Cancelling operations...");
                SetStoppingState();

                try
                {
                    _cancellationTokenSource?.Cancel();
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] {ex.Message}");
                    ResetUIState();
                }
            }
        }

        private void CleanFolder(CancellationToken token)
        {
            var txtFiles = Directory.GetFiles(_folderPath, "*.txt");
            var totalFiles = txtFiles.Length;

            LogMessage($"Found {totalFiles} .txt file(s) in folder.");

            for (int i = 0; i < txtFiles.Length; i++)
            {
                if (token.IsCancellationRequested) break;

                var currentFile = txtFiles[i];

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    FilePathEntry.Text = currentFile;
                    FileCounterLabel.Text = $"Files processed: {i} / {totalFiles}";

                    // Update status with progress
                    StatusLabel.Text = $"Processing: {i + 1}/{totalFiles}";
                }));

                CleanFile(currentFile, token); // Process one file at a time

                Dispatcher.BeginInvoke(new Action(() =>
                    FileCounterLabel.Text = $"Files processed: {i + 1} / {totalFiles}"));
            }

            if (!token.IsCancellationRequested)
            {
                LogMessage("Batch cleaning completed.");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusLabel.Text = "✅ Done";
                    MessageBox.Show($"Finished processing {totalFiles} files.", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }));
            }
        }

        private void CleanFile(string filePath, CancellationToken token)
        {
            try
            {
                LogMessage($"Processing: {filePath}");

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileName.EndsWith("_Core"))
                {
                    LogMessage($"Skipping already processed file: {fileName}");
                    return;
                }

                var tempPath = filePath + ".tmp";
                var hitCount = 0;
                var keptCount = 0;
                long lastValidPosition = 0;
                var lastUiUpdate = DateTime.MinValue;
                var uiUpdateInterval = TimeSpan.FromMilliseconds(100);

                var fileInfo = new FileInfo(filePath);
                var totalBytes = fileInfo.Length;
                bool wasCancelled = false;

                using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8, true, 8192))
                using (var writer = new StreamWriter(tempPath, false, System.Text.Encoding.UTF8, 8192))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lastValidPosition = reader.BaseStream.Position;

                        if (token.IsCancellationRequested)
                        {
                            wasCancelled = true;
                            LogMessage($"🛑 Stop requested. Saving progress via byte offset...");
                            break;
                        }

                        var lineLower = line.ToLower();
                        var containsKeyword = _keywords.Any(k => lineLower.Contains(k));
                        var hasUnicode = line.Any(c => c > 127);

                        if (!containsKeyword || hasUnicode)
                        {
                            writer.WriteLine(line);
                            keptCount++;
                        }
                        else
                        {
                            hitCount++;
                            lock (_lockObj) { _totalHits++; }
                        }

                        var bytesRead = lastValidPosition;
                        if (keptCount % 100 == 0 || reader.EndOfStream)
                        {
                            var now = DateTime.Now;
                            if (now - lastUiUpdate >= uiUpdateInterval)
                            {
                                lastUiUpdate = now;
                                var progress = totalBytes > 0 ? Math.Min(99.9, (bytesRead / (double)totalBytes) * 100) : 0;
                                var currentHits = _totalHits;

                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    ProgressBar.Value = progress;
                                    ProgressLabel.Text = $"{progress:F1}%";
                                    LineCounterLabel.Text = $"Lines kept: {keptCount} | Removed: {hitCount}";
                                    HitCounterLabel.Text = $"Total Hits: {currentHits}";
                                    FilePathEntry.Text = filePath;

                                    // Update elapsed time inline during heavy processing
                                    var elapsed = DateTime.Now - _startTime;
                                    ElapsedLabel.Text = elapsed.ToString(@"hh\:mm\:ss");
                                }));
                            }
                        }
                    }
                }

                if (wasCancelled && File.Exists(tempPath))
                {
                    LogMessage($"📦 Fast-saving: copying {totalBytes - lastValidPosition:N0} bytes from offset {lastValidPosition:N0}...");

                    using (var originalStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
                    using (var tempStream = new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                    {
                        originalStream.Seek(lastValidPosition, SeekOrigin.Begin);
                        var buffer = new byte[1024 * 1024];
                        int bytesRead;
                        while ((bytesRead = originalStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            tempStream.Write(buffer, 0, bytesRead);
                        }
                    }

                    LogMessage($"✓ Progress saved instantly. Resuming from byte {lastValidPosition:N0}.");
                }

                if (File.Exists(tempPath))
                {
                    File.Delete(filePath);
                    File.Move(tempPath, filePath);

                    if (!wasCancelled)
                    {
                        LogMessage($"✓ Finished: {Path.GetFileName(filePath)} | Kept: {keptCount} | Removed: {hitCount}");
                    }
                    else
                    {
                        LogMessage($"⚠ Stopped early: {Path.GetFileName(filePath)} | Byte offset: {lastValidPosition:N0}/{totalBytes:N0} | Kept: {keptCount} | Removed: {hitCount}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] {ex.Message}");
                var tempPath = filePath + ".tmp";
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusLabel.Text = "❌ Error";
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }));
            }
        }
    }
}
