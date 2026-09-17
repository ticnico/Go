using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using System.Net.Http;
using System.Windows.Controls;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ShowChangelogDialog.xaml
    /// </summary>
    public partial class ShowChangelogDialog : Page
    {
        private ChangelogViewModel vm;

        public ShowChangelogDialog()
        {
            InitializeComponent();
            vm = new ChangelogViewModel();
            DataContext = vm;
        }

        public class ChangelogViewModel : ViewModelBase
        {
            private string text = "Loading...";
            public string Text
            {
                get => text;
                set
                {
                    text = value;
                    OnPropertyChanged();
                }
            }

            public ChangelogViewModel()
            {
                FetchChangelog();
            }

            private async void FetchChangelog()
            {
                var updateService = SP.GetService<UpdateService>();

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:84.0) Gecko/20100101 Firefox/84.0");

                try
                {
                    var response = await client.GetAsync($"https://raw.githubusercontent.com/GoldenBullet/GoldenBullet/master/Changelog/{updateService.CurrentVersion}.md");
                    Text = await response.Content.ReadAsStringAsync();
                }
                catch
                {
                    Text = "Could not retrieve the changelog";
                }
            }
        }
    }
}
