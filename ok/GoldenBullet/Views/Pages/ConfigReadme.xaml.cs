using GoldenBullet.Extensions;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using MahApps.Metro.IconPacks;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Markdown editor with full-screen preview and compact bottom editor.
    /// </summary>
    public partial class ConfigReadme : Page
    {
        private readonly ConfigReadmeViewModel _vm;
        private bool _isUpdating;

        public ConfigReadme()
        {
            _vm = SP.GetService<ViewModelsService>().ConfigReadme;
            DataContext = _vm;

            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeEditor();
        }

        private void InitializeEditor()
        {
            LoadReadmeContent();
            UpdateCounts();
        }

        private void LoadReadmeContent()
        {
            _isUpdating = true;
            readmeRTB.Document.Blocks.Clear();

            if (!string.IsNullOrWhiteSpace(_vm.Readme))
            {
                readmeRTB.AppendText(_vm.Readme);
            }
            _isUpdating = false;
            UpdatePreviewStatus(true);
        }

        /// <summary>
        /// Public method to refresh the viewmodel and editor content.
        /// Called externally when navigating to this page or refreshing config.
        /// </summary>
        public void UpdateViewModel()
        {
            try
            {
                _vm.UpdateViewModel();
            }
            catch { }

            LoadReadmeContent();
        }

        public void Refresh()
        {
            UpdateViewModel();
        }

        private void ReadmeChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdating) return;

            var newText = readmeRTB.GetText();
            UpdateCounts();

            if (!string.IsNullOrWhiteSpace(newText))
            {
                _vm.Readme = newText;
                UpdatePreviewStatus(false);
            }
            else
            {
                _vm.Readme = string.Empty;
                UpdatePreviewStatus(true);
            }
        }

        private void UpdateCounts()
        {
            var text = readmeRTB.GetText();
            var charCount = text?.Length ?? 0;
            var wordCount = string.IsNullOrWhiteSpace(text)
                ? 0
                : Regex.Matches(text, @"\b\w+\b").Count;

            CharCountText.Text = $"{charCount:n0} chars";
            WordCountText.Text = $"{wordCount:n0} words";
        }

        private void UpdatePreviewStatus(bool isSynced)
        {
            if (isSynced)
            {
                PreviewStatusIcon.Kind = PackIconMaterialKind.CheckCircleOutline;
                PreviewStatusText.Text = "Synced";
                PreviewStatusIcon.Foreground = System.Windows.Media.Brushes.LightGreen;

                FooterStatusIcon.Kind = PackIconMaterialKind.CheckCircleOutline;
                FooterStatusText.Text = "All changes saved";
                FooterStatusIcon.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                PreviewStatusIcon.Kind = PackIconMaterialKind.CircleOutline;
                PreviewStatusText.Text = "Syncing...";
                PreviewStatusIcon.Foreground = System.Windows.Media.Brushes.Gold;

                FooterStatusIcon.Kind = PackIconMaterialKind.CircleOutline;
                FooterStatusText.Text = "Unsaved changes";
                FooterStatusIcon.Foreground = System.Windows.Media.Brushes.Gold;
            }
        }

        // === Formatting Buttons (Fixed) ===

        private void BoldBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFormatting("**", "**");
        }

        private void ItalicBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyFormatting("*", "*");
        }

        private void ApplyFormatting(string prefix, string suffix)
        {
            var selection = readmeRTB.Selection;
            if (selection.IsEmpty)
            {
                // No selection - insert markers and place cursor between
                var caret = readmeRTB.CaretPosition;
                new TextRange(caret, caret) { Text = prefix + suffix };
                var newCaret = caret.GetPositionAtOffset(prefix.Length);
                if (newCaret != null)
                    readmeRTB.CaretPosition = newCaret;
            }
            else
            {
                // Wrap selected text
                var text = selection.Text;
                selection.Text = prefix + text + suffix;
            }
            readmeRTB.Focus();
            ReadmeChanged(readmeRTB, null);
        }

        // === Code Snippet Buttons ===

        private void InsertCodeBlock_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("```\n\n```", "```", 3);

        private void InsertInlineCode_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("`code`", "code", 1);

        private void InsertImage_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("![alt text](image-url)", "alt text", 2);

        private void InsertLink_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("[text](url)", "text", 1);

        // === Header Buttons ===

        private void InsertH1_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("# Header\n\n", "# Header", 2);

        private void InsertH2_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("## Header\n\n", "## Header", 3);

        private void InsertH3_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("### Header\n\n", "### Header", 4);

        // === List Buttons ===

        private void InsertBulletList_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("- Item 1\n- Item 2\n- Item 3\n", "- Item 1", 2);

        private void InsertNumberedList_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("1. Item 1\n2. Item 2\n3. Item 3\n", "1. Item 1", 2);

        // === Other Buttons ===

        private void InsertQuote_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("> Quote text\n\n", "> Quote text", 2);

        private void InsertHorizontalRule_Click(object sender, RoutedEventArgs e)
            => InsertMarkdownSnippet("\n---\n\n", "---", 1);

        // === Helper Method ===

        private void InsertMarkdownSnippet(string snippet, string selectText, int offset = 0)
        {
            var caret = readmeRTB.CaretPosition;
            new TextRange(caret, caret) { Text = snippet };

            var start = caret.GetPositionAtOffset(offset);
            var end = caret.GetPositionAtOffset(offset + selectText.Length);
            if (start != null && end != null)
            {
                readmeRTB.Selection.Select(start, end);
            }
            readmeRTB.Focus();
        }

        private void RichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+B: Bold
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.B)
            {
                e.Handled = true;
                BoldBtn_Click(null, null);
                return;
            }

            // Ctrl+I: Italic
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.I)
            {
                e.Handled = true;
                ItalicBtn_Click(null, null);
                return;
            }

            // Tab: Insert 4-space indent
            if (e.Key == Key.Tab && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                var caret = readmeRTB.CaretPosition;
                new TextRange(caret, caret) { Text = "    " };
                readmeRTB.CaretPosition = caret.GetPositionAtOffset(4) ?? caret;
            }
        }
    }
}
