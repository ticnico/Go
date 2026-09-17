using Core.Repositories;
using Core.Services; // ✅ Try this namespace
using GoldenBullet.Helpers;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using RuriLib.Models.Configs;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    public partial class ConfigEditor : Page
    {
        private readonly MainWindow mainWindow;
        private readonly ConfigEditorViewModel vm;

        // ✅ FIX #1: Fully qualified name for Debugger
        private readonly GoldenBullet.Views.Pages.Shared.Debugger debugger;
        private readonly ConfigStacker stackerPage;
        private readonly ConfigLoliCode loliCodePage;
        private readonly ConfigCSharpCode cSharpPage;
        private readonly ConfigLoliScript loliScriptPage;

        public ConfigEditor()
        {
            mainWindow = SP.GetService<MainWindow>();
            vm = new ConfigEditorViewModel();
            DataContext = vm;

            InitializeComponent();

            editorFrame.Navigated += (_, _) => UpdateButtonsVisibility();

            debugger = new();
            stackerPage = new();
            loliCodePage = new();
            cSharpPage = new();
            loliScriptPage = new();

            debuggerFrame.Content = debugger;
            // Notify main window that an editor session has started so the
            // configs arrow remains visible for the whole session.
            mainWindow?.ShowConfigsArrowForEditor();
        }

        public void NavigateTo(ConfigEditorSection section)
        {
            switch (section)
            {
                case ConfigEditorSection.Stacker:
                    stackerPage.UpdateViewModel();
                    editorFrame.Content = stackerPage;
                    break;

                case ConfigEditorSection.LoliCode:
                    loliCodePage.UpdateViewModel();
                    editorFrame.Content = loliCodePage;
                    break;

                case ConfigEditorSection.CSharp:
                    cSharpPage.UpdateViewModel();
                    editorFrame.Content = cSharpPage;
                    break;

                case ConfigEditorSection.LoliScript:
                    loliScriptPage.UpdateViewModel();
                    editorFrame.Content = loliScriptPage;
                    break;

                default:
                    break;
            }
        }

        private void UpdateButtonsVisibility()
        {
            if (vm.Config.Mode == ConfigMode.Stack || vm.Config.Mode == ConfigMode.LoliCode)
            {
                stackerButton.Visibility = editorFrame.Content != stackerPage
                    ? Visibility.Visible : Visibility.Collapsed;

                loliCodeButton.Visibility = editorFrame.Content != loliCodePage
                    ? Visibility.Visible : Visibility.Collapsed;

                cSharpButton.Visibility = editorFrame.Content != cSharpPage ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                stackerButton.Visibility = Visibility.Collapsed;
                loliCodeButton.Visibility = Visibility.Collapsed;
                cSharpButton.Visibility = Visibility.Collapsed;
            }
        }

        public void OnPageChanged()
        {
            if (editorFrame.Content == loliCodePage)
            {
                loliCodePage.OnPageChanged();
            }

            // If user navigated away from editor internally (e.g. to a section
            // that isn't an editor page) we still consider the session active
            // until they explicitly close it. To end the session call
            // mainWindow?.EndConfigEditorSession() from the UI where appropriate.
        }

        private void OpenStacker(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigStacker);
        private void OpenLoliCode(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigLoliCode);
        private void OpenCSharpCode(object sender, RoutedEventArgs e) => mainWindow.NavigateTo(MainWindowPage.ConfigCSharpCode);

        // ✅ UPDATED: Save button with activation check
        private async void Save(object sender, RoutedEventArgs e)
        {
            // ✅ FIX #2: Use fully qualified name OR check your actual namespace
            if (!GoldenBullet.Services.FLS.IsActivated())
            {
                Debug.WriteLine("⚠️ Activation required before saving config");

                await ShowRequiredDialogAsync();

                if (!GoldenBullet.Services.FLS.IsActivated())
                {
                    Debug.WriteLine("❌ User did not activate - save cancelled");
                    Alert.Error("Activation Required",
                        "Please activate your license to save configs.\n\n" +
                        "You can still edit configs in trial mode,\n" +
                        "but saving requires an activated license.");
                    return;
                }

                Debug.WriteLine("✅ Activation confirmed - proceeding with save");
            }

            try
            {
                await vm.Save();
                Alert.Success("Success", $"{vm.Config.Metadata.Name} was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        // ✅ Helper method to show activation dialog using MainDialog
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

            // ✅ Refresh MainWindow UI if activated
            if (GoldenBullet.Services.FLS.IsActivated() && mainWindow != null)
            {
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.CheckLicense();
                    mainWindow.ForceRefresh();
                });
            }
        }
    }

    public enum ConfigEditorSection
    {
        Stacker,
        LoliCode,
        CSharp,
        LoliScript
    }

    public class ConfigEditorViewModel : ViewModelBase
    {
        private readonly IConfigRepository configRepo;
        private readonly ConfigService configService;
        public Config Config => configService.SelectedConfig;

        public ConfigEditorViewModel()
        {
            configRepo = SP.GetService<IConfigRepository>();
            configService = SP.GetService<ConfigService>();
        }

        public Task Save() => configRepo.SaveAsync(Config);
    }
}
