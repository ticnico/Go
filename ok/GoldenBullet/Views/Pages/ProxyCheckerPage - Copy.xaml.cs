using Core;
using Core.Entities;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using RuriLib.Models.Proxies;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class ProxyCheckerPage : Page
    {
        private CancellationTokenSource _cancellationTokenSource;
        private List<ProxyInfo> _proxies = new List<ProxyInfo>();
        private List<ProxyInfo> _workingProxies = new List<ProxyInfo>();
        private bool _isChecking = false;
        private bool _isPaused = false;
        private int _checkedCount = 0;
        private int _workingCount = 0;
        private int _totalCount = 0;
        private readonly object _lockObject = new object();
        private DateTime _startTime;
        private DispatcherTimer _elapsedTimer;
        private string _currentGroupName = string.Empty;
        private ProxiesViewModel _proxiesVm;
        private readonly ViewModelsService _vmService;
        private bool _isLoadingFromFile = false;
        private string _currentProxyList = string.Empty;

        public ProxyCheckerPage()
        {
            InitializeComponent();
            InitializeElapsedTimer();
            UrlEntry.Text = "https://google.com";
            ThreadCountEntry.Value = 50; // ✅ NumericUpDown: Use Value instead of Text
            TimeoutUpDown.Value = 10000; // ✅ Default timeout 10000ms
            _vmService = SP.GetService<ViewModelsService>();
            _proxiesVm = _vmService?.Proxies;
            _ = LoadProxyGroupsAsync();
            AppendLog("Proxy Checker initialized", "#2196F3");
        }

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
                    ImportGroupCombo.Items.Clear();
                    ImportGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "-- Select group --",
                        Tag = null,
                        IsSelected = true
                    });
                    ImportGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "All",
                        Tag = "ALL",
                        Foreground = new SolidColorBrush(Color.FromRgb(251, 192, 45))
                    });
                    foreach (var group in groups)
                    {
                        ImportGroupCombo.Items.Add(new ComboBoxItem
                        {
                            Content = group.Name,
                            Tag = group
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"Error loading groups: {ex.Message}", "#F44336"));
            }
        }

        private void InitializeElapsedTimer()
        {
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (s, e) => UpdateElapsedLabel();
        }

        private void ImportGroupCombo_DropDownOpened(object sender, EventArgs e) => LoadProxyGroups();

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
                    var previouslySelected = ImportGroupCombo.SelectedItem as ComboBoxItem;
                    ImportGroupCombo.Items.Clear();
                    ImportGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "-- Select group --",
                        Tag = null,
                        IsSelected = previouslySelected?.Tag == null
                    });
                    ImportGroupCombo.Items.Add(new ComboBoxItem
                    {
                        Content = "All",
                        Tag = "ALL",
                        Foreground = new SolidColorBrush(Color.FromRgb(251, 192, 45))
                    });
                    foreach (var group in groups)
                    {
                        var item = new ComboBoxItem { Content = group.Name, Tag = group };
                        if (previouslySelected?.Tag is ProxyGroupEntity saved && saved.Id == group.Id)
                            item.IsSelected = true;
                        ImportGroupCombo.Items.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"Error loading groups: {ex.Message}", "#F44336"));
            }
        }

        private async void ImportGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingFromFile) return;
            if (ImportGroupCombo.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag?.ToString() == "ALL")
            {
                await LoadAllGroupsProxies();
                return;
            }
            if (item.Tag == null)
            {
                _currentProxyList = string.Empty;
                UpdateProxyStats(0);
                return;
            }
            if (item.Tag is ProxyGroupEntity group)
            {
                await LoadSingleGroupProxies(group);
                return;
            }
        }

        private async void OnlyUntestedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isChecking) return;
            await RefreshProxyListWithFilter();
        }

        private async Task RefreshProxyListWithFilter()
        {
            var selectedItem = ImportGroupCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null) return;
            if (selectedItem.Tag?.ToString() == "ALL")
            {
                await LoadAllGroupsProxies();
            }
            else if (selectedItem.Tag is ProxyGroupEntity group)
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
                var sb = new StringBuilder();
                int totalProxies = 0;
                bool onlyUntested = OnlyUntestedCheckBox.IsChecked == true;
                foreach (var group in allGroups)
                {
                    if (group.Proxies?.Any() == true)
                    {
                        foreach (var proxy in group.Proxies)
                        {
                            if (onlyUntested && proxy.Status == ProxyWorkingStatus.Working)
                                continue;
                            var proxyStr = $"{proxy.Host}:{proxy.Port}";
                            if (!string.IsNullOrEmpty(proxy.Username))
                                proxyStr += $":{proxy.Username}:{proxy.Password}";
                            sb.AppendLine(proxyStr);
                            totalProxies++;
                        }
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    _currentProxyList = sb.ToString().TrimEnd();
                    UpdateProxyStats(totalProxies);
                    ParseProxies();
                });
                AppendLog($"Loaded {totalProxies} proxies from ALL groups", "#4CAF50");
            }
            catch (Exception ex)
            {
                AppendLog($"Error loading all groups: {ex.Message}", "#F44336");
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
                    var sb = new StringBuilder();
                    int proxyCount = 0;
                    bool onlyUntested = OnlyUntestedCheckBox.IsChecked == true;
                    foreach (var proxy in loadedGroup.Proxies)
                    {
                        if (onlyUntested && proxy.Status == ProxyWorkingStatus.Working)
                            continue;
                        var proxyStr = $"{proxy.Host}:{proxy.Port}";
                        if (!string.IsNullOrEmpty(proxy.Username))
                            proxyStr += $":{proxy.Username}:{proxy.Password}";
                        sb.AppendLine(proxyStr);
                        proxyCount++;
                    }
                    Dispatcher.Invoke(() =>
                    {
                        _currentProxyList = sb.ToString().TrimEnd();
                        UpdateProxyStats(proxyCount);
                        ParseProxies();
                    });
                    AppendLog($"Loaded {proxyCount} proxies from '{loadedGroup.Name}'", "#4CAF50");
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        _currentProxyList = string.Empty;
                        UpdateProxyStats(0);
                    });
                    AppendLog($"Group '{loadedGroup?.Name ?? group.Name}' has no proxies", "#FBC02D");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error loading group: {ex.Message}", "#F44336");
            }
        }

        private void UpdateProxyStats(int count)
        {
            Dispatcher.Invoke(() =>
            {
                if (TotalCountLabel != null) TotalCountLabel.Text = count.ToString();
                if (CheckedCountLabel != null) CheckedCountLabel.Text = "0";
                if (WorkingCountLabel != null) WorkingCountLabel.Text = "0";
                if (FailedCountLabel != null) FailedCountLabel.Text = "0";
                if (SuccessRateLabel != null) SuccessRateLabel.Text = "0%";
                if (MainProgressBar != null) MainProgressBar.Value = 0;
            });
        }

        private void ResetImportGroupCombo()
        {
            Dispatcher.Invoke(() =>
            {
                _isLoadingFromFile = true;
                foreach (ComboBoxItem item in ImportGroupCombo.Items)
                {
                    if (item.Tag == null)
                    {
                        item.IsSelected = true;
                        break;
                    }
                }
                _isLoadingFromFile = false;
            });
        }

        private ProxyType DetectProxyType(string proxyString)
        {
            if (proxyString.Contains("socks5", StringComparison.OrdinalIgnoreCase) ||
                (proxyString.Split(':').Length >= 2 && proxyString.Split(':')[1] == "1080"))
                return ProxyType.Socks5;
            if (proxyString.Contains("socks4", StringComparison.OrdinalIgnoreCase) ||
                (proxyString.Split(':').Length >= 2 &&
                (proxyString.Split(':')[1] == "1080" || proxyString.Split(':')[1] == "9050")))
                return ProxyType.Socks4;
            return ProxyType.Http;
        }

        private void LoadProxies_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", Title = "Load Proxy List" };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(openFileDialog.FileName);
                    var sb = new StringBuilder();
                    foreach (var line in lines) if (!string.IsNullOrWhiteSpace(line)) sb.AppendLine(line.Trim());
                    _isLoadingFromFile = true;
                    _currentProxyList = sb.ToString();
                    var count = lines.Length;
                    UpdateProxyStats(count);
                    ParseProxies();
                    ResetImportGroupCombo();
                    _isLoadingFromFile = false;
                    AppendLog($"Loaded {lines.Length} proxies from file.", "#AAAAAA");
                }
                catch (Exception ex) { MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private async void StartStopChecking_Click(object sender, RoutedEventArgs e)
        {
            if (_isChecking) { if (_isPaused) await ResumeChecking(); else await PauseChecking(); }
            else await StartChecking();
        }

        private void PauseChecking_Click(object sender, RoutedEventArgs e) => _ = PauseChecking();

        private async Task StartChecking()
        {
            var targetUrl = UrlEntry.Text.Trim();
            if (!string.IsNullOrEmpty(targetUrl) && !targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                targetUrl = "https://" + targetUrl;

            if (string.IsNullOrEmpty(targetUrl)) { MessageBox.Show("Please enter a target URL.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri)) { MessageBox.Show("Please enter a valid URL.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            // ✅ Get Thread Count from NumericUpDown
            int threadCount = 50;
            if (ThreadCountEntry != null && ThreadCountEntry.Value.HasValue)
            {
                threadCount = (int)ThreadCountEntry.Value.Value;
            }
            if (threadCount <= 0) threadCount = 50;

            // ✅ Get Timeout from NumericUpDown
            int timeoutMs = 10000;
            if (TimeoutUpDown != null && TimeoutUpDown.Value.HasValue)
            {
                timeoutMs = (int)TimeoutUpDown.Value.Value;
            }

            var urlsToCheck = new List<string> { targetUrl };
            ParseProxies();

            if (_proxies.Count == 0) { MessageBox.Show("Please import proxies from a group or load from file.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            _workingProxies.Clear();
            _isChecking = true;
            _isPaused = false;
            _checkedCount = 0;
            _workingCount = 0;
            _totalCount = _proxies.Count;
            _startTime = DateTime.Now;
            _currentGroupName = uri.Host;
            _cancellationTokenSource = new CancellationTokenSource();
            _elapsedTimer.Start();
            UpdateUIState();
            UpdateProgressLabel();
            ClearLog();

            AppendLog($"Target URL: {targetUrl}", "#AAAAAA");
            AppendLog($"Total proxies: {_totalCount}", "#AAAAAA");
            AppendLog($"Threads: {threadCount}", "#AAAAAA");
            AppendLog($"Timeout: {timeoutMs}ms", "#AAAAAA");
            AppendLog("----------------------------------------", "#AAAAAA");

            var semaphore = new SemaphoreSlim(threadCount);
            var tasks = new List<Task>();

            try
            {
                foreach (var proxy in _proxies)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) break;
                    await semaphore.WaitAsync(_cancellationTokenSource.Token);

                    // ✅ Pass timeoutMs to CheckProxy
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await CheckProxy(proxy, urlsToCheck, timeoutMs);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, _cancellationTokenSource.Token);
                    tasks.Add(task);
                }

                if (tasks.Count > 0) await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { AppendLog("[STOPPED BY USER]", "#FBC02D"); }
            catch (Exception ex) { AppendLog($"[ERROR] {ex.Message}", "#F44336"); }
            finally
            {
                semaphore?.Dispose();
                _isChecking = false;
                _isPaused = false;
                _elapsedTimer.Stop();
                AppendLog("----------------------------------------", "#AAAAAA");
                AppendLog($"Working: {_workingCount}/{_totalCount} ({(_workingCount * 100.0 / _totalCount):F1}%)", "#AAAAAA");
                Dispatcher.Invoke(() => UpdateUIState());
            }
        }

        private async Task PauseChecking() { _isPaused = true; AppendLog("[PAUSED]", "#FBC02D"); UpdateUIState(); }
        private async Task ResumeChecking() { _isPaused = false; AppendLog("[RESUMED]", "#2196F3"); UpdateUIState(); }
        private void StopChecking_Click(object sender, RoutedEventArgs e) { StopChecking(); }
        private void StopChecking() { _cancellationTokenSource?.Cancel(); _elapsedTimer.Stop(); _isChecking = false; _isPaused = false; UpdateUIState(); AppendLog("[STOPPED BY USER]", "#FBC02D"); }

        // ✅ Updated CheckProxy to accept timeoutMs parameter
        private async Task CheckProxy(ProxyInfo proxy, List<string> targetUrls, int timeoutMs)
        {
            while (_isPaused && !_cancellationTokenSource.Token.IsCancellationRequested)
                await Task.Delay(100);

            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isWorking = false;
            string status = "FAILED";
            GeoLocationInfo geoInfo = null;

            var detectedType = DetectProxyType(proxy.Raw);

            try
            {
                using var handler = new HttpClientHandler();

                if (detectedType == ProxyType.Http || detectedType == ProxyType.Socks4 || detectedType == ProxyType.Socks5)
                {
                    var proxyUrl = detectedType switch
                    {
                        ProxyType.Socks4 => $"socks4://{proxy.Host}:{proxy.Port}",
                        ProxyType.Socks5 => $"socks5://{proxy.Host}:{proxy.Port}",
                        _ => $"http://{proxy.Host}:{proxy.Port}"
                    };

                    handler.Proxy = new WebProxy(proxyUrl) { BypassProxyOnLocal = false };

                    if (!string.IsNullOrEmpty(proxy.Username) && !string.IsNullOrEmpty(proxy.Password))
                        handler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);

                    handler.UseProxy = true;
                }

                handler.ServerCertificateCustomValidationCallback = (m, c, ch, e) => true;
                handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;

                // ✅ Use dynamic timeout from NumericUpDown
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                foreach (var url in targetUrls)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        status = "CANCELLED";
                        break;
                    }

                    try
                    {
                        var response = await client.GetAsync(url, _cancellationTokenSource.Token);

                        if (response.IsSuccessStatusCode)
                        {
                            isWorking = true;
                            status = $"OK ({(int)response.StatusCode})";
                            break;
                        }
                        else
                        {
                            status = $"BAD ({(int)response.StatusCode})";
                        }
                    }
                    catch (Exception ex) when (url == targetUrls.Last())
                    {
                        status = $"ERROR: {ex.Message}";
                    }
                }
            }
            catch (TaskCanceledException)
            {
                status = "TIMEOUT";
            }
            catch (OperationCanceledException)
            {
                status = "CANCELLED";
            }
            catch (Exception ex)
            {
                status = $"ERROR: {ex.Message}";
            }

            sw.Stop();
            proxy.IsWorking = isWorking;
            proxy.ResponseTime = sw.ElapsedMilliseconds;

            if (isWorking)
            {
                try
                {
                    geoInfo = await GetIpGeolocationAsync(proxy.Host);
                    proxy.GeoInfo = geoInfo;
                }
                catch { }
            }

            lock (_lockObject)
            {
                _checkedCount++;

                if (isWorking)
                {
                    _workingCount++;
                    _workingProxies.Add(proxy);
                }

                var auth = !string.IsNullOrEmpty(proxy.Username) ? " [AUTH]" : "";
                var geo = geoInfo != null ? $" [{geoInfo?.Country}]" : "";
                var logText = $"[{proxy}] {status} - {proxy.ResponseTime}ms{auth}{geo}";

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateProgressLabel(_checkedCount, _workingCount, _totalCount);
                    AppendLog(logText, isWorking ? "#4CAF50" : "#F44336");
                }), DispatcherPriority.Background);
            }

            await UpdateProxyStatusInDatabaseAsync(proxy, isWorking, geoInfo?.CountryCode);
        }

        private async Task UpdateProxyStatusInDatabaseAsync(ProxyInfo proxy, bool isWorking, string countryCode)
        {
            try
            {
                using var scope = SP.GetService<IServiceScopeFactory>().CreateScope();
                var db = scope.ServiceProvider.GetService<ApplicationDbContext>();
                var proxyEntity = await db.Proxies
                    .FirstOrDefaultAsync(p => p.Host == proxy.Host &&
                                            p.Port == proxy.Port &&
                                            p.Username == (proxy.Username ?? "") &&
                                            p.Password == (proxy.Password ?? ""));
                if (proxyEntity != null)
                {
                    proxyEntity.Status = isWorking ? ProxyWorkingStatus.Working : ProxyWorkingStatus.NotWorking;
                    proxyEntity.Ping = (int)proxy.ResponseTime;
                    proxyEntity.LastChecked = DateTime.Now;
                    if (!string.IsNullOrEmpty(countryCode))
                        proxyEntity.Country = countryCode;
                    await db.SaveChangesAsync();
                    await UpdateProxiesViewModelAsync(proxyEntity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Status update error: {ex.Message}");
            }
        }

        private async Task UpdateProxiesViewModelAsync(ProxyEntity proxyEntity)
        {
            try
            {
                var proxiesVm = _vmService?.Proxies;
                if (proxiesVm == null) return;
                if (Dispatcher.CheckAccess())
                {
                    proxiesVm.UpdateProxyStatus(proxyEntity);
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                        proxiesVm.UpdateProxyStatus(proxyEntity));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ViewModel update error: {ex.Message}");
            }
        }

        private async Task<GeoLocationInfo> GetIpGeolocationAsync(string ip)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetStringAsync($"http://ip-api.com/json/{ip}?fields=status,country,countryCode,regionName,city,isp,timezone,query");
                var json = JsonDocument.Parse(response);
                if (json.RootElement.GetProperty("status").GetString() == "success")
                    return new GeoLocationInfo
                    {
                        Country = json.RootElement.GetProperty("country").GetString(),
                        CountryCode = json.RootElement.GetProperty("countryCode").GetString(),
                        Region = json.RootElement.GetProperty("regionName").GetString(),
                        City = json.RootElement.GetProperty("city").GetString(),
                        ISP = json.RootElement.GetProperty("isp").GetString(),
                        Timezone = json.RootElement.GetProperty("timezone").GetString()
                    };
            }
            catch { }
            return null;
        }

        private void UpdateProgressLabel(int checkedCount, int workingCount, int totalCount)
        {
            double percentage = totalCount > 0 ? (checkedCount * 100.0 / totalCount) : 0;
            double successRate = checkedCount > 0 ? (workingCount * 100.0 / checkedCount) : 0;
            Dispatcher.Invoke(() =>
            {
                MainProgressBar.Value = percentage;
                if (TotalCountLabel != null) TotalCountLabel.Text = totalCount.ToString();
                if (CheckedCountLabel != null) CheckedCountLabel.Text = checkedCount.ToString();
                if (WorkingCountLabel != null) { WorkingCountLabel.Text = workingCount.ToString(); WorkingCountLabel.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); }
                if (FailedCountLabel != null) { FailedCountLabel.Text = (checkedCount - workingCount).ToString(); FailedCountLabel.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54)); }
                if (SuccessRateLabel != null) SuccessRateLabel.Text = $"{successRate:F1}%";
                if (ProxyTypeLabel != null) ProxyTypeLabel.Text = "auto";
            });
        }

        private void UpdateProgressLabel() => UpdateProgressLabel(_checkedCount, _workingCount, _totalCount);

        private void UpdateElapsedLabel()
        {
            if (!_isChecking) return;
            var elapsed = DateTime.Now - _startTime;
            Dispatcher.Invoke(() => { if (ElapsedLabel != null) ElapsedLabel.Text = elapsed.ToString(@"hh\:mm\:ss"); });
        }

        private void ParseProxies()
        {
            _proxies.Clear();
            var lines = _currentProxyList.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                var parts = trimmed.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int port))
                {
                    _proxies.Add(new ProxyInfo
                    {
                        Host = parts[0],
                        Port = port,
                        Username = parts.Length >= 4 ? parts[2] : null,
                        Password = parts.Length >= 4 ? parts[3] : null,
                        Raw = trimmed
                    });
                }
            }
        }

        private void UpdateUIState()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(UpdateUIState); return; }
            if (_isChecking)
            {
                if (_isPaused)
                {
                    CheckButton.Visibility = Visibility.Visible;
                    CheckButton.ToolTip = "Resume Checking";
                    CheckButton.Content = new PackIconFontAwesome { Kind = PackIconFontAwesomeKind.PlaySolid, Width = 16, Height = 16, Foreground = Brushes.White };
                    PauseButton.Visibility = Visibility.Collapsed;
                    StopButton.Visibility = Visibility.Visible;
                    StatusLabel.Text = "Paused";
                    StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(251, 192, 45));
                }
                else
                {
                    CheckButton.Visibility = Visibility.Collapsed;
                    PauseButton.Visibility = Visibility.Visible;
                    StopButton.Visibility = Visibility.Visible;
                    StatusLabel.Text = "Running";
                    StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                }
                UrlEntry.IsEnabled = false;
                ImportGroupCombo.IsEnabled = false;
                ThreadCountEntry.IsEnabled = false;
                TimeoutUpDown.IsEnabled = false; // ✅ Disable timeout during check
                OnlyUntestedCheckBox.IsEnabled = false;
            }
            else
            {
                CheckButton.Visibility = Visibility.Visible;
                CheckButton.ToolTip = "Start Checking";
                CheckButton.Content = new PackIconFontAwesome { Kind = PackIconFontAwesomeKind.PlaySolid, Width = 16, Height = 16, Foreground = Brushes.White };
                PauseButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Collapsed;
                StatusLabel.Text = "Idle";
                StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(251, 192, 45));
                UrlEntry.IsEnabled = true;
                ImportGroupCombo.IsEnabled = true;
                ThreadCountEntry.IsEnabled = true;
                TimeoutUpDown.IsEnabled = true; // ✅ Enable timeout when idle
                OnlyUntestedCheckBox.IsEnabled = true;
            }
        }

        private void AppendLog(string text, string colorHex)
        {
            if (jobLog == null) return;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorHex);
                    jobLog.Append(text, color, addTimestamp: true);
                }
                catch
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(colorHex);
                        jobLog.Append(text, color, addTimestamp: false);
                    }
                    catch { }
                }
            });
        }

        private void ClearLog()
        {
            if (jobLog == null) return;
            Dispatcher.Invoke(() => { try { jobLog.Clear(); } catch { } });
        }
    }

    public class ProxyInfo
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Raw { get; set; }
        public bool IsWorking { get; set; }
        public long ResponseTime { get; set; }
        public GeoLocationInfo GeoInfo { get; set; }
        public override string ToString() => Raw ?? $"{Host}:{Port}";
    }

    public class GeoLocationInfo
    {
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string ISP { get; set; }
        public string Timezone { get; set; }
    }
}
