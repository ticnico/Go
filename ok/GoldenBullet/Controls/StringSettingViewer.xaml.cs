using System;
using System.Collections.Generic;
using GoldenBullet.ViewModels;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoldenBullet.Controls
{
    /// <summary>
    /// Interaction logic for StringSettingViewer.xaml
    /// </summary>
    public partial class StringSettingViewer : UserControl
    {
        // DependencyProperty for Setting
        public static readonly DependencyProperty SettingProperty =
            DependencyProperty.Register(
                nameof(Setting),
                typeof(BlockSetting),
                typeof(StringSettingViewer),
                new PropertyMetadata(null, OnSettingChanged));

        public BlockSetting Setting
        {
            get => (BlockSetting)GetValue(SettingProperty);
            set => SetValue(SettingProperty, value);
        }

        private static void OnSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (StringSettingViewer)d;
            var newValue = (BlockSetting)e.NewValue;

            if (newValue == null) return;

            if (newValue.FixedSetting is not StringSetting)
            {
                throw new Exception("Invalid setting type for this UC");
            }

            control.vm = new StringSettingViewerViewModel(newValue);
            control.DataContext = control.vm;

            control.tabControl.SelectedIndex = control.vm.Mode switch
            {
                SettingInputMode.Variable => 0,
                SettingInputMode.Fixed => 1,
                SettingInputMode.Interpolated => 2,
                _ => throw new NotImplementedException()
            };

            control.buttonTabControl.SelectedIndex = control.vm.Mode switch
            {
                SettingInputMode.Variable => 0,
                SettingInputMode.Fixed => 1,
                SettingInputMode.Interpolated => 2,
                _ => throw new NotImplementedException()
            };
        }

        private StringSettingViewerViewModel vm;

        public StringSettingViewer()
        {
            InitializeComponent();
        }

        // Interpolated -> Variable
        private void VariableMode(object sender, RoutedEventArgs e)
        {
            vm.Mode = SettingInputMode.Variable;
            vm.VariableName = vm.InterpValue;
            tabControl.SelectedIndex = 0;
            buttonTabControl.SelectedIndex = 0;
        }

        // Variable -> Constant
        private void ConstantMode(object sender, RoutedEventArgs e)
        {
            vm.Mode = SettingInputMode.Fixed;
            vm.Value = vm.VariableName;
            tabControl.SelectedIndex = 1;
            buttonTabControl.SelectedIndex = 1;
        }

        // Constant -> Interpolated
        private void InterpMode(object sender, RoutedEventArgs e)
        {
            vm.Mode = SettingInputMode.Interpolated;
            vm.InterpValue = vm.Value;
            tabControl.SelectedIndex = 2;
            buttonTabControl.SelectedIndex = 2;
        }

        private void SwitchToInterpolatedMode(object sender, MouseButtonEventArgs e)
        {
            vm.Mode = SettingInputMode.Interpolated;
            vm.InterpValue = vm.Value;
            tabControl.SelectedIndex = 2;
            buttonTabControl.SelectedIndex = 2;
        }
    }

    // Move the ViewModel outside and make it public
    public class StringSettingViewerViewModel : ViewModelBase
    {
        public BlockSetting Setting { get; init; }

        private StringSetting FixedSetting => Setting.FixedSetting as StringSetting;
        private InterpolatedStringSetting InterpolatedSetting => Setting.InterpolatedSetting as InterpolatedStringSetting;

        public string Name => Setting.ReadableName;

        public string Description => Setting.Description;

        public IEnumerable<string> Suggestions => Utils.Suggestions.GetInputVariableSuggestions(Setting);

        // FIX: Added null/empty check for Value to prevent NullReferenceException during binding
        public bool CanSwitchToInterpolatedMode => Mode == SettingInputMode.Fixed && !string.IsNullOrEmpty(Value) && Value.Contains('<') && Value.Contains('>');

        public bool MultiLine => FixedSetting.MultiLine;
        public VerticalAlignment VerticalAlignment => MultiLine ? VerticalAlignment.Top : VerticalAlignment.Center;
        public int Height => MultiLine ? 100 : 30;

        public SettingInputMode Mode
        {
            get => Setting.InputMode;
            set
            {
                Setting.InputMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSwitchToInterpolatedMode));
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

        // FIX: Added null-conditional operators to prevent NullReferenceException if InterpolatedSetting is null
        public string InterpValue
        {
            get => InterpolatedSetting?.Value ?? string.Empty;
            set
            {
                var s = InterpolatedSetting;
                if (s != null)
                {
                    s.Value = value;
                    OnPropertyChanged();
                }
            }
        }

        // FIX: Added null-conditional operators to prevent NullReferenceException if FixedSetting is null
        public string Value
        {
            get => FixedSetting?.Value ?? string.Empty;
            set
            {
                var s = FixedSetting;
                if (s != null)
                {
                    s.Value = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanSwitchToInterpolatedMode));
                }
            }
        }

        public StringSettingViewerViewModel(BlockSetting setting)
        {
            Setting = setting;
        }
    }
}
