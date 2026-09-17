using GoldenBullet.Helpers;
using GoldenBullet.Views.Dialogs;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for About.xaml
    /// </summary>
    public partial class About : Page
    {
        public About()
        {
            InitializeComponent();
        }

        private void OpenLicense(object sender, RoutedEventArgs e) => new MainDialog(new LicenseDialog(), "License", true).ShowDialog();

        private void OpenDonation(object sender, RoutedEventArgs e) => Url.Open("https://t.me/walidbendar");

        private void OpenRepository(object sender, RoutedEventArgs e) => Url.Open("https://github.com/ticnico/GoldenBullet/");

        private void Telegram(object sender, RoutedEventArgs e) => Url.Open("https://t.me/ticnicoo");

        private void OpenForum(object sender, RoutedEventArgs e) => Url.Open("https://discourse.GoldenBullet.dev/");

        private void OpenIssues(object sender, RoutedEventArgs e) => Url.Open("https://chat.whatsapp.com/DikUYduJXd1BUHCSoO7txK");
    }
}
