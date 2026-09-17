using GoldenBullet.Models;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Markdig.Syntax.Inlines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TicnicO;
using TicnicO.Entities;
using TicnicO.Models.Jobs;
using TicnicO.Models.Settings;
using TicnicO.Repositories;
using TicnicO.Services;

namespace GoldenBullet.Views.Pages
{
    // ✅ Static state to persist job across navigation
    public static class ProxyCheckJobState
    {
        public static string Url { get; set; } = "https://google.com";
        public static string SuccessKey { get; set; } = "title>Google";
        public static int Bots { get; set; } = 10;
        public static int Timeout { get; set; } = 10000;

        public static bool CheckOnlyUntested { get; set; } = false;
        public static int GroupId { get; set; } = -1;

        public static bool IsRunning { get; set; }
        public static bool IsPaused { get; set; }
        public static int Tested { get; set; }
        public static int Working { get; set; }
        public static int Total { get; set; }
        public static DateTime StartTime { get; set; }

        public static CancellationTokenSource Cts { get; set; }
    }

    public partial class ProxyCheckerPage : Page
    {
        // Runtime state
        private List<ProxyInfo> _proxies = new();
        private readonly object _lock = new();
        private bool _logSubscribed;

        // Track old values for change detection
        private int _oldGroupId = -1;
        private bool _oldCheckUntested = false;
        private int _oldBots = 10;
        private int _oldTimeout = 10000;
        private string _oldUrl = "";
        private string _oldSuccessKey = "";

        // Services
        private readonly IProxyGroupRepository _groupRepo;
        private readonly ViewModelsService _vmService;
        private readonly MainWindow _mainWindow;
        private readonly GoldenBulletSettingsService _settingsService;

        public ProxyCheckerPage()
        {
            _groupRepo = SP.GetService<IProxyGroupRepository>();
            _vmService = SP.GetService<ViewModelsService>();
            _mainWindow = SP.GetService<MainWindow>();
            _settingsService = SP.GetService<GoldenBulletSettingsService>();

            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // ✅ If job is running, just reconnect UI - don't reload anything
            if (ProxyCheckJobState.IsRunning || ProxyCheckJobState.IsPaused)
            {
                ReconnectUI();
                return;
            }

            // Normal initialization for fresh jobs
            if (string.IsNullOrEmpty(ProxyCheckJobState.Url))
                LoadDefaultTarget();

            _oldGroupId = ProxyCheckJobState.GroupId;
            _oldCheckUntested = ProxyCheckJobState.CheckOnlyUntested;
            _oldBots = ProxyCheckJobState.Bots;
            _oldTimeout = ProxyCheckJobState.Timeout;
            _oldUrl = ProxyCheckJobState.Url;
            _oldSuccessKey = ProxyCheckJobState.SuccessKey;

            InitializeUI();
            _logSubscribed = true;

            if (_proxies.Count == 0 && ProxyCheckJobState.Total == 0)
                _ = LoadProxiesAsync();
        }

        private void ReconnectUI()
        {
            // ✅ Re-initialize UI labels from static state
            InitializeUI();

            // ✅ Re-subscribe to logs
            _logSubscribed = true;

            // ✅ Reload _proxies list from database to match running job
            // (Only if not already loaded)
            if (_proxies.Count == 0 && ProxyCheckJobState.Total > 0)
            {
                _ = LoadProxiesAsync(); // This will load the same proxies the job is using
            }

            // ✅ Log reconnection
            Log("🔗 Reconnected to running job", Colors.Cyan);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // ✅ Only unsubscribe if job is NOT running
            // This keeps log subscription alive if user just switched tabs
            if (!ProxyCheckJobState.IsRunning && !ProxyCheckJobState.IsPaused)
            {
                _logSubscribed = false;
            }
        }

        private void LoadDefaultTarget()
        {
            var targets = _settingsService?.Settings?.GeneralSettings?.ProxyCheckTargets;
            var defaultTarget = targets?.FirstOrDefault() ?? new ProxyCheckTarget();

            ProxyCheckJobState.Url = defaultTarget.Url ?? "https://google.com";
            ProxyCheckJobState.SuccessKey = defaultTarget.SuccessKey ?? "title>Google.com";
        }

        private void InitializeUI()
        {
            // Stats
            if (TotalLabel != null) TotalLabel.Text = ProxyCheckJobState.Total.ToString();
            if (TestedLabel != null) TestedLabel.Text = ProxyCheckJobState.Tested.ToString();
            if (WorkingLabel != null) WorkingLabel.Text = ProxyCheckJobState.Working.ToString();
            if (NotWorkingLabel != null)
                NotWorkingLabel.Text = (ProxyCheckJobState.Tested - ProxyCheckJobState.Working).ToString();
            if (CpmLabel != null) CpmLabel.Text = "0";
            if (ElapsedLabel != null) ElapsedLabel.Text = "00:00:00";
            if (RemainingLabel != null) RemainingLabel.Text = "∞";
            if (ProgressPercentLabel != null) ProgressPercentLabel.Text = "0%";
            if (ProgressBarText != null) ProgressBarText.Text = "0%";
            if (MainProgressBar != null) MainProgressBar.Value = 0;

            // Options - ✅ Ensure these reflect current state
            if (TargetUrlLabel != null) TargetUrlLabel.Text = ProxyCheckJobState.Url ?? "Not set";
            if (SuccessKeyLabel != null)
                SuccessKeyLabel.Text = string.IsNullOrEmpty(ProxyCheckJobState.SuccessKey) ? "None" : ProxyCheckJobState.SuccessKey;
            if (BotsLabel != null) BotsLabel.Text = ProxyCheckJobState.Bots.ToString();
            if (TimeoutLabel != null) TimeoutLabel.Text = ProxyCheckJobState.Timeout.ToString();
            if (CheckUntestedLabel != null)
                CheckUntestedLabel.Text = ProxyCheckJobState.CheckOnlyUntested.ToString();

            UpdateButtons();
        }

        private async Task LoadProxiesAsync()
        {
            try
            {
                var groupName = ProxyCheckJobState.GroupId == -1 ? "All" : GetGroupName(ProxyCheckJobState.GroupId);

                Dispatcher.Invoke(() =>
                {
                    jobLog?.Append($"📦 Loading proxies from group '{groupName}'...", Colors.Cyan);
                });

                List<ProxyEntity> entities;

                if (ProxyCheckJobState.GroupId == -1)
                {
                    entities = await _groupRepo.GetAll()
                        .AsNoTracking()
                        .Include(g => g.Proxies)
                        .SelectMany(g => g.Proxies)
                        .ToListAsync();
                }
                else
                {
                    var group = await _groupRepo.GetAll()
                        .AsNoTracking()
                        .Include(g => g.Proxies)
                        .FirstOrDefaultAsync(g => g.Id == ProxyCheckJobState.GroupId);

                    if (group == null)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            jobLog?.Append($"❌ Group ID {ProxyCheckJobState.GroupId} not found", Colors.Red);
                        });
                        return;
                    }

                    entities = group.Proxies?.ToList() ?? new List<ProxyEntity>();
                }

                // ✅ FIX: Filter by Status BEFORE converting to ProxyInfo
                if (ProxyCheckJobState.CheckOnlyUntested)
                {
                    var beforeCount = entities.Count;
                    entities = entities.Where(p => p.Status == ProxyWorkingStatus.Untested).ToList();
                    var afterCount = entities.Count;

                    Dispatcher.Invoke(() =>
                    {
                        jobLog?.Append($"🔍 CheckOnlyUntested: {beforeCount} → {afterCount} proxies", Colors.Cyan);
                    });
                }

                _proxies = entities.Select(p => new ProxyInfo
                {
                    Host = p.Host,
                    Port = p.Port,
                    Username = string.IsNullOrEmpty(p.Username) ? null : p.Username,
                    Password = string.IsNullOrEmpty(p.Password) ? null : p.Password,
                    Raw = $"{p.Host}:{p.Port}" + (!string.IsNullOrEmpty(p.Username) ? $":{p.Username}:{p.Password}" : "")
                }).ToList();

                ProxyCheckJobState.Total = _proxies.Count;

                // ✅ Reset counters when loading new group
                if (ProxyCheckJobState.GroupId != _oldGroupId && !ProxyCheckJobState.IsRunning)
                {
                    ProxyCheckJobState.Tested = 0;
                    ProxyCheckJobState.Working = 0;
                    _oldGroupId = ProxyCheckJobState.GroupId;
                }

                UpdateStats();

                Dispatcher.Invoke(() =>
                {
                    jobLog?.Append($"✅ Loaded {ProxyCheckJobState.Total} proxies", Colors.LightGreen);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    jobLog?.Append($"❌ Load error: {ex.Message}", Colors.Red);
                });
            }
        }

        private string GetProxyKey(ProxyInfo p) =>
            $"{p.Host}:{p.Port}:{p.Username ?? ""}:{p.Password ?? ""}";

        private void UpdateStats()
        {
            Dispatcher.Invoke(() =>
            {
                if (TotalLabel != null) TotalLabel.Text = ProxyCheckJobState.Total.ToString();
                if (TestedLabel != null) TestedLabel.Text = ProxyCheckJobState.Tested.ToString();
                if (WorkingLabel != null) WorkingLabel.Text = ProxyCheckJobState.Working.ToString();
                if (NotWorkingLabel != null)
                    NotWorkingLabel.Text = (ProxyCheckJobState.Tested - ProxyCheckJobState.Working).ToString();

                double pct = ProxyCheckJobState.Total > 0
                    ? (ProxyCheckJobState.Tested * 100.0 / ProxyCheckJobState.Total) : 0;
                if (ProgressPercentLabel != null) ProgressPercentLabel.Text = $"{pct:F1}%";
                if (ProgressBarText != null) ProgressBarText.Text = $"{pct:F1}%";
                if (MainProgressBar != null) MainProgressBar.Value = pct;

                var elapsed = DateTime.Now - ProxyCheckJobState.StartTime;
                if (CpmLabel != null)
                    CpmLabel.Text = elapsed.TotalMinutes > 0
                        ? $"{ProxyCheckJobState.Tested / elapsed.TotalMinutes:F0}" : "0";
                if (ElapsedLabel != null)
                    ElapsedLabel.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

                if (RemainingLabel != null)
                {
                    if (ProxyCheckJobState.Tested > 0 && ProxyCheckJobState.Total > ProxyCheckJobState.Tested)
                    {
                        var avg = ProxyCheckJobState.Tested / Math.Max(elapsed.TotalSeconds, 1);
                        var rem = TimeSpan.FromSeconds(
                            (ProxyCheckJobState.Total - ProxyCheckJobState.Tested) / Math.Max(avg, 0.001));
                        RemainingLabel.Text = $"{rem.Hours:D2}:{rem.Minutes:D2}:{rem.Seconds:D2}";
                    }
                    else
                    {
                        RemainingLabel.Text = ProxyCheckJobState.Tested >= ProxyCheckJobState.Total ? "Done" : "∞";
                    }
                }
            });
        }

        private void UpdateButtons()
        {
            Dispatcher.Invoke(() =>
            {
                if (StartButton != null)
                    StartButton.Visibility = (!ProxyCheckJobState.IsRunning && !ProxyCheckJobState.IsPaused)
                        ? Visibility.Visible : Visibility.Collapsed;
                if (ResumeButton != null)
                    ResumeButton.Visibility = (ProxyCheckJobState.IsPaused && !ProxyCheckJobState.IsRunning)
                        ? Visibility.Visible : Visibility.Collapsed;
                if (PauseButton != null)
                    PauseButton.Visibility = (ProxyCheckJobState.IsRunning && !ProxyCheckJobState.IsPaused)
                        ? Visibility.Visible : Visibility.Collapsed;
                if (StopButton != null)
                    StopButton.Visibility = (ProxyCheckJobState.IsRunning || ProxyCheckJobState.IsPaused)
                        ? Visibility.Visible : Visibility.Collapsed;
                if (AbortButton != null)
                    AbortButton.Visibility = (ProxyCheckJobState.IsRunning || ProxyCheckJobState.IsPaused)
                        ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        private void Log(string msg, Color color)
        {
            if (!_logSubscribed) return;
            Dispatcher.Invoke(() => jobLog?.Append(msg, color));
        }

        // === Job Control ===

        public async void OnStart(object sender, RoutedEventArgs e)
        {
            if (ProxyCheckJobState.IsRunning || ProxyCheckJobState.IsPaused) return;
            if (string.IsNullOrWhiteSpace(ProxyCheckJobState.Url) || _proxies.Count == 0)
            {
                MessageBox.Show("Set URL and load proxies first.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProxyCheckJobState.Cts = new CancellationTokenSource();
            ProxyCheckJobState.IsRunning = true;
            ProxyCheckJobState.IsPaused = false;
            ProxyCheckJobState.Tested = 0;
            ProxyCheckJobState.Working = 0;
            ProxyCheckJobState.StartTime = DateTime.Now;

            _logSubscribed = true;
            UpdateButtons();
            Log($"Started: {ProxyCheckJobState.Url} | Bots: {ProxyCheckJobState.Bots}", Colors.LightGreen);

            try { await RunJob(ProxyCheckJobState.Cts.Token); }
            catch (OperationCanceledException) { Log("Cancelled", Colors.Orange); }
            catch (Exception ex) { Log($"Error: {ex.Message}", Colors.Red); }
            finally
            {
                ProxyCheckJobState.IsRunning = false;
                ProxyCheckJobState.IsPaused = false;
                UpdateButtons();
                Log($"Done: {ProxyCheckJobState.Working}/{ProxyCheckJobState.Total} working", Colors.LightGreen);
                if (ProxyCheckJobState.Working > 0)
                    await SaveWorking(new Uri(ProxyCheckJobState.Url).Host);
            }
        }

        public void OnStop(object sender, RoutedEventArgs e)
        {
            ProxyCheckJobState.Cts?.Cancel();
            ProxyCheckJobState.IsRunning = false;
            ProxyCheckJobState.IsPaused = false;
            UpdateButtons();
            Log("Stopped", Colors.Orange);
        }

        public void OnPause(object sender, RoutedEventArgs e)
        {
            ProxyCheckJobState.IsPaused = true;
            UpdateButtons();
            Log("Paused", Colors.Yellow);
        }

        public void OnResume(object sender, RoutedEventArgs e)
        {
            ProxyCheckJobState.IsPaused = false;
            ProxyCheckJobState.IsRunning = true;
            UpdateButtons();
            Log("Resumed", Colors.LightGreen);
        }

        public void OnAbort(object sender, RoutedEventArgs e)
        {
            ProxyCheckJobState.Cts?.Cancel();
            ProxyCheckJobState.IsRunning = false;
            ProxyCheckJobState.IsPaused = false;
            ProxyCheckJobState.Tested = 0;
            ProxyCheckJobState.Working = 0;
            UpdateStats();
            UpdateButtons();
            jobLog?.Clear();
            Log("Aborted", Colors.Red);
        }

        // === Core Logic ===

        private async Task RunJob(CancellationToken token)
        {
            var urls = new List<string> { ProxyCheckJobState.Url };
            if (Uri.TryCreate(ProxyCheckJobState.Url, UriKind.Absolute, out var uri) &&
                !ProxyCheckJobState.Url.StartsWith("http"))
                urls.Add((uri.Scheme == Uri.UriSchemeHttps ? "http://" : "https://") +
                    uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) + uri.PathAndQuery);

            var semaphore = new SemaphoreSlim(ProxyCheckJobState.Bots);
            var tasks = new List<Task>();

            foreach (var proxy in _proxies)  // ✅ Already filtered in LoadProxiesAsync()
            {
                if (token.IsCancellationRequested) break;
                while (ProxyCheckJobState.IsPaused && !token.IsCancellationRequested)
                    await Task.Delay(100);
                if (token.IsCancellationRequested) break;

                await semaphore.WaitAsync(token);
                tasks.Add(Task.Run(async () =>
                {
                    try { await CheckProxy(proxy, urls, token); }
                    finally { semaphore.Release(); }
                }, token));
            }
            if (tasks.Any()) await Task.WhenAll(tasks);
            semaphore.Dispose();
        }

        private async Task CheckProxy(ProxyInfo proxy, List<string> urls, CancellationToken token)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = false;
            string status = "FAILED";
            GeoLocationInfo geo = null;

            try
            {
                using var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://{proxy.Host}:{proxy.Port}") { BypassProxyOnLocal = false },
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                  System.Security.Authentication.SslProtocols.Tls13
                };
                if (!string.IsNullOrEmpty(proxy.Username))
                    handler.Proxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password);

                using var client = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromMilliseconds(ProxyCheckJobState.Timeout)
                };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                foreach (var url in urls)
                {
                    if (token.IsCancellationRequested) { status = "CANCELLED"; break; }
                    try
                    {
                        var res = await client.GetAsync(url, token);
                        bool success = res.IsSuccessStatusCode;
                        if (success && !string.IsNullOrWhiteSpace(ProxyCheckJobState.SuccessKey))
                        {
                            var content = await res.Content.ReadAsStringAsync();
                            success = content.Contains(ProxyCheckJobState.SuccessKey, StringComparison.OrdinalIgnoreCase);
                        }
                        if (success) { ok = true; status = $"OK ({(int)res.StatusCode})"; break; }
                        else status = $"BAD ({(int)res.StatusCode})";
                    }
                    catch (Exception ex) when (url == urls.Last())
                    { status = ex is TaskCanceledException ? "TIMEOUT" : $"ERROR: {ex.Message}"; }
                }
            }
            catch { }

            sw.Stop();
            proxy.IsWorking = ok;
            proxy.ResponseTime = sw.ElapsedMilliseconds;

            if (ok)
            {
                try
                {
                    using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var json = await c.GetStringAsync($"http://ip-api.com/json/{proxy.Host}?fields=countryCode");
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetProperty("status").GetString() == "success")
                        geo = new GeoLocationInfo { CountryCode = doc.RootElement.GetProperty("countryCode").GetString() };
                }
                catch { }
            }

            // ✅ Update state
            lock (_lock)
            {
                ProxyCheckJobState.Tested++;
                if (ok) ProxyCheckJobState.Working++;
            }

            // ✅ Update proxy status in database immediately
            await UpdateProxyStatusInDatabaseAsync(proxy, ok, geo?.CountryCode);

            Dispatcher.BeginInvoke(() =>
            {
                UpdateStats();
                if (_logSubscribed)
                {
                    var icon = ok ? "[OK]" : "[FAIL]";
                    var auth = !string.IsNullOrEmpty(proxy.Username) ? " [AUTH]" : "";
                    var geoTag = geo?.CountryCode != null ? $" [{geo.CountryCode}]" : "";
                    jobLog?.Append($"{icon} [{proxy}] {status} - {proxy.ResponseTime}ms{auth}{geoTag}",
                        ok ? Colors.LightGreen : Colors.Red);
                }
            });
        }

        // ✅ FIX: Update proxy status in database immediately after each check
        private async Task UpdateProxyStatusInDatabaseAsync(ProxyInfo proxy, bool isWorking, string countryCode)
        {
            try
            {
                using var scope = SP.GetService<IServiceScopeFactory>().CreateScope();
                var db = scope.ServiceProvider.GetService<ApplicationDbContext>();

                // Find the proxy entity in DB
                var proxyEntity = await db.Proxies
                    .FirstOrDefaultAsync(p => p.Host == proxy.Host &&
                                             p.Port == proxy.Port &&
                                             p.Username == (proxy.Username ?? "") &&
                                             p.Password == (proxy.Password ?? ""));

                if (proxyEntity != null)
                {
                    // Update status and metadata
                    proxyEntity.Status = isWorking ? ProxyWorkingStatus.Working : ProxyWorkingStatus.NotWorking;
                    proxyEntity.Ping = (int)proxy.ResponseTime;
                    proxyEntity.LastChecked = DateTime.Now;
                    if (!string.IsNullOrEmpty(countryCode))
                        proxyEntity.Country = countryCode;

                    await db.SaveChangesAsync();

                    // ✅ Update ViewModel for real-time UI refresh
                    await UpdateProxiesViewModelAsync(proxyEntity);
                }
            }
            catch (Exception ex)
            {
                // Silent fail - don't break proxy checking
                System.Diagnostics.Debug.WriteLine($"Status update error: {ex.Message}");
            }
        }

        // ✅ FIX: Update Proxies ViewModel with the proxy entity
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

        private async Task SaveWorking(string groupName)
        {
            try
            {
                using var scope = SP.GetService<IServiceScopeFactory>().CreateScope();
                var db = scope.ServiceProvider.GetService<ApplicationDbContext>();

                var group = await db.ProxyGroups.Include(g => g.Proxies)
                    .FirstOrDefaultAsync(g => g.Name == groupName) ??
                    new ProxyGroupEntity { Name = groupName, Proxies = new List<ProxyEntity>() };

                if (group.Id == 0) { db.ProxyGroups.Add(group); await db.SaveChangesAsync(); }

                var existing = group.Proxies.Select(p =>
                    $"{p.Host}:{p.Port}:{p.Username}:{p.Password}").ToHashSet();

                var newOnes = _proxies.Where(p =>
                    p.IsWorking && !existing.Contains(GetProxyKey(p)))
                    .Select(p => new ProxyEntity
                    {
                        Host = p.Host,
                        Port = p.Port,
                        Username = p.Username ?? "",
                        Password = p.Password ?? "",
                        Type = ProxyType.Http,
                        Ping = (int)p.ResponseTime,
                        Country = "",
                        Status = ProxyWorkingStatus.Working,
                        LastChecked = DateTime.Now,
                        Group = group
                    }).ToList();

                if (newOnes.Any())
                {
                    foreach (var p in newOnes) group.Proxies.Add(p);
                    await db.SaveChangesAsync();
                    Log($"Saved {newOnes.Count} to '{groupName}'", Colors.LightGreen);
                }
            }
            catch (Exception ex)
            {
                Log($"Save error: {ex.Message}", Colors.Red);
            }
        }

        // === Options & Navigation ===

        public void OnChangeBots(object sender, MouseButtonEventArgs e)
        {
            var dialog = new ChangeBotsDialog(this, ProxyCheckJobState.Bots);
            var mainDialog = new MainDialog(dialog, "Change bots");
            mainDialog.ShowDialog();
        }

        public void ChangeBots(int newValue)
        {
            if (newValue > 0 && newValue != ProxyCheckJobState.Bots)
            {
                var old = ProxyCheckJobState.Bots;
                ProxyCheckJobState.Bots = newValue;
                if (BotsLabel != null) BotsLabel.Text = newValue.ToString();
                Log($"⚙️ Bots: {old} → {newValue}", Colors.Cyan);
            }
        }

        public async void ChangeOptions(object sender, RoutedEventArgs e)
        {
            var options = new ProxyCheckJobOptions
            {
                GroupId = ProxyCheckJobState.GroupId,
                Bots = ProxyCheckJobState.Bots,
                TimeoutMilliseconds = ProxyCheckJobState.Timeout,
                CheckOnlyUntested = ProxyCheckJobState.CheckOnlyUntested,
                Target = new ProxyCheckTarget
                {
                    Url = ProxyCheckJobState.Url,
                    SuccessKey = ProxyCheckJobState.SuccessKey
                }
            };

            Action<JobOptions> onAccept = async opts =>
            {
                if (opts is ProxyCheckJobOptions o)
                {
                    // ✅ ENSURE LOG IS SUBSCRIBED
                    _logSubscribed = true;

                    // Group change
                    if (o.GroupId != ProxyCheckJobState.GroupId)
                    {
                        var oldName = ProxyCheckJobState.GroupId == -1 ? "All" : GetGroupName(ProxyCheckJobState.GroupId);
                        var newName = o.GroupId == -1 ? "All" : GetGroupName(o.GroupId);

                        // ✅ FORCE LOG VIA DISPATCHER
                        Dispatcher.Invoke(() =>
                        {
                            jobLog?.Append($"⚙️ Proxy group: {oldName} → {newName}", Colors.Cyan);
                        });

                        ProxyCheckJobState.GroupId = o.GroupId;
                        ProxyCheckJobState.Tested = 0;
                        ProxyCheckJobState.Working = 0;
                        //ProxyCheckJobState.TestedProxyKeys.Clear();

                        await LoadProxiesAsync();
                        UpdateStats();
                    }

                    // Check only untested change
                    // Check only untested change
                    if (o.CheckOnlyUntested != ProxyCheckJobState.CheckOnlyUntested)
                    {
                        var oldVal = ProxyCheckJobState.CheckOnlyUntested;
                        ProxyCheckJobState.CheckOnlyUntested = o.CheckOnlyUntested;

                        Dispatcher.Invoke(() =>
                        {
                            jobLog?.Append($"⚙️ Check only untested: {oldVal} → {ProxyCheckJobState.CheckOnlyUntested}", Colors.Cyan);
                        });

                        // ✅ Reset progress when filter changes
                        ProxyCheckJobState.Tested = 0;
                        ProxyCheckJobState.Working = 0;

                        await LoadProxiesAsync();
                        UpdateStats();
                    }

                    // Bots change
                    if (o.Bots != ProxyCheckJobState.Bots)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            jobLog?.Append($"⚙️ Bots: {ProxyCheckJobState.Bots} → {o.Bots}", Colors.Cyan);
                        });
                        ProxyCheckJobState.Bots = o.Bots;
                        if (BotsLabel != null) BotsLabel.Text = o.Bots.ToString();
                    }

                    // Timeout change
                    if (o.TimeoutMilliseconds != ProxyCheckJobState.Timeout)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            jobLog?.Append($"⚙️ Timeout: {ProxyCheckJobState.Timeout}ms → {o.TimeoutMilliseconds}ms", Colors.Cyan);
                        });
                        ProxyCheckJobState.Timeout = o.TimeoutMilliseconds;
                        if (TimeoutLabel != null) TimeoutLabel.Text = o.TimeoutMilliseconds.ToString();
                    }

                    // URL/SuccessKey from Target
                    if (o.Target != null)
                    {
                        if (o.Target.Url != ProxyCheckJobState.Url)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                jobLog?.Append($"⚙️ Target URL: {ProxyCheckJobState.Url} → {o.Target.Url}", Colors.Cyan);
                            });
                            ProxyCheckJobState.Url = o.Target.Url ?? ProxyCheckJobState.Url;
                            if (TargetUrlLabel != null) TargetUrlLabel.Text = ProxyCheckJobState.Url;
                        }
                        if (o.Target.SuccessKey != ProxyCheckJobState.SuccessKey)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                jobLog?.Append($"⚙️ Success key: '{ProxyCheckJobState.SuccessKey}' → '{o.Target.SuccessKey}'", Colors.Cyan);
                            });
                            ProxyCheckJobState.SuccessKey = o.Target.SuccessKey ?? "";
                            if (SuccessKeyLabel != null)
                                SuccessKeyLabel.Text = ProxyCheckJobState.SuccessKey == "" ? "None" : ProxyCheckJobState.SuccessKey;
                        }
                    }

                    // ✅ Final confirmation log
                    Dispatcher.Invoke(() =>
                    {
                        jobLog?.Append("✅ Settings updated - reloading proxies...", Colors.LightGreen);
                    });
                }
            };

            var dialog = new ProxyCheckJobOptionsDialog(options, onAccept, null);
            if (NavigationService != null)
                NavigationService.Navigate(dialog);
            else
                _mainWindow?.SetMainContent(dialog);

            await Task.Yield();
        }

        private string GetGroupName(int groupId)
        {
            try
            {
                if (groupId == -1) return "All";

                var group = _groupRepo.GetAll()
                    .AsNoTracking()
                    .FirstOrDefault(g => g.Id == groupId);
                return group?.Name ?? $"Group #{groupId}";
            }
            catch (Exception ex)
            {
                Log($"[DEBUG] GetGroupName error: {ex.Message}", Colors.Cyan);
                return $"Group #{groupId}";
            }
        }

        public void GoBack(object sender, RoutedEventArgs e)
        {
            if (NavigationService?.CanGoBack == true)
                NavigationService.GoBack();
            else
                _mainWindow?.SetMainContent(new Jobs());
        }
    }
}
