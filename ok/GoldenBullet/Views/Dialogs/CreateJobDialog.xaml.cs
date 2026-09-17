using Core.Models.Jobs;
using GoldenBullet.Views.Pages;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for CreateJobDialog.xaml
    /// </summary>
    public partial class CreateJobDialog : Page
    {
        private readonly object caller;

        public CreateJobDialog(object caller)
        {
            this.caller = caller;
            InitializeComponent();
        }

        private void CreateMultiRunJob(object sender, RoutedEventArgs e) => CreateJob(JobType.MultiRun);

        // ✅ FIXED: Use NavigateTo with MainWindowPage.ProxyChecker enum
        private void CreateProxyCheckJob(object sender, RoutedEventArgs e)
        {
            // Close the dialog first
            ((MainDialog)Parent)?.Close();

            // Navigate using the existing enum-based navigation
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(MainWindowPage.ProxyChecker);
        }

        private void CreateJob(JobType type)
        {
            // Close the dialog first
            ((MainDialog)Parent)?.Close();

            Action<JobOptions> onAccept = options =>
            {
                if (caller is Jobs page)
                {
                    page.CreateJob(options);
                }
            };

            // Create the options page with navigation callback
            Page optionsPage = type switch
            {
                JobType.MultiRun => new MultiRunJobOptionsDialog(null, onAccept, NavigateBack),
                _ => throw new ArgumentException("Invalid job type")
            };

            // Navigate to the options page in main content
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateToJobOptions(optionsPage);
        }

        private void NavigateBack()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            mainWindow?.NavigateTo(MainWindowPage.Jobs);
        }
    }
}
