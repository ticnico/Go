using Core.Models.Jobs;
using Core.Repositories;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Newtonsoft.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for Jobs.xaml
    /// </summary>
    public partial class Jobs : Page
    {
        private readonly IJobRepository jobRepo;
        private readonly JobsViewModel vm;


        // ✅ JSON Settings (Reusable)
        private readonly JsonSerializerSettings jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        // ✅ Get MainWindow from visual tree (NOT from SP)
        private MainWindow MainWindow => Window.GetWindow(this) as MainWindow;

        public Jobs()
        {
            jobRepo = SP.GetService<IJobRepository>();
            vm = SP.GetService<ViewModelsService>().Jobs;
            DataContext = vm;

            InitializeComponent();
        }

        private void NewJob(object sender, RoutedEventArgs e)
        {
            // ✅ No Trial Limits - Users can create jobs freely
            new MainDialog(new CreateJobDialog(this), "Select job type").ShowDialog();
        }

        private void RemoveAll(object sender, RoutedEventArgs e)
        {
            try
            {
                vm.RemoveAll();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void RowClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is JobViewModel jobVM)
            {
                var mainWindow = MainWindow;
                if (mainWindow != null)
                    mainWindow.DisplayJob(jobVM);
            }
        }

        private async void StartJob(object sender, RoutedEventArgs e)
        {
            var jobVM = GetJobViewModel(sender);
            if (jobVM == null) return;

            // ✅ TRIAL MODE: Cannot start any job - Must activate license
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                return;
            }

            // ✅ Full Version: Check bot limit if needed
            if (jobVM is MultiRunJobViewModel mrJobVM)
            {
                int currentBots = mrJobVM.Bots;

            }

            try
            {
                switch (jobVM)
                {
                    case MultiRunJobViewModel mrJob:
                        mrJob.Job.Start();
                        break;
                    case ProxyCheckJobViewModel pcJob:
                        pcJob.Job.Start();
                        break;
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        // ✅ Helper: Activation Required Dialog (For Start Button in Trial)
        private Task ShowRequiredDialogAsync()
        {
            var mainWindow = MainWindow;

            if (mainWindow != null)
            {
                // ✅ Use new custom dialog (RequiredDialog)
                var dialog = new MainDialog(new RequiredDialog(), "🔒 Activation Required");
                dialog.ShowDialog();
                return Task.CompletedTask;
            }

            // ✅ Fallback to standard MessageBox
            MessageBox.Show(
                "Starting jobs requires an activated license.\n\n" +
                "You can create, edit, and clone jobs in trial mode,\n" +
                "but you must activate to start them.\n\n" +
                "Please purchase a license to unlock full functionality.",
                "🔒 Feature Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return Task.CompletedTask;
        }

        private void PauseJob(object sender, RoutedEventArgs e)
        {
            var jobVM = GetJobViewModel(sender);
            if (jobVM == null) return;

            try
            {
                switch (jobVM)
                {
                    case MultiRunJobViewModel mrJob:
                        mrJob.Job.Pause();
                        break;
                    case ProxyCheckJobViewModel pcJob:
                        pcJob.Job.Pause();
                        break;
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ForceStopJob(object sender, RoutedEventArgs e)
        {
            var jobVM = GetJobViewModel(sender);
            if (jobVM == null) return;

            try
            {
                switch (jobVM)
                {
                    case MultiRunJobViewModel mrJob:
                        mrJob.Job.Abort();
                        break;
                    case ProxyCheckJobViewModel pcJob:
                        pcJob.Job.Abort();
                        break;
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void EditJob(object sender, RoutedEventArgs e) => EditJob(GetJobViewModel(sender));

        public async void EditJob(JobViewModel jobVM)
        {
            if (jobVM == null) return;

            // ✅ No Trial Limit - Editing is allowed
            try
            {
                var entity = await jobRepo.GetAsync(jobVM.Id);

                // ✅ Fixed JSON Deserialization
                var jobOptionsWrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings);
                var jobOptions = jobOptionsWrapper?.Options;

                if (jobOptions == null)
                {
                    Alert.Error("Deserialization Error",
                        "Failed to load job options. The job data may be corrupted.");
                    return;
                }

                Action<JobOptions> onAccept = async options =>
                {
                    jobVM = await vm.EditJobAsync(entity, options);
                    var mainWindow = MainWindow;
                    if (mainWindow != null)
                        mainWindow.SetMainContent(new Jobs());
                };

                Page optionsPage = jobVM switch
                {
                    MultiRunJobViewModel mrVm => new MultiRunJobOptionsDialog(jobOptions as MultiRunJobOptions ?? new MultiRunJobOptions(), onAccept),
                    ProxyCheckJobViewModel pcVm => new ProxyCheckJobOptionsDialog(jobOptions as ProxyCheckJobOptions ?? new ProxyCheckJobOptions(), onAccept),
                    _ => throw new NotImplementedException($"Job type {jobVM.GetType().Name} not supported")
                };

                var mainWindow2 = MainWindow;
                if (mainWindow2 != null)
                    mainWindow2.SetMainContent(optionsPage);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void CloneJob(object sender, RoutedEventArgs e)
        {
            var jobVM = GetJobViewModel(sender);
            if (jobVM == null) return;

            // ✅ No Trial Limit - Cloning is allowed
            try
            {
                var entity = await jobRepo.GetAsync(jobVM.Id);

                // ✅ Fixed JSON Deserialization
                var jobOptionsWrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings);
                var oldOptions = jobOptionsWrapper?.Options;

                if (oldOptions == null)
                {
                    Alert.Error("Deserialization Error",
                        "Failed to clone job. The job data may be corrupted.");
                    return;
                }

                var newOptions = JobOptionsFactory.CloneExistant(oldOptions);

                Action<JobOptions> onAccept = async options =>
                {
                    var cloned = await vm.CloneJobAsync(entity.JobType, options);
                    var mainWindow = MainWindow;
                    if (mainWindow != null)
                        mainWindow.SetMainContent(new Jobs());
                };

                Page optionsPage = jobVM switch
                {
                    MultiRunJobViewModel => new MultiRunJobOptionsDialog(newOptions as MultiRunJobOptions, onAccept),
                    ProxyCheckJobViewModel => new ProxyCheckJobOptionsDialog(newOptions as ProxyCheckJobOptions, onAccept),
                    _ => throw new NotImplementedException($"Job type {jobVM.GetType().Name} not supported")
                };

                var mainWindow2 = MainWindow;
                if (mainWindow2 != null)
                    mainWindow2.SetMainContent(optionsPage);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void RemoveJob(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.RemoveJobAsync(GetJobViewModel(sender));
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        public async void CreateJob(JobOptions options) => await vm.CreateJobAsync(options);

        private void ViewJob(object sender, MouseButtonEventArgs e)
        {
            if (sender is WrapPanel panel && panel.Tag is JobViewModel jobVM)
            {
                var mainWindow = MainWindow;
                if (mainWindow != null)
                    mainWindow.DisplayJob(jobVM);
            }
        }

        private JobViewModel GetJobViewModel(object sender)
        {
            return sender switch
            {
                Button btn => btn.Tag as JobViewModel,
                MenuItem mi => mi.Tag as JobViewModel,
                _ => null
            };
        }

        private void ShowActionsMenu(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var jobVM = button.Tag as JobViewModel;
            if (jobVM == null) return;

            var contextMenu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            var status = jobVM.Status.ToString();

            if (status != "Running")
            {
                contextMenu.Items.Add(CreateMenuItem("Start", "PlayCircle", StartJob, jobVM, "#48bb78"));
            }

            if (status == "Running")
            {
                contextMenu.Items.Add(CreateMenuItem("Pause", "PauseCircle", PauseJob, jobVM, "#ecc94b"));
                contextMenu.Items.Add(CreateMenuItem("Force Stop", "LightningBolt", ForceStopJob, jobVM, "#f56565"));
            }
            else if (status == "Paused")
            {
                contextMenu.Items.Add(CreateMenuItem("Resume", "PlayCircle", StartJob, jobVM, "#48bb78"));
                contextMenu.Items.Add(CreateMenuItem("Force Stop", "LightningBolt", ForceStopJob, jobVM, "#f56565"));
            }

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(CreateMenuItem("Edit", "Pencil", EditJob, jobVM, null));
            contextMenu.Items.Add(CreateMenuItem("Clone", "ContentCopy", CloneJob, jobVM, null));
            contextMenu.Items.Add(CreateMenuItem("Delete", "CloseCircle", RemoveJob, jobVM, "#f56565"));

            contextMenu.IsOpen = true;
        }

        private MenuItem CreateMenuItem(string header, string iconKind, RoutedEventHandler handler, JobViewModel tag, string colorHex)
        {
            var menuItem = new MenuItem { Header = header, Tag = tag };
            menuItem.Click += handler;

            var icon = new MahApps.Metro.IconPacks.PackIconMaterial
            {
                Kind = (MahApps.Metro.IconPacks.PackIconMaterialKind)Enum.Parse(
                    typeof(MahApps.Metro.IconPacks.PackIconMaterialKind), iconKind),
                Width = 14,
                Height = 14
            };

            if (!string.IsNullOrEmpty(colorHex))
            {
                icon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            }

            menuItem.Icon = icon;
            return menuItem;
        }
    }

    // ✅ Status to Brush Converter
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value?.ToString()?.ToLower() ?? "idle";

            return status switch
            {
                "idle" => new SolidColorBrush(Color.FromRgb(74, 85, 104)),      // #4a5568 - Gray
                "running" => new SolidColorBrush(Color.FromRgb(72, 187, 120)),   // #48bb78 - Green
                "paused" => new SolidColorBrush(Color.FromRgb(236, 201, 75)),    // #ecc94b - Yellow
                "finished" => new SolidColorBrush(Color.FromRgb(49, 130, 206)),  // #3182ce - Blue
                "error" => new SolidColorBrush(Color.FromRgb(245, 101, 101)),    // #f56565 - Red
                "stopping" => new SolidColorBrush(Color.FromRgb(237, 137, 54)),  // #ed8936 - Orange
                _ => new SolidColorBrush(Color.FromRgb(74, 85, 104))             // Default Gray
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ✅ Null to Visibility Converter
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // ✅ Job Options Wrapper Class (for JSON serialization)
    public class JobOptionsWrapper
    {
        [JsonProperty("options")]
        public JobOptions Options { get; set; }
    }
}
