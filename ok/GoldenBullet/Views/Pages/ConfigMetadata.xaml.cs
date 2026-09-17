using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Microsoft.Win32;
using System;
using System.Linq; // Added for LINQ methods
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // Added for KeyEventArgs and Keyboard

namespace GoldenBullet.Views.Pages
{
    public partial class ConfigMetadata : Page
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly ConfigMetadataViewModel vm;

        public ConfigMetadata()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            vm = SP.GetService<ViewModelsService>().ConfigMetadata;
            DataContext = vm;
            InitializeComponent();

            // --- FIX: Populate categories like in CreateConfigDialog ---
            categoryCombobox.Items.Add("Default");

            var categories = SP.GetService<ConfigService>().Configs
                .Select(c => c.Metadata.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category) && category != "Default")
                .Distinct();

            foreach (var category in categories)
            {
                categoryCombobox.Items.Add(category);
            }
            // ---------------------------------------------------------
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

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
                Alert.Success("Success", $"{vm.Name} metadata was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void OpenIcon(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.ico;*.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1
            };

            if (ofd.ShowDialog() == true && !string.IsNullOrEmpty(ofd.FileName))
            {
                try
                {
                    vm.SetIconFromFile(ofd.FileName);
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        private async void DownloadIcon(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.SetIconFromUrlAsync(urlTextbox.Text);
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
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowDialog();
            });

            if (FLS.IsActivated() && mainWindow != null)
            {
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.CheckLicense();
                    mainWindow.ForceRefresh();
                });
            }
        }

        // --- FIX: Added missing event handler referenced in XAML ---
        private void TextboxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Clear focus to commit the editable ComboBox text binding
                FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), null);
                Keyboard.ClearFocus();
            }
        }
    }
}
