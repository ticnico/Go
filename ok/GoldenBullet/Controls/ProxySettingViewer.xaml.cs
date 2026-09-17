using GoldenBullet.ViewModels;
using RuriLib.Models.Blocks.Settings;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Controls
{
    public partial class ProxySettingViewer : UserControl
    {
        public static readonly DependencyProperty SettingProperty =
            DependencyProperty.Register(
                nameof(Setting),
                typeof(BlockSetting),
                typeof(ProxySettingViewer),
                new PropertyMetadata(null, OnSettingChanged));

        public BlockSetting Setting
        {
            get => (BlockSetting)GetValue(SettingProperty);
            set => SetValue(SettingProperty, value);
        }

        private static void OnSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (ProxySettingViewer)d;
            var newValue = (BlockSetting)e.NewValue;

            if (newValue == null) return;

            if (newValue.FixedSetting is not StringSetting)
            {
                throw new Exception("ProxySettingViewer requires a StringSetting");
            }

            control.DataContext = new ProxySettingViewerViewModel(newValue);
        }

        public ProxySettingViewer()
        {
            InitializeComponent();
        }
    }

    public class ProxySettingViewerViewModel : ViewModelBase
    {
        public BlockSetting Setting { get; init; }
        private StringSetting FixedSetting => Setting.FixedSetting as StringSetting;

        public string Name => Setting.ReadableName;
        public string Description => Setting.Description;

        public string Value
        {
            get
            {
                // If the value is null or empty, return "Default" so the UI shows it
                if (string.IsNullOrEmpty(FixedSetting.Value))
                    return "Default";

                return FixedSetting.Value;
            }
            set
            {
                if (FixedSetting.Value != value)
                {
                    FixedSetting.Value = value;
                    OnPropertyChanged();
                }
            }
        }

        public ProxySettingViewerViewModel(BlockSetting setting)
        {
            Setting = setting;

            // Ensure the underlying setting is initialized to "Default" if it's empty
            if (string.IsNullOrEmpty(FixedSetting.Value))
            {
                FixedSetting.Value = "Default";
            }
        }
    }
}
