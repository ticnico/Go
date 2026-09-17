using GoldenBullet.ViewModels;
using RuriLib.Models.Blocks.Settings;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Controls
{
    /// <summary>
    /// Interaction logic for BlockSettingViewer.xaml
    /// </summary>
    public partial class FloatSettingViewer : UserControl
    {
        private FloatSettingViewerViewModel vm;

        public BlockSetting Setting
        {
            get => vm?.Setting;
            set
            {
                if (value.FixedSetting is not FloatSetting)
                {
                    throw new Exception("Invalid setting type for this UC");
                }

                vm = new FloatSettingViewerViewModel(value);
                DataContext = vm;

                tabControl.SelectedIndex = vm.Mode switch
                {
                    SettingInputMode.Variable => 0,
                    SettingInputMode.Fixed => 1,
                    _ => throw new NotImplementedException()
                };

                buttonTabControl.SelectedIndex = vm.Mode switch
                {
                    SettingInputMode.Variable => 0,
                    SettingInputMode.Fixed => 1,
                    _ => throw new NotImplementedException()
                };
            }
        }

        public FloatSettingViewer()
        {
            InitializeComponent();
        }

        // Constant -> Variable
        private void VariableMode(object sender, RoutedEventArgs e)
        {
            vm.Mode = SettingInputMode.Variable;
            tabControl.SelectedIndex = 0;
            buttonTabControl.SelectedIndex = 0;
        }

        // Variable -> Constant
        private void ConstantMode(object sender, RoutedEventArgs e)
        {
            vm.Mode = SettingInputMode.Fixed;
            tabControl.SelectedIndex = 1;
            buttonTabControl.SelectedIndex = 1;
        }
    }

    public class FloatSettingViewerViewModel : ViewModelBase
    {
        public BlockSetting Setting { get; init; }

        public string Name => Setting.ReadableName;

        public string Description => Setting.Description;

        public IEnumerable<string> Suggestions => Utils.Suggestions.GetInputVariableSuggestions(Setting);

        public SettingInputMode Mode
        {
            get => Setting.InputMode;
            set
            {
                Setting.InputMode = value;
                OnPropertyChanged();
            }
        }

        public string VariableName
        {
            get => Setting.InputVariableName;
            set
            {
                Setting.InputVariableName = value;
                OnPropertyChanged();
            }
        }

        // FIX: Changed type from float to double to match FloatSetting.Value
        public double Value
        {
            get => (Setting.FixedSetting as FloatSetting).Value;
            set
            {
                var s = Setting.FixedSetting as FloatSetting;
                s.Value = value;
                OnPropertyChanged();
            }
        }

        public FloatSettingViewerViewModel(BlockSetting setting)
        {
            Setting = setting;
        }
    }
}
