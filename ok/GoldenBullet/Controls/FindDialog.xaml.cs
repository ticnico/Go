using ICSharpCode.AvalonEdit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoldenBullet.Controls
{
    public partial class FindDialog : Page
    {
        private TextEditor _editor;
        private int _lastOffset = -1;

        // Parameterless constructor for XAML designer
        public FindDialog()
        {
            InitializeComponent();
        }

        public FindDialog(TextEditor editor) : this()
        {
            _editor = editor;
            SearchTextBox.KeyDown += SearchTextBox_KeyDown;
        }

        private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DoFindNext();
                e.Handled = true;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e) => DoFindNext();

        private void PrevButton_Click(object sender, RoutedEventArgs e) => DoFindPrevious();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // If hosted inside a MainDialog, close that window; otherwise do nothing
            var wnd = Window.GetWindow(this);
            wnd?.Close();
        }

        private void DoFindNext()
        {
            var text = SearchTextBox.Text;
            if (string.IsNullOrEmpty(text) || _editor == null) return;

            var comparison = CaseSensitiveCheck.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var start = Math.Max(0, _editor.TextArea.Caret.Offset);

            var idx = _editor.Text.IndexOf(text, start, comparison);
            if (idx < 0 && start > 0)
            {
                // wrap
                idx = _editor.Text.IndexOf(text, 0, comparison);
            }

            if (idx >= 0)
            {
                _editor.Select(idx, text.Length);
                _editor.ScrollTo(_editor.Document.GetLineByOffset(idx).LineNumber, 0);
                _editor.Focus();
                _lastOffset = idx;
            }
        }

        private void DoFindPrevious()
        {
            var text = SearchTextBox.Text;
            if (string.IsNullOrEmpty(text) || _editor == null) return;

            var comparison = CaseSensitiveCheck.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var start = Math.Max(0, _editor.TextArea.Caret.Offset - 1);

            var idx = _editor.Text.LastIndexOf(text, start, comparison);
            if (idx < 0 && start < _editor.Text.Length - 1)
            {
                // wrap to end
                idx = _editor.Text.LastIndexOf(text, _editor.Text.Length - 1, comparison);
            }

            if (idx >= 0)
            {
                _editor.Select(idx, text.Length);
                _editor.ScrollTo(_editor.Document.GetLineByOffset(idx).LineNumber, 0);
                _editor.Focus();
                _lastOffset = idx;
            }
        }
    }
}
