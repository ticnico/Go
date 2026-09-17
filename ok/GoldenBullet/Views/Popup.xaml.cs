using MahApps.Metro.Controls;
using RuriLib.Services;
using System.Diagnostics;
using System.Windows;

namespace GoldenBullet.Views
{
    public partial class Popup : MetroWindow
    {
        public Popup()
        {
            InitializeComponent();
            Loaded += Popup_Loaded;
        }

        private void Popup_Loaded(object sender, RoutedEventArgs e)
        {
            // Display hardware ID for license purchase reference
            TxtHardwareId.Text = Info.GetMachineID();
        }

        private void BtnBuy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open license purchase page in default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://t.me/walidbendar", // 🔁 Update with your actual URL
                    UseShellExecute = true
                });

                Debug.WriteLine("🛒 Buy button clicked - opened license page");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error opening buy page: {ex.Message}");
            }
        }

        private void BtnDemo_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("▶️ Demo button clicked - continuing in trial mode");
            this.DialogResult = true;
            this.Close();
        }
    }
}
