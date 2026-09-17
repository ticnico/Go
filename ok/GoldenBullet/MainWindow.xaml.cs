using Core.Models.Settings;
using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views;
using GoldenBullet.Views.Pages;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using RuriLib.Models.Configs;
using RuriLib.Models.Jobs;
using RuriLib.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BrushHelper = GoldenBullet.Helpers.Brush;

namespace GoldenBullet
{
    public partial class MainWindow : MetroWindow
    {
        private readonly UpdateService updateService;
        private readonly MainWindowViewModel vm;

        private Home homePage;
        private Jobs jobsPage;
        private GoldenBullet.Views.Pages.Monitor monitorPage;
        private MultiRunJobViewer multiRunJobViewerPage;
        private ProxyCheckJobViewer proxyCheckJobViewerPage;
        private Proxies proxiesPage;
        private Wordlists wordlistsPage;
        private Configs configsPage;
        private Views.Pages.ConfigMetadata configMetadataPage;
        private ConfigReadme configReadmePage;
        private ConfigEditor configEditorPage;
        private Views.Pages.ConfigSettings configSettingsPage;
        private Hits hitsPage;
        private GBSettings obSettingsPage;
        private RLSettings rlSettingsPage;
        private Plugins pluginsPage;
        private About aboutPage;

        private NetflixCookiePage netflixCookiePage;
        private NFTokenCheckerPage NFTokenCheckerPage;

        private UlpToComboPage ulpToComboPage;
        private KeywordRemoverPage keywordRemoverPage;
        private ProxyCheckerPage proxyCheckerPage;
        private LogsToUlpPage logsToUlpPage;
        private SiteScannerPage siteScannerPage;

        public Page CurrentPage { get; private set; }

        private bool _toolsSubmenuExpanded = false;
        private bool _configsSubmenuExpanded = false;
        private bool _configEditorSessionActive = false;

        public MainWindow()
        {
            InitializeComponent();

            vm = new MainWindowViewModel();
            DataContext = vm;
            Closing += vm.OnWindowClosing;

            configsPage = new Configs();

            updateService = SP.GetService<UpdateService>();
            var obSettingsService = SP.GetService<GoldenBulletSettingsService>();

            Title = $"GoldenBullet - {updateService.CurrentVersion} [{updateService.CurrentVersionType}]";

            var customization = obSettingsService.Settings.CustomizationSettings;
            SetTheme(customization);

            ConfigsArrow.Visibility = Visibility.Collapsed;
            ConfigsSubStackPanel.Visibility = Visibility.Collapsed;

            vm.ConfigSelected += config =>
            {
                if (config == null && _configEditorSessionActive)
                    return;
                ConfigsMainButton.IsChecked = true;
                ConfigsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");

                var isEditingConfig = CurrentPage is ConfigEditor;
                if (isEditingConfig && vm.IsConfigSelected)
                {
                    ConfigsArrow.Visibility = Visibility.Visible;
                    ConfigsArrow.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
                    ConfigsSubStackPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ConfigsArrow.Visibility = Visibility.Collapsed;
                    ConfigsSubStackPanel.Visibility = Visibility.Collapsed;
                    _configsSubmenuExpanded = false;
                }
            };

            Debug.WriteLine("🔍 MainWindow constructor completed");
        }

        // ✅ NO DIALOG POPUP — Silent online check only
        private async void MetroWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);
            Debug.WriteLine("🔍 MetroWindow_Loaded: Silent online license check...");

            var status = await Validate.CheckActivationAsync();

            if (status == ActivationStatus.Valid)
            {
                Debug.WriteLine("✅ Online license valid — full version");
            }
            else
            {
                Debug.WriteLine("⚠️ No valid online license — trial mode");
            }

            // Always update UI (shows Activated or Trial banner)
            CheckLicense();
        }

        // ✅ Updated to use LicenseValidator
        public void CheckLicense()
        {
            try
            {
                bool isActivated = Validate.IsActivated();
                if (isActivated)
                {
                    if (TrialBanner != null)
                        TrialBanner.Visibility = Visibility.Collapsed;

                    if (TxtActivationStatus != null)
                    {
                        TxtActivationStatus.Text = "Activated";
                        TxtActivationStatus.Foreground = Brushes.LightGreen;
                    }

                    if (IconActivation != null)
                        IconActivation.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.LockCheck;

                    if (ActivationButton != null)
                        ActivationButton.Visibility = Visibility.Collapsed;

                    this.Title = $"GoldenBullet - {updateService.CurrentVersion} [Activated]";

                    Debug.WriteLine("✅ UI: Activated");
                }
                else
                {
                    Debug.WriteLine("⚠️ UI: Trial mode");

                    if (TrialBanner != null)
                        TrialBanner.Visibility = Visibility.Visible;

                    if (TxtActivationStatus != null)
                    {
                        TxtActivationStatus.Text = "Trial";
                        TxtActivationStatus.Foreground = Brushes.Orange;
                    }

                    if (IconActivation != null)
                        IconActivation.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.LockOutline;

                    if (ActivationButton != null)
                        ActivationButton.Visibility = Visibility.Visible;

                    this.Title = $"GoldenBullet - {updateService.CurrentVersion} [Trial]";

                    Debug.WriteLine("✅ UI: Trial");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CheckLicense error: {ex.Message}");
            }
        }

        public void ForceRefresh()
        {
            Debug.WriteLine("🔄 ForceRefresh() called");

            try
            {
                CheckLicense();

                var currentPage = mainFrame?.Content as Page;
                if (currentPage != null)
                {
                    Debug.WriteLine($"ℹ️ Current page: {currentPage.GetType().Name}");

                    if (currentPage is MultiRunJobViewer || currentPage is ProxyCheckJobViewer || currentPage is ConfigEditor)
                    {
                        Debug.WriteLine("⚠️ Skipping page recreation for job viewer");
                        return;
                    }

                    var newPage = Activator.CreateInstance(currentPage.GetType()) as Page;
                    if (newPage != null)
                    {
                        mainFrame.Content = newPage;
                        Debug.WriteLine("✅ Page refreshed");
                    }
                }

                this.UpdateLayout();

                Debug.WriteLine("✅ ForceRefresh completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ForceRefresh error: {ex.Message}");
            }
        }

        // ✅ Opens ActivationDialog when user clicks button
        private async void ActivationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("🔓 User clicked activation");

                var activationDialog = new ADialog();
                activationDialog.Owner = this;
                activationDialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                var result = activationDialog.ShowDialog();

                if (result == true)
                {
                    Debug.WriteLine("✅ Activation successful");
                    await Validate.CheckActivationAsync();
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        CheckLicense();
                        ForceRefresh();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Activation error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ✅ Updated for online deactivation
        private async void BtnActivationStatus_Click(object sender, RoutedEventArgs e)
        {
            var license = Validate.GetLicenseInfo();

            if (license != null)
            {
                var dialog = new Status(license);
                dialog.Owner = this;
                var result = dialog.ShowDialog();

                if (result == true)
                {
                    bool deactivated = await Validate.DeactivateAsync();

                    if (deactivated)
                    {
                        Validate.ClearCache();
                        CheckLicense();
                        ForceRefresh();
                    }
                    else
                    {
                        await this.ShowMessageAsync("Error",
                            "❌ Failed to deactivate license online.\n\nCheck your internet connection.");
                    }
                }
            }
            else
            {
                await this.ShowMessageAsync("License Status",
                    "⚠️ Trial Mode\n\n" +
                    "Activate your license to unlock all features.\n\n" +
                    "Hardware ID: " + Info.GetMachineID());
            }
        }

        #region Navigation

        public void NavigateTo(MainWindowPage page, ToggleButton submenuButton = null)
        {
            if (CurrentPage == configEditorPage)
                configEditorPage?.OnPageChanged();

            switch (page)
            {
                case MainWindowPage.Home:
                    homePage ??= new Home();
                    ChangePage(homePage, HomeButton);
                    break;

                case MainWindowPage.Jobs:
                    jobsPage ??= new Jobs();
                    ChangePage(jobsPage, JobsButton);
                    break;

                case MainWindowPage.Monitor:
                    monitorPage ??= new GoldenBullet.Views.Pages.Monitor();
                    ChangePage(monitorPage, null);
                    break;

                case MainWindowPage.Proxies:
                    proxiesPage ??= new Proxies();
                    proxiesPage.UpdateViewModel();
                    ChangePage(proxiesPage, ProxiesButton);
                    break;

                case MainWindowPage.Wordlists:
                    wordlistsPage ??= new Wordlists();
                    ChangePage(wordlistsPage, WordlistsButton);
                    break;

                case MainWindowPage.Configs:
                    configsPage.UpdateViewModel();
                    ChangePage(configsPage, ConfigsMainButton);
                    break;

                case MainWindowPage.Hits:
                    hitsPage ??= new Hits();
                    hitsPage.UpdateViewModel();
                    ChangePage(hitsPage, HitsButton);
                    break;

                case MainWindowPage.Plugins:
                    pluginsPage ??= new Plugins();
                    ChangePage(pluginsPage, PluginsButton);
                    break;

                case MainWindowPage.GBSettings:
                    obSettingsPage ??= new GBSettings();
                    ChangePage(obSettingsPage, GBSettingsButton);
                    break;

                case MainWindowPage.RLSettings:
                    rlSettingsPage ??= new RLSettings();
                    ChangePage(rlSettingsPage, RLSettingsButton);
                    break;

                case MainWindowPage.About:
                    aboutPage ??= new About();
                    ChangePage(aboutPage, AboutButton);
                    break;

                case MainWindowPage.ConfigMetadata:
                    configMetadataPage ??= new Views.Pages.ConfigMetadata();
                    configMetadataPage.UpdateViewModel();
                    ChangePage(configMetadataPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigReadme:
                    configReadmePage ??= new ConfigReadme();
                    configReadmePage.UpdateViewModel();
                    ChangePage(configReadmePage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigStacker:
                    if (vm.Config == null || (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.LoliCode))
                        return;
                    configEditorPage ??= new ConfigEditor();
                    configEditorPage.NavigateTo(ConfigEditorSection.Stacker);
                    ChangePage(configEditorPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigLoliCode:
                    if (vm.Config == null || (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.LoliCode))
                        return;
                    configEditorPage ??= new ConfigEditor();
                    configEditorPage.NavigateTo(ConfigEditorSection.LoliCode);
                    ChangePage(configEditorPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigSettings:
                    configSettingsPage ??= new Views.Pages.ConfigSettings();
                    configSettingsPage.UpdateViewModel();
                    ChangePage(configSettingsPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigCSharpCode:
                    if (vm.Config == null || (vm.Config.Mode is not ConfigMode.Stack and not ConfigMode.CSharp and not ConfigMode.LoliCode))
                        return;
                    configEditorPage ??= new ConfigEditor();
                    configEditorPage.NavigateTo(ConfigEditorSection.CSharp);
                    ChangePage(configEditorPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ConfigLoliScript:
                    if (vm.Config == null || vm.Config.Mode is not ConfigMode.Legacy)
                        return;
                    configEditorPage ??= new ConfigEditor();
                    configEditorPage.NavigateTo(ConfigEditorSection.LoliScript);
                    ChangePage(configEditorPage, null, keepConfigButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.NetflixCookie:
                    netflixCookiePage ??= new NetflixCookiePage();
                    ChangePage(netflixCookiePage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.NFTokenChecker:
                    NFTokenCheckerPage ??= new NFTokenCheckerPage();
                    ChangePage(NFTokenCheckerPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.UlpToCombo:
                    ulpToComboPage ??= new UlpToComboPage();
                    ChangePage(ulpToComboPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.KeywordRemover:
                    keywordRemoverPage ??= new KeywordRemoverPage();
                    ChangePage(keywordRemoverPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.ProxyChecker:
                    proxyCheckerPage ??= new ProxyCheckerPage();
                    ChangePage(proxyCheckerPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.LogsToUlp:
                    logsToUlpPage ??= new LogsToUlpPage();
                    ChangePage(logsToUlpPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;

                case MainWindowPage.SiteScanner:
                    siteScannerPage ??= new SiteScannerPage();
                    ChangePage(siteScannerPage, null, keepConfigButtonChecked: false, keepToolsButtonChecked: true, keepSubmenuButtonChecked: submenuButton);
                    break;
            }

            if (submenuButton != null)
            {
                if (ToolsSubStackPanel?.Children.Contains(submenuButton) == true)
                {
                    ToolsMainButton.IsChecked = true;
                    ToolsMainButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
                    if (!_toolsSubmenuExpanded)
                    {
                        ToolsSubStackPanel.Visibility = Visibility.Visible;
                        ToolsArrow.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp;
                        _toolsSubmenuExpanded = true;
                    }
                }
                else if (ConfigsSubStackPanel?.Children.Contains(submenuButton) == true)
                {
                    ConfigsMainButton.IsChecked = true;
                    ConfigsMainButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
                    if (!_configsSubmenuExpanded)
                    {
                        ConfigsSubStackPanel.Visibility = Visibility.Visible;
                        ConfigsArrow.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp;
                        _configsSubmenuExpanded = true;
                    }
                }
            }
        }

        public void NavigateToJobOptions(Page optionsPage)
        {
            CurrentPage = optionsPage;
            mainFrame.Content = optionsPage;
            UncheckAllButtons(SidebarStackPanel, null, false, false);
        }

        public void SetMainContent(Page page) => mainFrame.Content = page;

        public void DisplayJob(JobViewModel jobVM)
        {
            switch (jobVM)
            {
                case MultiRunJobViewModel mrj:
                    multiRunJobViewerPage ??= new MultiRunJobViewer();
                    multiRunJobViewerPage.BindViewModel(mrj);
                    ChangePage(multiRunJobViewerPage, null);
                    break;
                case ProxyCheckJobViewModel pcj:
                    proxyCheckJobViewerPage ??= new ProxyCheckJobViewer();
                    proxyCheckJobViewerPage.BindViewModel(pcj);
                    ChangePage(proxyCheckJobViewerPage, null);
                    break;
            }
        }

        public void EditJob(JobViewModel jobVM)
        {
            NavigateTo(MainWindowPage.Jobs);
            jobsPage.EditJob(jobVM);
        }

        public void ShowConfigsArrowForEditor()
        {
            try
            {
                ConfigsArrow.Visibility = Visibility.Visible;
                ConfigsArrow.Kind = MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
                ConfigsSubStackPanel.Visibility = Visibility.Collapsed;
                _configsSubmenuExpanded = false;
                _configEditorSessionActive = true;
                ConfigsMainButton.IsChecked = true;
                ConfigsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowConfigsArrowForEditor error: {ex.Message}");
            }
        }

        public void EndConfigEditorSession()
        {
            _configEditorSessionActive = false;
            if (!vm.IsConfigSelected)
            {
                ConfigsArrow.Visibility = Visibility.Collapsed;
                ConfigsSubStackPanel.Visibility = Visibility.Collapsed;
                _configsSubmenuExpanded = false;
            }
        }

        private void ChangePage(Page newPage, ToggleButton newButton,
            bool keepConfigButtonChecked = false, bool keepToolsButtonChecked = false,
            ToggleButton keepSubmenuButtonChecked = null)
        {
            CurrentPage = newPage;
            _configEditorSessionActive = newPage is ConfigEditor ? true : _configEditorSessionActive;
            mainFrame.Content = newPage;

            UncheckAllButtons(SidebarStackPanel, newButton, keepConfigButtonChecked, keepToolsButtonChecked, keepSubmenuButtonChecked);

            if (newButton != null)
            {
                newButton.IsChecked = true;
                newButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
            }

            if (keepSubmenuButtonChecked != null)
            {
                keepSubmenuButtonChecked.IsChecked = true;
                keepSubmenuButtonChecked.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
            }
        }

        private void UncheckAllButtons(Panel panel, ToggleButton exceptButton,
            bool keepConfigButtonChecked, bool keepToolsButtonChecked = false,
            ToggleButton keepSubmenuButtonChecked = null)
        {
            if (panel == null) return;

            foreach (var child in panel.Children)
            {
                if (child is ToggleButton btn && btn != exceptButton)
                {
                    if (btn == keepSubmenuButtonChecked)
                        continue;

                    if (keepConfigButtonChecked && btn == ConfigsMainButton)
                    {
                        btn.Foreground = BrushHelper.Get("ForegroundMenuSelected");
                        continue;
                    }

                    if (keepToolsButtonChecked && btn == ToolsMainButton)
                    {
                        btn.Foreground = BrushHelper.Get("ForegroundMenuSelected");
                        continue;
                    }

                    btn.IsChecked = false;
                    btn.Foreground = BrushHelper.Get("ForegroundMain");
                }
                else if (child is Panel subPanel)
                {
                    UncheckAllButtons(subPanel, exceptButton, keepConfigButtonChecked, keepToolsButtonChecked, keepSubmenuButtonChecked);
                }
                else if (child is Grid grid)
                {
                    UncheckAllButtons(grid, exceptButton, keepConfigButtonChecked, keepToolsButtonChecked, keepSubmenuButtonChecked);
                }
            }
        }

        #endregion

        #region Sidebar Click Handlers

        private void ToggleSidebarButton(ToggleButton clickedButton)
        {
            UncheckAllButtons(SidebarStackPanel, clickedButton, keepConfigButtonChecked: false, keepToolsButtonChecked: false);

            if (clickedButton != null)
            {
                clickedButton.IsChecked = true;
                clickedButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700"));
            }
        }

        private void OpenHomePage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(homePage ??= new Home(), sender as ToggleButton); }
        private void OpenJobsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(jobsPage ??= new Jobs(), sender as ToggleButton); }
        private void OpenMonitorPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(monitorPage ??= new GoldenBullet.Views.Pages.Monitor(), sender as ToggleButton); }

        private void OpenProxiesPage(object sender, RoutedEventArgs e)
        {
            proxiesPage ??= new Proxies();
            proxiesPage.UpdateViewModel();
            ToggleSidebarButton(sender as ToggleButton);
            ChangePage(proxiesPage, sender as ToggleButton);
        }

        private void OpenWordlistsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(wordlistsPage ??= new Wordlists(), sender as ToggleButton); }
        private void OpenConfigsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(configsPage ??= new Configs(), sender as ToggleButton); }

        private void OpenHitsPage(object sender, RoutedEventArgs e)
        {
            hitsPage ??= new Hits();
            hitsPage.UpdateViewModel();
            ToggleSidebarButton(sender as ToggleButton);
            ChangePage(hitsPage, sender as ToggleButton);
        }

        private void OpenPluginsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(pluginsPage ??= new Plugins(), sender as ToggleButton); }
        private void OpenGBSettingsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(obSettingsPage ??= new GBSettings(), sender as ToggleButton); }
        private void OpenRLSettingsPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(rlSettingsPage ??= new RLSettings(), sender as ToggleButton); }
        private void OpenAboutPage(object sender, RoutedEventArgs e) { ToggleSidebarButton(sender as ToggleButton); ChangePage(aboutPage ??= new About(), sender as ToggleButton); }

        #endregion

        #region Configs Submenu Logic

        private void ConfigsArrow_Click(object sender, RoutedEventArgs e)
        {
            if (!vm.IsConfigSelected)
            {
                Alert.Info("No Config Selected", "Please select a config first to access sub-options.");
                e.Handled = true;
                return;
            }

            _configsSubmenuExpanded = !_configsSubmenuExpanded;
            ConfigsSubStackPanel.Visibility = _configsSubmenuExpanded ? Visibility.Visible : Visibility.Collapsed;
            ConfigsArrow.Kind = _configsSubmenuExpanded
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp
                : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;

            if (_configsSubmenuExpanded)
            {
                ConfigsMainButton.IsChecked = true;
                ConfigsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
            }

            e.Handled = true;
        }

        private void ConfigsMainButton_Click(object sender, RoutedEventArgs e)
        {
            if (e is RoutedEventArgs re && re.Handled)
                return;

            NavigateTo(MainWindowPage.Configs);

            UncheckAllButtons(SidebarStackPanel, ConfigsMainButton, keepConfigButtonChecked: true, keepToolsButtonChecked: false);

            if (!vm.IsConfigSelected)
            {
                ConfigsArrow.Visibility = Visibility.Collapsed;
                ConfigsSubStackPanel.Visibility = Visibility.Collapsed;
                _configsSubmenuExpanded = false;
                ConfigsMainButton.IsChecked = true;
                ConfigsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
                return;
            }

            _configsSubmenuExpanded = !_configsSubmenuExpanded;
            ConfigsSubStackPanel.Visibility = _configsSubmenuExpanded ? Visibility.Visible : Visibility.Collapsed;
            ConfigsArrow.Kind = _configsSubmenuExpanded
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp
                : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
            ConfigsArrow.Visibility = Visibility.Visible;

            ConfigsMainButton.IsChecked = true;
            ConfigsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
        }

        private void OpenMetadataPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenReadmePage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenStackerPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenLoliCodePage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenConfigSettingsPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenCSharpCodePage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenLoliScriptPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }

        #endregion

        #region Tools Submenu Logic

        private void ToolsMainButton_Click(object sender, RoutedEventArgs e)
        {
            _toolsSubmenuExpanded = !_toolsSubmenuExpanded;
            ToolsSubStackPanel.Visibility = _toolsSubmenuExpanded ? Visibility.Visible : Visibility.Collapsed;
            ToolsArrow.Kind = _toolsSubmenuExpanded
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp
                : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;
            ToolsArrow.Visibility = Visibility.Visible;

            UncheckAllButtons(SidebarStackPanel, ToolsMainButton, keepConfigButtonChecked: false, keepToolsButtonChecked: true);
            ToolsMainButton.IsChecked = true;
            ToolsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
        }

        private void OpenSiteScannerPage(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page))
            {
                NavigateTo(page, btn);
            }
        }


        private void ToolsArrow_Click(object sender, RoutedEventArgs e)
        {
            _toolsSubmenuExpanded = !_toolsSubmenuExpanded;
            ToolsSubStackPanel.Visibility = _toolsSubmenuExpanded ? Visibility.Visible : Visibility.Collapsed;
            ToolsArrow.Kind = _toolsSubmenuExpanded
                ? MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronUp
                : MahApps.Metro.IconPacks.PackIconMaterialKind.ChevronDown;

            if (_toolsSubmenuExpanded)
            {
                ToolsMainButton.IsChecked = true;
                ToolsMainButton.Foreground = BrushHelper.Get("ForegroundMenuSelected");
            }

            e.Handled = true;
        }

        private void OpenNetflixCookiePage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenNFTokenCheckerPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }

        private void OpenUlpToComboPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenKeywordRemoverPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenProxyCheckerPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }
        private void OpenLogsToUlpPage(object sender, RoutedEventArgs e) { if (sender is ToggleButton btn && btn.Tag is string tag && Enum.TryParse<MainWindowPage>(tag, out var page)) { NavigateTo(page, btn); } }

        #endregion

        #region Theme

        public void SetTheme(CustomizationSettings customization)
        {
            BrushHelper.SetAppColor("BackgroundMain", customization.BackgroundMain);
            BrushHelper.SetAppColor("BackgroundSecondary", customization.BackgroundSecondary);
            BrushHelper.SetAppColor("BackgroundInput", customization.BackgroundInput);
            BrushHelper.SetAppColor("ForegroundMain", customization.ForegroundMain);
            BrushHelper.SetAppColor("ForegroundInput", customization.ForegroundInput);
            BrushHelper.SetAppColor("ForegroundGood", customization.ForegroundGood);
            BrushHelper.SetAppColor("ForegroundBad", customization.ForegroundBad);
            BrushHelper.SetAppColor("ForegroundCustom", customization.ForegroundCustom);
            BrushHelper.SetAppColor("ForegroundRetry", customization.ForegroundRetry);
            BrushHelper.SetAppColor("ForegroundBanned", customization.ForegroundBanned);
            BrushHelper.SetAppColor("ForegroundToCheck", customization.ForegroundToCheck);
            BrushHelper.SetAppColor("ForegroundMenuSelected", "#FFD700");
            BrushHelper.SetAppColor("SuccessButton", customization.SuccessButton);
            BrushHelper.SetAppColor("PrimaryButton", customization.PrimaryButton);
            BrushHelper.SetAppColor("WarningButton", customization.WarningButton);
            BrushHelper.SetAppColor("DangerButton", customization.DangerButton);
            BrushHelper.SetAppColor("ForegroundButton", customization.ForegroundButton);
            BrushHelper.SetAppColor("BackgroundButton", customization.BackgroundButton);

            if (File.Exists(customization.BackgroundImagePath))
            {
                Background = new ImageBrush(
                    new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(customization.BackgroundImagePath)))
                {
                    Opacity = customization.BackgroundOpacity / 100,
                    Stretch = Stretch.UniformToFill
                };
            }
            else
            {
                Background = BrushHelper.Get("BackgroundMain");
            }
        }

        #endregion

        private void ToggleButton_Checked(RoutedEventArgs e) { }
    }

    public class MainWindowViewModel : ViewModelBase
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly JobManagerService jobManagerService;
        private readonly ConfigService configService;

        public event Action<Config> ConfigSelected;
        public Config Config => configService.SelectedConfig;
        public bool IsConfigSelected => Config != null;

        public MainWindowViewModel()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            jobManagerService = SP.GetService<JobManagerService>();
            configService = SP.GetService<ConfigService>();

            configService.OnConfigSelected += (sender, config) =>
            {
                OnPropertyChanged(nameof(IsConfigSelected));
                ConfigSelected?.Invoke(config);
            };
        }

        public void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved &&
                Config != null && Config.HasUnsavedChanges())
            {
                e.Cancel = !Alert.Choice("Config not saved",
                    $"The config you are editing ({Config.Metadata.Name}) has unsaved changes, are you sure you want to quit?");
            }

            if (e.Cancel) return;

            if (jobManagerService.Jobs.Any(j => j.Status != JobStatus.Idle))
            {
                e.Cancel = !Alert.Choice("Job(s) running", "One or more jobs are still running, are you sure you want to quit?");
            }
        }
    }

    public enum MainWindowPage
    {
        Home, NetflixCookie, Jobs, Monitor, Proxies, Wordlists, Configs,
        ConfigMetadata, ConfigReadme, ConfigStacker, ConfigLoliCode, ConfigSettings,
        ConfigCSharpCode, ConfigLoliScript, Hits, Plugins, GBSettings, RLSettings, About,
        UlpToCombo, KeywordRemover, ProxyChecker, LogsToUlp, NFTokenChecker, SiteScanner
    }
}
