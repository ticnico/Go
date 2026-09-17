using Core.Repositories;
using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using RuriLib.Models.Configs;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for ConfigLoliCode.xaml
    /// </summary>
    public partial class ConfigLoliCode : Page
    {
        private readonly ConfigLoliCodeViewModel vm;
        private readonly ConfigService configService;
        private readonly IConfigRepository configRepo; // TODO: This should not be here
        private CompletionWindow completionWindow;

        public ConfigLoliCode()
        {
            vm = new ConfigLoliCodeViewModel();
            DataContext = vm;

            InitializeComponent();
            configService = SP.GetService<ConfigService>();
            configRepo = SP.GetService<IConfigRepository>();

            HighlightSyntax(editor);
            AddAutoCompletion(editor);
            SearchPanel.Install(editor);
            // Intercept Ctrl+F to show custom find dialog
            editor.PreviewKeyDown += Editor_PreviewKeyDown;

            HighlightSyntax(startupEditor);
            AddAutoCompletion(startupEditor);
            SearchPanel.Install(startupEditor);
            startupEditor.PreviewKeyDown += Editor_PreviewKeyDown;
        }

        public void UpdateViewModel()
        {
            try
            {
                // Try to change the mode to LoliCode and set the editor's text
                configService.SelectedConfig.ChangeMode(ConfigMode.LoliCode);
                editor.Text = configService.SelectedConfig.LoliCodeScript;
                startupEditor.Text = configService.SelectedConfig.StartupLoliCodeScript;
                vm.UpdateViewModel();

                if (configService.SelectedConfig.StartupLoliCodeScript is not null &&
                    configService.SelectedConfig.StartupLoliCodeScript.Length > 0)
                {
                    startupEditorContainer.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                // On fail, prompt it to the user and go back to the configs page
                Alert.Exception(ex);
                SP.GetService<MainWindow>().NavigateTo(MainWindowPage.Configs);
            }
        }

        /// <summary>
        /// Call this when changing page via the dropdown menu otherwise it
        /// will not trigger the LostFocus event on the editor.
        /// </summary>
        public void OnPageChanged()
        {
            configService.SelectedConfig.LoliCodeScript = editor.Text;
            configService.SelectedConfig.StartupLoliCodeScript = startupEditor.Text;
        }

        private void EditorLostFocus(object sender, RoutedEventArgs e)
        {
            configService.SelectedConfig.LoliCodeScript = editor.Text;
            configService.SelectedConfig.StartupLoliCodeScript = startupEditor.Text;
        }

        private void HighlightSyntax(TextEditor textEditor)
        {
            using var reader = XmlReader.Create("Highlighting/LoliCode.xshd");
            textEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            textEditor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.DodgerBlue);
            textEditor.TextArea.TextView.LinkTextUnderline = false;
        }

        private void AddAutoCompletion(TextEditor textEditor)
        {
            textEditor.TextArea.TextEntering += TextEntering;
            textEditor.TextArea.TextEntered += TextEntered;
        }

        private void TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]))
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    completionWindow.CompletionList.RequestInsertion(e);
                }
            }
            // Do not set e.Handled=true.
            // We still want to insert the character that was typed.
        }

        private void TextEntered(object sender, TextCompositionEventArgs e)
        {
            try
            {
                var textArea = sender as TextArea;
                var offset = textArea.Caret.Offset;
                var documentLine = textArea.Document.GetLineByOffset(offset);
                var line = textArea.Document.GetText(documentLine.Offset, documentLine.Length);

                // Do not complete if we are not typing at the start of the line without spaces
                if (!string.IsNullOrWhiteSpace(line) && !line.Contains(' '))
                {
                    var snippets = AutocompletionProvider.GetSnippets()
                            .Where(s => s.Id.StartsWith(line, StringComparison.OrdinalIgnoreCase));

                    // If there are no snippets, do not even open the completion window
                    if (!snippets.Any())
                    {
                        return;
                    }

                    // Open code completion:
                    completionWindow = new CompletionWindow(textArea)
                    {
                        Foreground = Brushes.Gainsboro,
                        Background = Helpers.Brush.FromHex("#222")
                    };

                    var data = completionWindow.CompletionList.CompletionData;

                    foreach (var snippet in snippets)
                    {
                        data.Add(new LoliCodeCompletionData(snippet.Id, snippet.Body, snippet.Description));
                    }

                    completionWindow.Show();
                    completionWindow.Closed += (_, _) => completionWindow = null;
                }
            }
            catch
            {

            }
        }

        private async void PageKeyDown(object sender, KeyEventArgs e)
        {
            // Save on CTRL+S
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                configService.SelectedConfig.LoliCodeScript = editor.Text;
                configService.SelectedConfig.StartupLoliCodeScript = startupEditor.Text;
                await configRepo.SaveAsync(configService.SelectedConfig);
                Alert.Success("Saved", $"{configService.SelectedConfig.Metadata.Name} was saved successfully!");
            }
        }

        private void ToggleUsings(object sender, RoutedEventArgs e) => usingsContainer.Visibility =
            usingsContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleStartup(object sender, RoutedEventArgs e) => startupEditorContainer.Visibility =
            startupEditorContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;

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
    }

    public class ConfigLoliCodeViewModel : ViewModelBase
    {
        private readonly ConfigService configService;
        private readonly GoldenBulletSettingsService obSettingsService;
        private Config Config => configService.SelectedConfig;

        public ConfigLoliCodeViewModel()
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

    public class LoliCodeCompletionData : ICompletionData
    {
        private readonly string text;
        private readonly string snippet;
        private readonly string description;

        public LoliCodeCompletionData(string text, string snippet, string description)
        {
            this.text = text;
            this.snippet = snippet;
            this.description = description;
        }

        public ImageSource Image => null;

        public string Text => text;

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content => text;

        public object Description => description;

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            try
            {
                var offset = textArea.Caret.Offset;
                var documentLine = textArea.Document.GetLineByOffset(offset);
                var line = textArea.Document.GetText(documentLine.Offset, documentLine.Length);

                textArea.Document.Remove(documentLine);
                textArea.Document.Insert(documentLine.Offset, snippet);
            }
            catch
            {

            }
        }
    }
}
