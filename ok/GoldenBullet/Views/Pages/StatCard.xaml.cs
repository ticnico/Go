using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    public partial class StatCard : UserControl
    {
        public static readonly DependencyProperty NumberProperty =
            DependencyProperty.Register("Number", typeof(string), typeof(StatCard), new PropertyMetadata("0"));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(StatCard), new PropertyMetadata("Label"));

        public string Number
        {
            get => (string)GetValue(NumberProperty);
            set => SetValue(NumberProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public StatCard()
        {
            InitializeComponent();
            this.Loaded += (s, e) => UpdateUI();
        }

        private void UpdateUI()
        {
            TxtNumber.Text = Number;
            TxtLabel.Text = Label;
        }
    }
}
