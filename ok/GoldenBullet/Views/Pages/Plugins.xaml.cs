using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for Plugins.xaml
    /// </summary>
    public partial class Plugins : Page
    {
        private readonly PluginsViewModel vm;

        public Plugins()
        {
            vm = SP.GetService<ViewModelsService>().Plugins;
            DataContext = vm;

            InitializeComponent();
        }

        private void AddPlugin(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Plugin Files(*.zip)|*.zip",
                FilterIndex = 1
            };

            ofd.ShowDialog();

            if (!string.IsNullOrWhiteSpace(ofd.FileName))
            {
                vm.Add(ofd.FileName);
            }
        }

        private void RemovePlugin(object sender, RoutedEventArgs e) => vm.Delete((PluginInfo)(sender as Button).Tag);
    }
}
