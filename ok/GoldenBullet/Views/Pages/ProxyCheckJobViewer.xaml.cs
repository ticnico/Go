using Core.Entities;  // Added this for JobEntity
using Core.Models.Jobs;
using Core.Repositories;
using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Newtonsoft.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for ProxyCheckJobViewer.xaml
    /// </summary>
    public partial class ProxyCheckJobViewer : Page
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly IJobRepository jobRepo;
        private readonly MainWindow mainWindow;
        private readonly ViewModelsService vmService;
        private ProxyCheckJobViewerViewModel vm;

        public ProxyCheckJobViewer()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            jobRepo = SP.GetService<IJobRepository>();
            mainWindow = SP.GetService<MainWindow>();
            vmService = SP.GetService<ViewModelsService>();
            InitializeComponent();
        }

        public void BindViewModel(ProxyCheckJobViewModel jobVM)
        {
            if (vm is not null)
            {
                vm.Dispose();

                try
                {
                    vm.NewMessage -= OnResultMessage;
                }
                catch
                {

                }
            }

            vm = new ProxyCheckJobViewerViewModel(jobVM);
            vm.NewMessage += OnResultMessage;
            DataContext = vm;
        }

        private async void Start(object sender, RoutedEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() => jobLog.Clear());
                jobLog.BufferSize = obSettingsService.Settings.GeneralSettings.LogBufferSize;
                await vm.Start();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Stop(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Stop();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Pause(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Pause();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Resume(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Resume();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Abort(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Abort();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void SkipWait(object sender, RoutedEventArgs e)
        {
            try
            {
                vm.SkipWait();
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ChangeBots(object sender, MouseButtonEventArgs e)
            => new MainDialog(new ChangeBotsDialog(this, vm.Job.Bots), "Change bots").ShowDialog();

        public async void ChangeBots(int newValue)
        {
            try
            {
                await vm.ChangeBotsAsync(newValue);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void OnResultMessage(object sender, string message, Color color)
            => Application.Current.Dispatcher.Invoke(() =>
            {
                if (obSettingsService.Settings.GeneralSettings.EnableJobLogging)
                {
                    jobLog.Append(message, color);
                }
            });

        private void GoBack(object sender, RoutedEventArgs e)
        {
            // Navigate back to jobs page
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                mainWindow.SetMainContent(new Jobs());
            }
        }

        private void ChangeOptions(object sender, RoutedEventArgs e)
        {
            _ = ChangeOptionsAsync();
        }

        private async Task ChangeOptionsAsync()
        {
            try
            {
                // Check if job exists
                if (vm?.Job == null)
                {
                    MessageBox.Show("Job is not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the job ID - ProxyCheckJobViewModel should have an Id property
                var jobId = vm.Job.Id;

                // If the job hasn't been saved to DB yet (Id is 0), work with current options directly
                if (jobId == 0)
                {
                    // For new jobs, we need to get options from the Job itself or create new ones
                    var currentOptions = CreateOptionsFromJob(vm.Job);

                    Action<JobOptions> onAccept = options =>
                    {
                        try
                        {
                            if (options is ProxyCheckJobOptions newOptions)
                            {
                                // Apply the new options to the job
                                ApplyOptionsToJob(vm.Job, newOptions);

                                // Refresh UI
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    DataContext = null;
                                    DataContext = vm;
                                });
                            }

                            MessageBox.Show("Job options updated successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        catch (Exception ex)
                        {
                            Alert.Exception(ex);
                        }
                    };

                    // Navigate to options page
                    var optionsPage = new ProxyCheckJobOptionsDialog(currentOptions, onAccept, null);

                    // Use NavigationService if available, otherwise use MainWindow
                    if (NavigationService != null)
                    {
                        NavigationService.Navigate(optionsPage);
                    }
                    else
                    {
                        mainWindow.SetMainContent(optionsPage);
                    }

                    return;
                }

                // Job exists in DB, fetch and update
                JobEntity entity = null;

                await Task.Run(async () =>
                {
                    entity = await jobRepo.GetAsync(jobId);
                });

                if (entity == null)
                {
                    MessageBox.Show($"Could not find job with ID {jobId} in repository", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Deserialize the job options
                var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
                JobOptionsWrapper wrapper = null;

                try
                {
                    wrapper = JsonConvert.DeserializeObject<JobOptionsWrapper>(entity.JobOptions, jsonSettings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to deserialize job options: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (wrapper?.Options is not ProxyCheckJobOptions proxyOptions)
                {
                    MessageBox.Show("Invalid job options type or null options", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create callback for when options are accepted
                Action<JobOptions> onAcceptDb = async options =>
                {
                    try
                    {
                        // Update the job with new options in DB
                        var updatedJobVM = await vmService.Jobs.EditJobAsync(entity, options);

                        // Apply the new options to the current job
                        if (options is ProxyCheckJobOptions newProxyOptions && vm?.Job != null)
                        {
                            ApplyOptionsToJob(vm.Job, newProxyOptions);

                            // Refresh UI
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                DataContext = null;
                                DataContext = vm;
                            });
                        }

                        MessageBox.Show("Job options updated successfully", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        Alert.Exception(ex);
                    }
                };

                // Navigate to options page
                var optionsPageDb = new ProxyCheckJobOptionsDialog(proxyOptions, onAcceptDb, null);

                // Use NavigationService if available, otherwise use MainWindow
                if (NavigationService != null)
                {
                    NavigationService.Navigate(optionsPageDb);
                }
                else
                {
                    mainWindow.SetMainContent(optionsPageDb);
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        /// <summary>
        /// Creates ProxyCheckJobOptions from the current job state
        /// </summary>
        private ProxyCheckJobOptions CreateOptionsFromJob(ProxyCheckJobViewModel job)
        {
            // Create new options with current job settings
            var options = JobOptionsFactory.CreateNew(JobType.ProxyCheck) as ProxyCheckJobOptions;

            if (options != null)
            {
                // Copy current settings from the job if available
                options.Bots = job.Bots;
            }

            return options ?? new ProxyCheckJobOptions();
        }

        /// <summary>
        /// Applies options to the job
        /// </summary>
        private void ApplyOptionsToJob(ProxyCheckJobViewModel job, ProxyCheckJobOptions options)
        {
            if (job == null || options == null) return;

            // Apply the options to the job using reflection if needed
            try
            {
                var botsProperty = job.GetType().GetProperty("Bots");
                if (botsProperty != null && botsProperty.CanWrite)
                {
                    botsProperty.SetValue(job, options.Bots);
                }
            }
            catch
            {
                // Ignore reflection errors
            }
        }
    }
}
