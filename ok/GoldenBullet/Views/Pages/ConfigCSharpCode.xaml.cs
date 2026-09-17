using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Configs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigCSharpCode.xaml
    /// </summary>
    public partial class ConfigCSharpCode : Page
    {
        private readonly ConfigCSharpCodeViewModel vm;
        private readonly ConfigService configService;
        private readonly GoldenBulletSettingsService obSettingsService;
        private Config Config => configService.SelectedConfig;

        public ConfigCSharpCode()
        {
            vm = new ConfigCSharpCodeViewModel();
            DataContext = vm;

            InitializeComponent();
            configService = SP.GetService<ConfigService>();
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();

            HighlightSyntax(editor);
            HighlightSyntax(startupEditor);
            SearchPanel.Install(editor);
            SearchPanel.Install(startupEditor);
            // Intercept Ctrl+F and open the shared Find dialog hosted in MainDialog
            editor.PreviewKeyDown += Editor_PreviewKeyDown;
            startupEditor.PreviewKeyDown += Editor_PreviewKeyDown;
        }

        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                e.Handled = true;
                try
                {
                    var editor = sender as TextEditor;
                    var page = new GoldenBullet.Controls.FindDialog(editor);
                    var main = new MainDialog(page, "Find", 400, 160);
                    main.Owner = Window.GetWindow(this);
                    main.Show();
                }
                catch
                {
                    // ignore
                }
            }
        }

        public void UpdateViewModel()
        {
            try
            {
                // Transpile if not in CSharp mode
                if (Config != null && Config.Mode != ConfigMode.CSharp)
                {
                    Config.CSharpScript = Config.Mode == ConfigMode.Stack
                            ? Stack2CSharpTranspiler.Transpile(Config.Stack, Config.Settings)
                            : Loli2CSharpTranspiler.Transpile(Config.LoliCodeScript, Config.Settings);

                    Config.StartupCSharpScript = Loli2CSharpTranspiler.Transpile(
                        Config.StartupLoliCodeScript, Config.Settings);

                    editor.Text = Config.CSharpScript;
                    startupEditor.Text = Config.StartupCSharpScript;

                    if (configService.SelectedConfig.StartupCSharpScript is not null &&
                        configService.SelectedConfig.StartupCSharpScript.Length > 0)
                    {
                        startupEditorContainer.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                // On fail, prompt it to the user and go back to the configs page
                Alert.Exception(ex);
                SP.GetService<MainWindow>().NavigateTo(MainWindowPage.Configs);
            }
        }

        private void HighlightSyntax(TextEditor textEditor)
        {
            using var reader = XmlReader.Create("Highlighting/LoliCode.xshd");
            textEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            textEditor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.DodgerBlue);
            textEditor.TextArea.TextView.LinkTextUnderline = false;
        }

        private void ToggleUsings(object sender, RoutedEventArgs e) => usingsContainer.Visibility =
            usingsContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleStartup(object sender, RoutedEventArgs e) => startupEditorContainer.Visibility =
            startupEditorContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    public class ConfigCSharpCodeViewModel : ViewModelBase
    {
        private readonly ConfigService configService;
        private readonly GoldenBulletSettingsService obSettingsService;
        private Config Config => configService.SelectedConfig;

        public ConfigCSharpCodeViewModel()
        {
            configService = SP.GetService<ConfigService>();
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
        }

        public bool WordWrap => obSettingsService.Settings.CustomizationSettings.WordWrap;

        public string UsingsString
        {
            get => string.Join(Environment.NewLine, Config.Settings.ScriptSettings.CustomUsings);
            set
            {
                Config.Settings.ScriptSettings.CustomUsings = value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
                OnPropertyChanged();
            }
        }
    }
}
