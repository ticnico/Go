using MahApps.Metro.Controls;
using RuriLib.Services;
using System.Windows;

namespace GoldenBullet.Views
{
    public partial class Status : MetroWindow
    {
        private readonly pipo _license;

        public Status(pipo license)
        {
            InitializeComponent();
            _license = license;
            DataContext = new LicenseStatusViewModel(license);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class LicenseStatusViewModel
    {
        public string CustomerName { get; set; }
        public string CustomerEmail { get; set; }
        public string LicenseId { get; set; }
        public string ExpiryDateText { get; set; }
        public string HardwareId { get; set; }

        public LicenseStatusViewModel(pipo license)
        {
            CustomerName = license.CustomerName ?? "N/A";
            CustomerEmail = license.CustomerEmail ?? "N/A";
            LicenseId = license.LicenseId ?? "N/A";
            ExpiryDateText = license.ExpiryDate == DateTime.MaxValue ? "Lifetime" : license.ExpiryDate.ToString("yyyy-MM-dd");
            HardwareId = license.HardwareId ?? "N/A";
        }
    }
}
