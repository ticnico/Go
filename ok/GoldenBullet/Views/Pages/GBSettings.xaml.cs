using Core.Models.Settings;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for GBSettings.xaml
    /// </summary>
    public partial class GBSettings : Page
    {
        private readonly GBSettingsViewModel vm;

        public GBSettings()
        {
            vm = SP.GetService<ViewModelsService>().GBSettings;
            DataContext = vm;

            InitializeComponent();

            configSectionOnLoadCombobox.ItemsSource = Enum.GetValues(typeof(ConfigSection)).Cast<ConfigSection>();
        }

        private async void Save(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.Save();
                Alert.Success("Success", $"Settings was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void Reset(object sender, RoutedEventArgs e)
        {
            try
            {
                vm.Reset();
                Alert.Success("Success", $"SSettings have been reset to default values!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ResetCustomization(object sender, RoutedEventArgs e) => vm.ResetCustomization();

        private void AddProxyCheckTarget(object sender, RoutedEventArgs e) => vm.AddProxyCheckTarget();
        private void RemoveProxyCheckTarget(object sender, RoutedEventArgs e)
            => vm.RemoveProxyCheckTarget((ProxyCheckTarget)(sender as Button).Tag);

        private void AddCustomSnippet(object sender, RoutedEventArgs e) => vm.AddCustomSnippet();
        private void RemoveCustomSnippet(object sender, RoutedEventArgs e)
            => vm.RemoveCustomSnippet((CustomSnippet)(sender as Button).Tag);

        private void AddRemoteConfigsEndpoint(object sender, RoutedEventArgs e) => vm.AddRemoteConfigsEndpoint();
        private void RemoveRemoteConfigsEndpoint(object sender, RoutedEventArgs e)
            => vm.RemoveRemoteConfigsEndpoint((RemoteConfigsEndpoint)(sender as Button).Tag);

        private void ChooseBackgroundImage(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1
            };

            ofd.ShowDialog();

            if (!string.IsNullOrEmpty(ofd.FileName))
            {
                try
                {
                    vm.SetBackgroundImage(ofd.FileName);
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }
    }
}
