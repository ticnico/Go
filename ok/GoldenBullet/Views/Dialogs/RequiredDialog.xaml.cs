using MahApps.Metro.Controls;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for RequiredDialog.xaml
    /// </summary>
    public partial class RequiredDialog : Page
    {
        public RequiredDialog()
        {
            InitializeComponent();
        }

        private async void ActivateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("🔓 RequiredDialog: Activate button clicked");

                // 1. Get reference to MainWindow BEFORE closing this window
                var mainWindow = Application.Current.MainWindow as MainWindow;

                // 2. Get reference to current window (to close it)
                var currentWindow = Window.GetWindow(this) as MetroWindow;

                // 3. Close this warning dialog FIRST
                currentWindow?.Close();

                Debug.WriteLine("✅ Warning dialog closed");

                // 4. Now show the Activation Dialog
                var ADialog = new GoldenBullet.Views.ADialog();
                ADialog.Owner = mainWindow; // Set owner to MainWindow
                ADialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                // 5. Wait for activation result
                var result = ADialog.ShowDialog();

                Debug.WriteLine($"📋 ADialog result: {result}");

                // 6. If activation was successful
                if (result == true)
                {
                    Debug.WriteLine("✅ Activation successful!");

                    // 7. Refresh MainWindow UI on Dispatcher thread
                    if (mainWindow != null)
                    {
                        await mainWindow.Dispatcher.InvokeAsync(() =>
                        {
                            Debug.WriteLine("🔄 Refreshing MainWindow UI...");

                            // Call the existing methods from your MainWindow
                            mainWindow.CheckLicense();
                            mainWindow.ForceRefresh();

                            Debug.WriteLine("✅ MainWindow UI refreshed");
                        });
                    }

                    Debug.WriteLine("✅ Activation flow complete.");
                }
                else
                {
                    Debug.WriteLine("❌ Activation cancelled or failed.");

                    // Optional: Re-show the warning dialog if user cancelled
                    // var warningWindow = new MetroWindow { Content = new RequiredDialog() };
                    // warningWindow.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ ActivateButton_Click error: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the dialog without activation
            var window = Window.GetWindow(this) as MetroWindow;
            window?.Close();
        }
    }
}
