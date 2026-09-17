using Core.Repositories;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class Home : Page
    {
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _perfTimer;
        private DispatcherTimer _statsTimer;

        private PerformanceCounter _cpuCounter;
        private bool _perfCountersAvailable = false;

        private long _lastTotalBytesReceived;
        private long _lastTotalBytesSent;
        private DateTime _lastNetworkCheck;

        private readonly JobsViewModel _jobsVm;
        private readonly ProxiesViewModel _proxiesVm;
        private readonly WordlistsViewModel _wordlistsVm;
        private readonly HitsViewModel _hitsVm;
        private readonly ConfigsViewModel _configsVm;
        private readonly PluginsViewModel _pluginsVm;
        private readonly IProxyRepository _proxyRepo;

        private bool _jobsSubscribed, _wordlistsSubscribed, _hitsSubscribed;
        private bool _configsSubscribed, _pluginsSubscribed, _proxiesSubscribed;
        private readonly DateTime _appStartTime;

        public Home()
        {
            InitializeComponent();

            _appStartTime = DateTime.Now;

            var vms = SP.GetService<ViewModelsService>();
            _jobsVm = vms.Jobs;
            _proxiesVm = vms.Proxies;
            _wordlistsVm = vms.Wordlists;
            _hitsVm = vms.Hits;
            _configsVm = vms.Configs;
            _pluginsVm = vms.Plugins;
            _proxyRepo = SP.GetService<IProxyRepository>();

            InitializeMonitoring();
            InitializeTimers();

            this.Loaded += Home_Loaded;
            this.IsVisibleChanged += Home_IsVisibleChanged;
        }

        private async void Home_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateServerTime();
            UpdateRealPerformanceData();
            UpdateAllStatsImmediately();
            DrawRecentHitsChart();

            // Give license loading time to complete
            await Task.Delay(1000);
            RefreshWelcome();
        }

        private void Home_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                RefreshWelcome();
            }
        }

        private void RefreshWelcome()
        {
            try
            {
                var license = Core.Services.Validate.GetLicenseInfo();
                if (license?.CustomerName != null)
                    txtWelcome.Text = $"Welcome back, {license.CustomerName}!";
                else
                    txtWelcome.Text = "Welcome back!";
            }
            catch
            {
                txtWelcome.Text = "Welcome back!";
            }
        }

        #region Initialization

        private void InitializeMonitoring()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue();
                _perfCountersAvailable = true;
            }
            catch
            {
                _perfCountersAvailable = false;
            }
        }

        private void SubscribeToChanges()
        {
            if (!_jobsSubscribed && _jobsVm?.JobsCollection != null)
            {
                _jobsVm.JobsCollection.CollectionChanged += (s, ev) =>
                    UpdateStatCard(statJobs, _jobsVm.JobsCollection.Count.ToString());
                _jobsSubscribed = true;
            }

            if (!_wordlistsSubscribed && _wordlistsVm?.WordlistsCollection != null)
            {
                _wordlistsVm.WordlistsCollection.CollectionChanged += (s, ev) =>
                {
                    UpdateStatCard(statWordlists, _wordlistsVm.WordlistsCollection.Count.ToString());
                    UpdateLinesStat();
                };
                _wordlistsSubscribed = true;
            }

            if (!_hitsSubscribed && _hitsVm?.HitsCollection != null)
            {
                _hitsVm.HitsCollection.CollectionChanged -= OnHitsCollectionChanged;
                _hitsVm.HitsCollection.CollectionChanged += OnHitsCollectionChanged;
                _hitsSubscribed = true;
            }

            if (!_configsSubscribed && _configsVm?.ConfigsCollection != null)
            {
                _configsVm.ConfigsCollection.CollectionChanged += (s, ev) =>
                    UpdateStatCard(statConfigs, _configsVm.ConfigsCollection.Count.ToString());
                _configsSubscribed = true;
            }

            if (!_pluginsSubscribed && _pluginsVm?.PluginsCollection != null)
            {
                _pluginsVm.PluginsCollection.CollectionChanged += (s, ev) =>
                    UpdateStatCard(statPlugins, _pluginsVm.PluginsCollection.Count.ToString());
                _pluginsSubscribed = true;
            }

            if (!_proxiesSubscribed && _proxiesVm?.ProxiesCollection != null)
            {
                _proxiesVm.ProxiesCollection.CollectionChanged += (s, ev) =>
                    UpdateStatCard(statProxies, _proxiesVm.ProxiesCollection.Count.ToString());
                _proxiesSubscribed = true;
            }
        }

        private void OnHitsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateStatCard(statHits, _hitsVm.HitsCollection.Count.ToString());
            DrawRecentHitsChart();
        }

        #endregion

        #region Performance Updates

        private void UpdateRealPerformanceData()
        {
            try
            {
                float cpu = _perfCountersAvailable ? _cpuCounter.NextValue() : 0;
                txtCpuValue.Text = $"{cpu:F1}%";
                progCpu.Value = Math.Max(0, Math.Min(100, cpu));
            }
            catch
            {
                txtCpuValue.Text = "0.0%";
                progCpu.Value = 0;
            }

            UpdateMemoryStats();
            UpdateNetworkStats();
        }

        private void UpdateMemoryStats()
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    ulong totalMB = memStatus.ullTotalPhys / (1024 * 1024);
                    ulong availMB = memStatus.ullAvailPhys / (1024 * 1024);
                    ulong usedMB = totalMB - availMB;
                    double percent = totalMB > 0 ? ((double)usedMB / totalMB) * 100.0 : 0;

                    txtMemValue.Text = $"{percent:F1}%";
                    progMem.Value = Math.Max(0, Math.Min(100, percent));
                    txtMemPercent.Text = $"{usedMB:N0} MB / {totalMB:N0} MB";
                }
                else
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Memory update failed: {ex.Message}");
                txtMemValue.Text = "Error";
                txtMemPercent.Text = "Unavailable";
            }
        }

        private void UpdateNetworkStats()
        {
            try
            {
                long totalRecv = 0;
                long totalSent = 0;
                bool anyValid = false;

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    try
                    {
                        var stats = ni.GetIPv4Statistics();
                        totalRecv += stats.BytesReceived;
                        totalSent += stats.BytesSent;
                        anyValid = true;
                    }
                    catch { }
                }

                if (anyValid)
                {
                    var now = DateTime.Now;
                    var timeSpan = (now - _lastNetworkCheck).TotalSeconds;

                    if (timeSpan > 0 && _lastNetworkCheck != default)
                    {
                        long recvDiff = Math.Max(0, totalRecv - _lastTotalBytesReceived);
                        long sentDiff = Math.Max(0, totalSent - _lastTotalBytesSent);

                        long recvPerSec = (long)(recvDiff / timeSpan);
                        long sentPerSec = (long)(sentDiff / timeSpan);

                        txtNetDown.Text = $"{FormatBytes(recvPerSec)}/s";
                        txtNetUp.Text = $"{FormatBytes(sentPerSec)}/s";
                    }

                    _lastTotalBytesReceived = totalRecv;
                    _lastTotalBytesSent = totalSent;
                    _lastNetworkCheck = now;
                }
                else
                {
                    txtNetDown.Text = "0 B/s";
                    txtNetUp.Text = "0 B/s";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Network update failed: {ex.Message}");
                txtNetDown.Text = "0 B/s";
                txtNetUp.Text = "0 B/s";
            }
        }

        #endregion

        #region Stats Updates

        private void UpdateAllStatsImmediately()
        {
            SubscribeToChanges();

            UpdateStatCard(statJobs, _jobsVm?.JobsCollection?.Count.ToString() ?? "0");
            UpdateStatCard(statWordlists, _wordlistsVm?.WordlistsCollection?.Count.ToString() ?? "0");
            UpdateStatCard(statHits, _hitsVm?.HitsCollection?.Count.ToString() ?? "0");
            UpdateStatCard(statConfigs, _configsVm?.ConfigsCollection?.Count.ToString() ?? "0");
            UpdateStatCard(statPlugins, _pluginsVm?.PluginsCollection?.Count.ToString() ?? "0");
            UpdateLinesStat();
            UpdateStatCard(statProxies, _proxiesVm?.ProxiesCollection?.Count.ToString() ?? "0");
        }

        private void UpdateStatCard(StatCard card, string value)
        {
            Dispatcher.Invoke(() =>
            {
                if (card != null) card.Number = value;
            });
        }

        private void UpdateLinesStat()
        {
            long total = 0;
            if (_wordlistsVm?.WordlistsCollection != null)
            {
                foreach (var wl in _wordlistsVm.WordlistsCollection)
                    total += wl.Total;
            }
            UpdateStatCard(statLines, total.ToString("N0"));
        }

        private Task UpdateProxyCountAsync()
        {
            UpdateStatCard(statProxies, _proxiesVm?.ProxiesCollection?.Count.ToString() ?? "0");
            return Task.CompletedTask;
        }

        private async Task UpdateCollectionStatsAsync()
        {
            SubscribeToChanges();
            await UpdateProxyCountAsync();
            Dispatcher.Invoke(() => DrawRecentHitsChart());
        }

        #endregion

        #region Timers

        private void InitializeTimers()
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (s, e) => UpdateServerTime();
            _clockTimer.Start();

            _perfTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _perfTimer.Tick += (s, e) => UpdateRealPerformanceData();
            _perfTimer.Start();

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _statsTimer.Tick += async (s, e) => await UpdateCollectionStatsAsync();
            _statsTimer.Start();
        }

        private void UpdateServerTime()
        {
            txtServerTime.Text = DateTime.Now.ToString("ddd, MMM dd, yyyy hh:mm:ss tt");
            TimeSpan ts = DateTime.Now - _appStartTime;
            txtUptime.Text = ts.ToString(@"hh\:mm\:ss");
        }

        #endregion

        #region Chart — Restyled with hit counts on bars

        private void DrawRecentHitsChart()
        {
            var canvas = HitsCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();

            var hits = _hitsVm?.HitsCollection;
            if (hits == null || hits.Count == 0) return;

            var now = DateTime.Now;
            var labels = new List<string>();
            var data = new List<int>();

            for (int i = 6; i >= 0; i--)
            {
                var day = now.AddDays(-i);
                labels.Add(day.ToString("ddd"));
                int count = hits.Count(h => h.Date.Date == day.Date);
                data.Add(count);
            }

            double maxVal = Math.Max(data.Max(), 1);
            if (ChartGrid.ActualHeight <= 0 || ChartGrid.ActualWidth <= 0) return;

            double chartHeight = ChartGrid.ActualHeight;
            double chartWidth = ChartGrid.ActualWidth;
            double padding = 30; // left/right padding
            double bottomMargin = 25; // space for labels
            double topMargin = 20; // space for value labels
            double availableHeight = chartHeight - bottomMargin - topMargin;
            double availableWidth = chartWidth - (padding * 2);
            double barCount = data.Count;
            double gap = 12; // gap between bars
            double barWidth = (availableWidth - (gap * (barCount - 1))) / barCount;

            // Draw subtle horizontal grid lines
            for (int i = 0; i <= 4; i++)
            {
                double y = topMargin + (availableHeight * i / 4);
                var line = new Line
                {
                    X1 = padding,
                    Y1 = y,
                    X2 = chartWidth - padding,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);
            }

            for (int i = 0; i < data.Count; i++)
            {
                double barHeight = (data[i] / maxVal) * availableHeight;
                double x = padding + (i * (barWidth + gap));
                double y = chartHeight - bottomMargin - barHeight;

                // Bar background (subtle track)
                var trackRect = new Rectangle
                {
                    Width = barWidth,
                    Height = availableHeight,
                    Fill = new SolidColorBrush(Color.FromArgb(20, 209, 153, 0)),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(trackRect, x);
                Canvas.SetTop(trackRect, topMargin);
                canvas.Children.Add(trackRect);

                // Actual bar
                var barColor = data[i] > 0
                    ? Color.FromArgb(255, 209, 153, 0) // gold
                    : Color.FromArgb(80, 209, 153, 0);  // dim gold for zero

                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = new SolidColorBrush(barColor),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                canvas.Children.Add(rect);

                // Hit count label on top of bar
                if (barHeight > 15 || data[i] > 0)
                {
                    var countLabel = new TextBlock
                    {
                        Text = data[i].ToString(),
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 209, 153, 0)),
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Width = barWidth,
                        TextAlignment = TextAlignment.Center
                    };
                    Canvas.SetLeft(countLabel, x);
                    Canvas.SetTop(countLabel, y - 16);
                    canvas.Children.Add(countLabel);
                }

                // Day label below bar
                var label = new TextBlock
                {
                    Text = labels[i],
                    Foreground = new SolidColorBrush(Color.FromArgb(180, 170, 170, 170)),
                    FontSize = 10,
                    Width = barWidth,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, x);
                Canvas.SetTop(label, chartHeight - bottomMargin + 6);
                canvas.Children.Add(label);
            }

            // Total hits summary at top left
            int totalWeek = data.Sum();
            var summary = new TextBlock
            {
                Text = $"{totalWeek} hits this week",
                Foreground = new SolidColorBrush(Color.FromArgb(150, 170, 170, 170)),
                FontSize = 11,
                FontStyle = FontStyles.Italic
            };
            Canvas.SetLeft(summary, padding);
            Canvas.SetTop(summary, 2);
            canvas.Children.Add(summary);
        }

        #endregion

        private string FormatBytes(double bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes = bytes / 1024;
            }
            return $"{bytes:0.#} {sizes[order]}";
        }

        private void ShowChangelog(object sender, RoutedEventArgs e) { }
        private void start(object sender, RoutedEventArgs e) { }

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
    }
}
