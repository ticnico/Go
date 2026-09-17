using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using RuriLib.Models.Configs.Settings;
using RuriLib.Models.Data.Resources.Options;
using RuriLib.Models.Data.Rules;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigSettings.xaml
    /// </summary>
    public partial class ConfigSettings : Page
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly ConfigSettingsViewModel vm;

        public ConfigSettings()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            vm = SP.GetService<ViewModelsService>().ConfigSettings;
            DataContext = vm;

            InitializeComponent();
            SetMultiLineTextBoxContents();
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

        private void BlockedUrlsChanged(object sender, TextChangedEventArgs e)
            => vm.BlockedUrls = blockedUrlsTextBox.Text.Split(Environment.NewLine).ToList();

        private void AddCustomInput(object sender, RoutedEventArgs e) => vm.AddCustomInput();
        private void RemoveCustomInput(object sender, RoutedEventArgs e)
            => vm.RemoveCustomInput((CustomInput)(sender as Button).Tag);

        private void AddLinesFromFileResource(object sender, RoutedEventArgs e) => vm.AddLinesFromFileResource();
        private void AddRandomLinesFromFileResource(object sender, RoutedEventArgs e) => vm.AddRandomLinesFromFileResource();
        private void RemoveResource(object sender, RoutedEventArgs e)
            => vm.RemoveResource((ConfigResourceOptions)(sender as Button).Tag);

        private void AddSimpleDataRule(object sender, RoutedEventArgs e) => vm.AddSimpleDataRule();
        private void AddRegexDataRule(object sender, RoutedEventArgs e) => vm.AddRegexDataRule();
        private void RemoveDataRule(object sender, RoutedEventArgs e)
            => vm.RemoveDataRule((DataRule)(sender as Button).Tag);

        private void SetMultiLineTextBoxContents()
        {
            blockedUrlsTextBox.Text = string.Join(Environment.NewLine, vm.BlockedUrls);
        }

        private void TestDataRules(object sender, RoutedEventArgs e)
            => new MainDialog(new TestDataRulesDialog(vm.TestDataForRules, vm.TestWordlistTypeForRules, vm.DataRulesCollection), "Test Results").ShowDialog();

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.GoBack();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (vm is null)
            {
                Alert.Error("Error", "No configuration loaded.");
                return;
            }

            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    return;
                }
            }

            try
            {
                await vm.SaveAsync();
                Alert.Success("Success", "Settings were saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async Task ShowRequiredDialogAsync()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var activationPage = new RequiredDialog();
                var dialog = new MainDialog(activationPage, "Activation Required", 500, 600);
                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            });
        }

    }
}
