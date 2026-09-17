using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using RuriLib.Functions.Captchas;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages;

/// <summary>
/// Interaction logic for RLSettings.xaml
/// </summary>
public partial class RLSettings : Page
{
    private readonly RLSettingsViewModel vm;

    public RLSettings()
    {
        vm = SP.GetService<ViewModelsService>().RLSettings;
        vm.CaptchaServiceChanged += UpdateCaptchaTabControl;
        DataContext = vm;

        InitializeComponent();

        UpdateCaptchaTabControl(vm.CurrentCaptchaService);
        SetMultiLineTextBoxContents();
    }

    private void CustomUserAgentsChanged(object sender, TextChangedEventArgs e)
        => vm.UserAgents = customUserAgentsListTextBox.Text.Split(Environment.NewLine).ToList();

    private void GlobalBanKeysChanged(object sender, TextChangedEventArgs e)
        => vm.GlobalBanKeys = globalBanKeysTextBox.Text.Split(Environment.NewLine).ToList();

    private void GlobalRetryKeysChanged(object sender, TextChangedEventArgs e)
        => vm.GlobalRetryKeys = globalRetryKeysTextBox.Text.Split(Environment.NewLine).ToList();

    private async void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            await vm.Save();
            Alert.Success("Success", $"Settings was saved successfully!");
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }


    private void Reset(object sender, RoutedEventArgs e)
    {
        try
        {
            vm.Reset();
            SetMultiLineTextBoxContents();
            Alert.Success("Reset", "Settings have been reset to default values!");
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private async void CheckCaptchaBalance(object sender, RoutedEventArgs e)
    {
        try
        {
            var balance = await vm.CheckCaptchaBalance();
            Alert.Success("Success", $"Balance: {balance}");
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }

    private void UpdateCaptchaTabControl(CaptchaServiceType service)
    {
        var values = Enum.GetValues(typeof(CaptchaServiceType)).Cast<CaptchaServiceType>().ToList();
        var index = values.IndexOf(service);
        captchaServiceTabControl.SelectedIndex = index;
    }

    private void SetMultiLineTextBoxContents()
    {
        customUserAgentsListTextBox.Text = string.Join(Environment.NewLine, vm.UserAgents);
        globalBanKeysTextBox.Text = string.Join(Environment.NewLine, vm.GlobalBanKeys);
        globalRetryKeysTextBox.Text = string.Join(Environment.NewLine, vm.GlobalRetryKeys);
    }
}
