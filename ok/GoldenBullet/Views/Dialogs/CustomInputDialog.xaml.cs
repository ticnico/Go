using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for CustomInputDialog.xaml
    /// </summary>
    public partial class CustomInputDialog : Page
    {
        private readonly Action<string> onAnswer;

        public CustomInputDialog(string question, string defaultAnswer, Action<string> onAnswer)
        {
            this.onAnswer = onAnswer;
            InitializeComponent();

            this.question.Text = question;

            foreach (var option in defaultAnswer.Split(','))
            {
                answerComboBox.Items.Add(option);
            }

            // Do not select the first item by default so the editable textbox can receive input
            answerComboBox.SelectedIndex = -1;

            // Ensure the inner editable TextBox receives focus so the user can type immediately.
            // The editable TextBox is named PART_EditableTextBox in the ComboBox template.
            answerComboBox.Loaded += (s, e) =>
            {
                var tb = answerComboBox.Template.FindName("PART_EditableTextBox", answerComboBox) as TextBox;
                if (tb != null)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
                else
                {
                    // Fallback to focusing the ComboBox itself
                    answerComboBox.Focus();
                }
            };
        }

        private void Ok(object sender, RoutedEventArgs e)
        {
            onAnswer(answerComboBox.Text);
            ((MainDialog)Parent).Close();
        }
    }
}
