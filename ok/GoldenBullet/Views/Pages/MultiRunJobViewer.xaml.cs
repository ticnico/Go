using Core.Models.Jobs;
using Core.Repositories;
using Core.Services;
using GoldenBullet.Extensions;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Newtonsoft.Json;
using RuriLib.Models.Configs;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for MultiRunJobViewer.xaml
    /// </summary>
    public partial class MultiRunJobViewer : Page
    {
        private readonly MainWindow mainWindow;
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly IJobRepository jobRepo;
        private MultiRunJobViewerViewModel vm;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private MainWindow MainWindow => Window.GetWindow(this) as MainWindow;

        private IEnumerable<HitViewModel> SelectedHits => hitsListView.SelectedItems.Cast<HitViewModel>().ToList();

        public MultiRunJobViewer()
        {
            mainWindow = SP.GetService<MainWindow>();
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            jobRepo = SP.GetService<IJobRepository>();
            InitializeComponent();

            // Show customer name instead of "Core's job"
            try
            {
                var license = Core.Services.Validate.GetLicenseInfo();
                if (license?.CustomerName != null)
                    txtJobTitle.Text = $"{license.CustomerName}'s Job";
            }
            catch { /* keep default if license fails */ }
        }

        public void BindViewModel(MultiRunJobViewModel jobVM)
        {
            if (vm is not null)
            {
                vm.Dispose();
                try { vm.NewMessage -= OnResultMessage; } catch { }
            }

            vm = new MultiRunJobViewerViewModel(jobVM);
            vm.NewMessage += OnResultMessage;
            DataContext = vm;
        }

        private void GoBack(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private async Task ShowRequiredDialogAsync()
        {
            var mainWindow = MainWindow;

            if (mainWindow != null)
            {
                var dialog = new MainDialog(new RequiredDialog(), "🔒 Activation Required");
                dialog.ShowDialog();
                return;
            }

            MessageBox.Show(
                "Starting jobs requires an activated license.\n\n" +
                "You can create, edit, and clone jobs in trial mode,\n" +
                "but you must activate to start them.\n\n" +
                "Please purchase a license to unlock full functionality.",
                "🔒 Feature Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // ✅ FIXED: Start button now respects trial/activation limits (same pattern as Jobs page)
        private async void Start(object sender, RoutedEventArgs e)
        {
            // ✅ TRIAL MODE: Cannot start any job - Must activate license
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                return;
            }

            try
            {
                await vm.StartAsync();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Stop(object sender, RoutedEventArgs e)
        { try { await vm.StopAsync(); } catch (Exception ex) { Alert.Exception(ex); } }

        private async void Pause(object sender, RoutedEventArgs e)
        { try { await vm.PauseAsync(); } catch (Exception ex) { Alert.Exception(ex); } }

        private async void Resume(object sender, RoutedEventArgs e)
        { try { await vm.ResumeAsync(); } catch (Exception ex) { Alert.Exception(ex); } }

        private async void Abort(object sender, RoutedEventArgs e)
        { try { await vm.AbortAsync(); } catch (Exception ex) { Alert.Exception(ex); } }

        private void SkipWait(object sender, RoutedEventArgs e)
        { try { vm.SkipWait(); } catch (Exception ex) { Alert.Exception(ex); } }

        // ✅ UPDATED: ChangeOptions now uses the same edit flow as Jobs page
        private async void ChangeOptions(object sender, RoutedEventArgs e) => await EditJobAsync(vm.Job);

        private void ChangeBots(object sender, MouseButtonEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async void ChangeBots(int newValue)
        {
            try { await vm.ChangeBotsAsync(newValue); }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        // ✅ NEW: EditJobAsync - Mirrors the logic from Jobs.EditJob
        public async Task EditJobAsync(JobViewModel jobVM)
        {
            if (jobVM == null) return;

            try
            {
                var entity = await jobRepo.GetAsync(jobVM.Id);

                // Deserialize job options with proper settings
                var jobOptionsWrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings);
                var jobOptions = jobOptionsWrapper?.Options;

                if (jobOptions == null)
                {
                    Alert.Error("Deserialization Error", "Failed to load job options. The job data may be corrupted.");
                    return;
                }

                // Callback when user accepts changes
                Action<JobOptions> onAccept = async options =>
                {
                    var updatedJobVM = await SP.GetService<ViewModelsService>().Jobs.EditJobAsync(entity, options);

                    if (updatedJobVM is MultiRunJobViewModel updatedMrJob)
                    {
                        BindViewModel(updatedMrJob);
                    }
                };

                // Create the appropriate options dialog
                Page optionsPage = jobVM switch
                {
                    MultiRunJobViewModel => new MultiRunJobOptionsDialog(jobOptions as MultiRunJobOptions, onAccept),
                    ProxyCheckJobViewModel => new ProxyCheckJobOptionsDialog(jobOptions as ProxyCheckJobOptions, onAccept),
                    _ => throw new NotImplementedException($"Job type {jobVM.GetType().Name} not supported")
                };

                // Navigate to the options page within MainWindow
                var mw = MainWindow;
                if (mw != null)
                    mw.SetMainContent(optionsPage);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void CopySelectedHits(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(h => h.Data);

        private void CopySelectedProxies(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(h => h.Proxy);

        private void CopySelectedHitsCapture(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(h => $"{h.Data} | {h.Capture}");

        private void SendToDebugger(object sender, RoutedEventArgs e)
        {
            var hitVM = SelectedHits.FirstOrDefault();
            if (hitVM is not null)
            {
                var debugger = SP.GetService<ViewModelsService>().Debugger;
                debugger.TestData = hitVM.Data;

                if (hitVM.Hit.Proxy is not null)
                {
                    debugger.TestProxy = hitVM.Hit.Proxy.ToString();
                    debugger.ProxyType = hitVM.Hit.Proxy.Type;
                }
            }
        }

        private void SelectAll(object sender, RoutedEventArgs e) => hitsListView.SelectAll();

        private void ShowBotLog(object sender, RoutedEventArgs e)
        {
            var hitVM = SelectedHits.FirstOrDefault();
            if (hitVM is null) return;

            if (hitVM.Hit.Config.Mode == ConfigMode.DLL)
            {
                Alert.Error("Bot log unavailable", "The bot log is not available for pre-compiled configs");
                return;
            }

            new MainDialog(new BotLogDialog(hitVM.Hit.BotLogger), $"Bot log for {hitVM.Data}").Show();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                botsListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;
            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
                newDir = ListSortDirection.Descending;

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            botsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void LVIRightClick(object sender, MouseButtonEventArgs e) { }

        private void OnResultMessage(object sender, string message, Color color)
        {
            // Log removed - do nothing
        }
    }
}
