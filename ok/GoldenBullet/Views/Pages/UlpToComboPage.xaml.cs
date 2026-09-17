using Core.Entities;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using Microsoft.Win32;
using RuriLib.Models.Environment;
using RuriLib.Services;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class UlpToComboPage : Page
    {
        private readonly Dictionary<string, string> _predefinedRegexes = new()
        {
            ["PHONE"] = @"[\+0-9]{10,}:[a-zA-Z0-9.#$!%*()&+^<>/_?,\=""'~@-]+",
            ["E-MAIL"] = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]{2,}:\S+",
            ["USERNAME"] = @"(?<=:)[a-zA-Z0-9._%#+-]+:[a-zA-Z0-9.#$!%*()&+^<>/_?,\=""'~@-]+$",
            ["KEYWORDS"] = @"https?://(?:www\.)?([a-zA-Z0-9.-]+)",
            ["MAC"] = @"(?:[0-9A-Fa-f]{2}[:-]){5}(?:[0-9A-Fa-f]{2})"
        };

        private List<string> _selectedFilePaths = new();
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly DispatcherTimer _logTimer;
        private readonly DispatcherTimer _progressTimer;
        private const int LOG_BATCH_MS = 100;
        private long _hitCount;
        private long _failCount;
        private volatile bool _isProcessing;

        private DateTime _startTime;
        private int _currentFileIndex;
        private int _totalFiles;

        private ConcurrentDictionary<string, string> _wordlistFiles;
        private readonly EnvironmentSettings _env;
        private readonly WordlistsViewModel _wordlistsVm;

        public UlpToComboPage()
        {
            InitializeComponent();
            _env = SP.GetService<RuriLibSettingsService>().Environment;
            _wordlistsVm = SP.GetService<ViewModelsService>().Wordlists;

            _logTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LOG_BATCH_MS) };
            _logTimer.Tick += (s, e) => FlushLogs();

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _progressTimer.Tick += ProgressTimer_Tick;

            // Initialize UI state based on default checkbox states
            UpdateUIState();
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = DateTime.Now - _startTime;
            ElapsedLabel.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            if (_totalFiles > 0 && _currentFileIndex >= 0)
            {
                var progress = Math.Min(100, (_currentFileIndex * 100) / _totalFiles);
                MainProgressBar.Value = progress;
            }
        }

        private void Log(string message)
        {
            _logQueue.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void FlushLogs()
        {
            if (_logQueue.IsEmpty) return;
            var sb = new StringBuilder();
            while (_logQueue.TryDequeue(out var msg))
                sb.AppendLine(msg);
            LogTextBox.AppendText(sb.ToString());
            LogTextBox.ScrollToEnd();
        }

        private void UpdateStats()
        {
            HitsLabel.Text = $"Hits: {Interlocked.Read(ref _hitCount)}";
            FailsLabel.Text = $"Fails: {Interlocked.Read(ref _failCount)}";
        }

        private void SetUIState(bool processing)
        {
            _isProcessing = processing;
            var enabled = !processing;
            StartButton.Content = processing ? "⏹ Stop Processing" : "▶ Start Processing";
            StartButton.Style = (Style)Application.Current.Resources[
                processing ? "StyledDangerButton" : "StyledSuccessButton"
            ];
            StartButton.IsEnabled = true;
            SelectButton.IsEnabled = enabled;
            LoadKeywordsButton.IsEnabled = enabled;
            KeywordsTextBox.IsEnabled = enabled;

            PhoneCheckBox.IsEnabled = enabled;
            EmailCheckBox.IsEnabled = enabled;
            UsernameCheckBox.IsEnabled = enabled;
            MacCheckBox.IsEnabled = enabled;
            KeywordsCheckBox.IsEnabled = enabled;
            FileRadioButton.IsEnabled = enabled;
            FolderRadioButton.IsEnabled = enabled;

            // When processing stops, restore the correct enabled/disabled states based on checkboxes
            if (!processing)
            {
                UpdateUIState();
            }
        }

        // --- Checkbox Mutual Exclusion Logic ---
        private void Checkbox_Changed(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;

            bool isChecked = checkBox.IsChecked == true;

            if (isChecked)
            {
                // If a Group 1 checkbox is checked, uncheck all others
                if (checkBox == MacCheckBox || checkBox == KeywordsCheckBox)
                {
                    PhoneCheckBox.IsChecked = false;
                    EmailCheckBox.IsChecked = false;
                    UsernameCheckBox.IsChecked = false;
                    if (checkBox == MacCheckBox) KeywordsCheckBox.IsChecked = false;
                    if (checkBox == KeywordsCheckBox) MacCheckBox.IsChecked = false;
                }
                else // If a Group 2 checkbox is checked, uncheck Group 1
                {
                    MacCheckBox.IsChecked = false;
                    KeywordsCheckBox.IsChecked = false;
                }
            }

            UpdateUIState();
        }

        private void UpdateUIState()
        {
            bool macChecked = MacCheckBox.IsChecked == true;
            bool keywordsChecked = KeywordsCheckBox.IsChecked == true;
            bool phoneChecked = PhoneCheckBox.IsChecked == true;
            bool emailChecked = EmailCheckBox.IsChecked == true;
            bool usernameChecked = UsernameCheckBox.IsChecked == true;

            bool group2Checked = phoneChecked || emailChecked || usernameChecked;

            // Disable conflicting checkboxes
            PhoneCheckBox.IsEnabled = !macChecked && !keywordsChecked;
            EmailCheckBox.IsEnabled = !macChecked && !keywordsChecked;
            UsernameCheckBox.IsEnabled = !macChecked && !keywordsChecked;

            MacCheckBox.IsEnabled = !group2Checked && !keywordsChecked;
            KeywordsCheckBox.IsEnabled = !group2Checked && !macChecked;

            // Keyword input is enabled by default, ONLY disabled if MAC or KEYWORDS is checked
            bool keywordInputEnabled = !(macChecked || keywordsChecked);
            KeywordsTextBox.IsEnabled = keywordInputEnabled;
            LoadKeywordsButton.IsEnabled = keywordInputEnabled;

            // Visual feedback for disabled state
            KeywordsTextBox.Opacity = keywordInputEnabled ? 1.0 : 0.6;
            LoadKeywordsButton.Opacity = keywordInputEnabled ? 1.0 : 0.6;
        }
        // --------------------------------------------

        private void LoadKeywordsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var fileInfo = new FileInfo(dialog.FileName);
                const long MAX_SIZE_MB = 5;
                const long MAX_BYTES = MAX_SIZE_MB * 1024 * 1024;

                if (fileInfo.Length > MAX_BYTES)
                {
                    Log($"❌ File too large: {fileInfo.Length / 1024 / 1024}MB (max {MAX_SIZE_MB}MB)");
                    MessageBox.Show($"File is too large!\nSize: {fileInfo.Length / 1024 / 1024}MB\nMax allowed: {MAX_SIZE_MB}MB",
                        "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var content = File.ReadAllText(dialog.FileName);
                var keywords = content.Contains(',')
                    ? content.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k))
                    : content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim());

                KeywordsTextBox.Text = string.Join(", ", keywords);
                Log($"📥 Loaded {keywords.Count()} keyword(s) ({fileInfo.Length / 1024}KB)");
            }
            catch (Exception ex)
            {
                Log($"❌ Failed to load keywords: {ex.Message}");
                MessageBox.Show($"Could not load keywords:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedFilePaths.Clear();
            if (FileRadioButton.IsChecked == true)
            {
                var dialog = new OpenFileDialog { Filter = "Text Files|*.txt", Multiselect = true };
                if (dialog.ShowDialog() == true)
                {
                    _selectedFilePaths.AddRange(dialog.FileNames);
                    Dispatcher.Invoke(() =>
                    {
                        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}]  Selected {_selectedFilePaths.Count} file(s)" + Environment.NewLine);
                        LogTextBox.ScrollToEnd();
                    });
                }
            }
            else
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var files = Directory.GetFiles(dialog.SelectedPath, "*.txt", SearchOption.TopDirectoryOnly);
                    _selectedFilePaths.AddRange(files);
                    Dispatcher.Invoke(() =>
                    {
                        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 📂 Selected folder: {dialog.SelectedPath}" + Environment.NewLine);
                        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 📄 {files.Length} .txt file(s) found" + Environment.NewLine);
                        LogTextBox.ScrollToEnd();
                    });
                }
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing)
            {
                _cts?.Cancel();
                StartButton.IsEnabled = false;
                return;
            }

            if (!_selectedFilePaths.Any())
            {
                MessageBox.Show("Please select file or folder first.", "No Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Separate patterns into those that apply to ALL lines (MAC) and those that require keywords
            var allLinePatterns = new List<Regex>();
            if (MacCheckBox.IsChecked == true)
                allLinePatterns.Add(new Regex(_predefinedRegexes["MAC"], RegexOptions.Compiled | RegexOptions.IgnoreCase));

            var keywordPatterns = new List<Regex>();
            if (PhoneCheckBox.IsChecked == true) keywordPatterns.Add(new Regex(_predefinedRegexes["PHONE"], RegexOptions.Compiled | RegexOptions.IgnoreCase));
            if (EmailCheckBox.IsChecked == true) keywordPatterns.Add(new Regex(_predefinedRegexes["E-MAIL"], RegexOptions.Compiled | RegexOptions.IgnoreCase));
            if (UsernameCheckBox.IsChecked == true) keywordPatterns.Add(new Regex(_predefinedRegexes["USERNAME"], RegexOptions.Compiled | RegexOptions.IgnoreCase));
            if (KeywordsCheckBox.IsChecked == true) keywordPatterns.Add(new Regex(_predefinedRegexes["KEYWORDS"], RegexOptions.Compiled | RegexOptions.IgnoreCase));

            if (!allLinePatterns.Any() && !keywordPatterns.Any())
            {
                MessageBox.Show("Please select at least one pattern.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool keywordsOnly = KeywordsCheckBox.IsChecked == true;
            List<string> keywords = new();

            // Only require keywords if we are using keywordPatterns and it's not the "keywordsOnly" (URL extraction) mode
            if (keywordPatterns.Any() && !keywordsOnly)
            {
                var all = KeywordsTextBox.Text.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
                keywords = all.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (all.Count != keywords.Count)
                    Log($"⚠️ Removed {all.Count - keywords.Count} duplicates");

                if (!keywords.Any())
                {
                    MessageBox.Show("Please enter at least one keyword.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Determine Wordlist Type dynamically
            string wordlistType = "Credentials";
            if (MacCheckBox.IsChecked == true)
            {
                wordlistType = "MAC";
            }

            SetUIState(true);
            _logTimer.Start();

            _startTime = DateTime.Now;
            _currentFileIndex = 0;
            _totalFiles = _selectedFilePaths.Count;
            StatusLabel.Text = "Starting...";
            StatusLabel.Foreground = (SolidColorBrush)FindResource("ForegroundWarning");
            MainProgressBar.Value = 0;
            ElapsedLabel.Text = "00:00:00";
            _progressTimer.Start();

            _cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _hitCount, 0);
            Interlocked.Exchange(ref _failCount, 0);
            UpdateStats();
            _wordlistFiles = new ConcurrentDictionary<string, string>();

            bool wasCancelled = false;
            try
            {
                await Task.Run(() => ProcessFiles(allLinePatterns, keywordPatterns, keywords, keywordsOnly, _cts.Token), _cts.Token);

                StatusLabel.Text = "Completed";
                StatusLabel.Foreground = (SolidColorBrush)FindResource("ForegroundGood");
                MainProgressBar.Value = 100;
                Log("✅ Processing complete");
            }
            catch (OperationCanceledException)
            {
                Log("⏹️ Stopped by user - saving partial results...");
                StatusLabel.Text = "Stopped";
                StatusLabel.Foreground = (SolidColorBrush)FindResource("ForegroundBad");
                wasCancelled = true;
            }
            catch (Exception ex)
            {
                Log($"❌ Error: {ex.Message}");
                StatusLabel.Text = "Error";
                StatusLabel.Foreground = (SolidColorBrush)FindResource("ForegroundBad");
                MessageBox.Show($"Error:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _logTimer.Stop();
                _progressTimer.Stop();
                FlushLogs();

                if (_wordlistFiles?.Any() == true)
                {
                    await CreateWordlistsAsync(_wordlistFiles, wasCancelled, wordlistType);
                }
                _cts?.Dispose();
                _cts = null;
                SetUIState(false);

                if (wasCancelled)
                {
                    MessageBox.Show($"Stopped!\nHits: {Interlocked.Read(ref _hitCount)}\nFails: {Interlocked.Read(ref _failCount)}\nPartial results saved to wordlists.",
                        "Stopped", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void ProcessFiles(List<Regex> allLinePatterns, List<Regex> keywordPatterns, List<string> keywords, bool keywordsOnly, CancellationToken token)
        {
            var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData", "Wordlists");
            Directory.CreateDirectory(outputDir);

            var writers = new ConcurrentDictionary<string, StreamWriter>();
            var seenLines = new ConcurrentDictionary<string, ConcurrentHashSet<string>>();

            try
            {
                // Initialize output files for ALL LINE patterns (e.g., MAC)
                if (allLinePatterns.Any())
                {
                    var path = Path.Combine(outputDir, "MAC_TicnicO.txt");
                    SafeDelete(path);
                    writers["MAC"] = new StreamWriter(path, false, Encoding.UTF8, 65536);
                    // Use case-insensitive comparer so AA:BB and aa:bb are treated as the same MAC
                    seenLines["MAC"] = new ConcurrentHashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _wordlistFiles["MAC"] = path;
                    Log($"[MAC] Output ready");
                }

                // Initialize output files for KEYWORD patterns
                if (keywordsOnly)
                {
                    var path = Path.Combine(outputDir, "KEYWORDS_TicnicO.txt");
                    SafeDelete(path);
                    writers["KEYWORDS"] = new StreamWriter(path, false, Encoding.UTF8, 65536);
                    seenLines["KEYWORDS"] = new ConcurrentHashSet<string>();
                    _wordlistFiles["KEYWORDS"] = path;
                    Log($"[KEYWORDS] Output ready");
                }
                else if (keywordPatterns.Any())
                {
                    foreach (var kw in keywords)
                    {
                        var safeName = Regex.Replace(kw, @"[^a-zA-Z0-9.-]+", "_");
                        var path = Path.Combine(outputDir, $"{safeName}_TicnicO.txt");
                        SafeDelete(path);
                        writers[kw] = new StreamWriter(path, false, Encoding.UTF8, 65536);
                        seenLines[kw] = new ConcurrentHashSet<string>();
                        _wordlistFiles[kw] = path;
                    }
                    Log($"📁 {keywords.Count} output files ready");
                }

                _totalFiles = _selectedFilePaths.Count;
                _currentFileIndex = 0;

                foreach (var filepath in _selectedFilePaths)
                {
                    if (token.IsCancellationRequested)
                    {
                        Log("⚠️ Cancellation requested - finishing current file...");
                        throw new OperationCanceledException();
                    }

                    _currentFileIndex++;
                    var fileName = Path.GetFileName(filepath);

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        StatusLabel.Text = $"Processing: {fileName}";
                        var progress = Math.Min(100, (_currentFileIndex * 100) / _totalFiles);
                        MainProgressBar.Value = progress;
                    }), DispatcherPriority.Background);

                    Log($"📄 Processing: {fileName}");
                    ProcessFile(filepath, allLinePatterns, keywordPatterns, keywords, keywordsOnly, writers, seenLines, token);
                }
            }
            finally
            {
                Log("🔒 Closing files...");
                foreach (var w in writers.Values)
                {
                    try { w.Flush(); w.Dispose(); } catch { }
                }
            }
        }

        private void ProcessFile(string filepath, List<Regex> allLinePatterns, List<Regex> keywordPatterns,
            List<string> keywords, bool keywordsOnly,
            ConcurrentDictionary<string, StreamWriter> writers,
            ConcurrentDictionary<string, ConcurrentHashSet<string>> seen, CancellationToken token)
        {
            using var reader = new StreamReader(filepath, Encoding.UTF8, true, 65536);
            string line;
            int lineNum = 0;
            int localHits = 0;
            int localFails = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (token.IsCancellationRequested) return;
                lineNum++;

                // 1. Process patterns that apply to ALL lines (e.g., MAC)
                foreach (var pattern in allLinePatterns)
                {
                    foreach (Match match in pattern.Matches(line))
                    {
                        var extracted = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                        if (seen["MAC"].TryAdd(extracted))
                        {
                            writers["MAC"].WriteLine(extracted);
                            localHits++;
                        }
                    }
                }

                // 2. Process patterns that require keywords
                if (keywordPatterns.Any())
                {
                    if (keywordsOnly)
                    {
                        // Extract URLs from all lines
                        foreach (var pattern in keywordPatterns)
                        {
                            foreach (Match match in pattern.Matches(line))
                            {
                                var extracted = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                                var clean = extracted.Replace("https://", "").Replace("http://", "").Replace("www.", "").Trim('/');
                                if (seen["KEYWORDS"].TryAdd(clean))
                                {
                                    writers["KEYWORDS"].WriteLine(clean);
                                    localHits++;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Filter by keyword, then extract (Original Fail Count Logic Restored)
                        foreach (var keyword in keywords)
                        {
                            if (!line.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;

                            var found = false;
                            foreach (var pattern in keywordPatterns)
                            {
                                foreach (Match match in pattern.Matches(line))
                                {
                                    found = true;
                                    var extracted = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                                    if (seen[keyword].TryAdd(extracted))
                                    {
                                        writers[keyword].WriteLine(extracted);
                                        localHits++;
                                    }
                                }
                            }
                            // Original fail count: counts every line with the keyword that didn't match the pattern
                            if (!found) localFails++;
                        }
                    }
                }

                if (lineNum % 500 == 0)
                {
                    if (localHits > 0) { Interlocked.Add(ref _hitCount, localHits); localHits = 0; }
                    if (localFails > 0) { Interlocked.Add(ref _failCount, localFails); localFails = 0; }
                    Dispatcher.BeginInvoke(UpdateStats, DispatcherPriority.Background);
                }
            }

            if (localHits > 0) Interlocked.Add(ref _hitCount, localHits);
            if (localFails > 0) Interlocked.Add(ref _failCount, localFails);
            Dispatcher.BeginInvoke(UpdateStats, DispatcherPriority.Background);
        }

        private void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private async Task CreateWordlistsAsync(ConcurrentDictionary<string, string> files, bool wasCancelled, string wordlistType)
        {
            await Dispatcher.Invoke(async () =>
            {
                try
                {
                    var cwd = Directory.GetCurrentDirectory();
                    int createdCount = 0;

                    foreach (var kvp in files)
                    {
                        var name = kvp.Key;
                        var path = kvp.Value;
                        if (!File.Exists(path))
                        {
                            Log($"⚠️ File not found: {name}");
                            continue;
                        }

                        int lineCount = 0;
                        try
                        {
                            using var reader = new StreamReader(path);
                            while (reader.ReadLine() != null) lineCount++;
                        }
                        catch { continue; }

                        if (lineCount == 0)
                        {
                            Log($"⚠️ Empty file, skipping: {name}");
                            continue;
                        }

                        var relPath = path.StartsWith(cwd) ? path[(cwd.Length + 1)..] : path;

                        var existing = _wordlistsVm.WordlistsCollection
                            .FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            await _wordlistsVm.DeleteAsync(existing);
                            Log($"🗑️ Replaced: {name}");
                        }

                        await _wordlistsVm.AddAsync(new WordlistEntity
                        {
                            Name = name,
                            FileName = relPath,
                            Type = wordlistType,
                            Purpose = wasCancelled ? "Partial ULP Extract" : "ULP Extract",
                            Total = lineCount
                        });
                        Log($"📋 Wordlist saved: {name} ({lineCount} lines)");
                        createdCount++;
                    }

                    await _wordlistsVm.InitializeAsync();
                    Log($"💾 {createdCount} wordlist(s) saved");
                }
                catch (Exception ex)
                {
                    Log($" Wordlist error: {ex.Message}");
                }
            });
        }
    }

    public class ConcurrentHashSet<T>
    {
        private readonly ConcurrentDictionary<T, byte> _dict;

        public ConcurrentHashSet()
        {
            _dict = new ConcurrentDictionary<T, byte>();
        }

        // Added constructor to allow custom equality comparison (like case-insensitivity)
        public ConcurrentHashSet(IEqualityComparer<T> comparer)
        {
            _dict = new ConcurrentDictionary<T, byte>(comparer);
        }

        public bool TryAdd(T item) => _dict.TryAdd(item, 0);
        public int Count => _dict.Count;
    }
}
