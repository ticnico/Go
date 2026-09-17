using Core.Services;
using GoldenBullet.Licensing.Services;
using GoldenBullet.Services;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using RuriLib.Services;
using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using System.IO;

namespace GoldenBullet.Views
{
    public partial class ADialog : MetroWindow
    {
        public ADialog()
        {
            InitializeComponent();
            string hwid = Info.GetMachineID();
            TxtHardwareId.Text = hwid;
            Debug.WriteLine($"💻 Hardware ID: {hwid}");
        }

        private void BtnCopyHwId_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(Info.GetMachineID());
                BtnCopyHwId.Content = "Copied!";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Copy error: {ex.Message}");
            }
        }

        private void BtnLoadFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "GoldenBullet License (*.dat)|*.dat|All files (*.*)|*.*",
                Title = "Select your License File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    // Read the .dat file and put it in the text box
                    string fileContent = File.ReadAllText(openFileDialog.FileName);
                    TxtLicenseKey.Text = fileContent;

                    Debug.WriteLine($"✅ Loaded license file: {openFileDialog.FileName}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error reading file: {ex.Message}");
                    MessageBox.Show("Failed to read the license file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            string key = TxtLicenseKey.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(key))
            {
                await this.ShowMessageAsync("Error", "❌ Please enter a license key or offline activation code.");
                return;
            }

            // Detect if it's an offline code (JSON) or online key
            bool isOffline = key.Contains("\"data\"") && key.Contains("\"signature\"");

            var controller = await this.ShowProgressAsync(
                "Activating...",
                isOffline ? "Verifying offline license locally..." : "Connecting to license server...");
            controller.SetIndeterminate();

            var (success, message) = await Validate.ActivateWithKeyAsync(key);

            await controller.CloseAsync();

            if (success)
            {
                FLS.Refresh(); // Ensure FLS limits are updated immediately
                await this.ShowMessageAsync("Success!", "✅ License activated successfully!");
                DialogResult = true;
                Close();
            }
            else
            {
                await this.ShowMessageAsync("Activation Failed",
                    $"❌ {message}\n\n" +
                    $"Your Hardware ID:\n{Info.GetMachineID()}\n\n" +
                    $"Please contact support with this information.");
            }
        }

        private void BtnBuy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://t.me/walidbendar",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error: {ex.Message}");
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
