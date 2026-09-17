using Core.Services;
using GoldenBullet.Views.Pages;
using System.Windows;
using System.Windows.Controls;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ChangeBotsDialog.xaml
    /// </summary>
    public partial class ChangeBotsDialog : Page
    {
        private readonly object caller;

        public ChangeBotsDialog(object caller, int oldValue)
        {
            this.caller = caller;

            InitializeComponent();
            bots.Maximum = SP.GetService<JobFactoryService>().BotLimit;
            bots.Value = oldValue;
        }

        private void Accept(object sender, RoutedEventArgs e)
        {
            if (caller is MultiRunJobViewer mr)
            {
                mr.ChangeBots((int)bots.Value);
            }
            else if (caller is ProxyCheckJobViewer pc)
            {
                pc.ChangeBots((int)bots.Value);
            }

            ((MainDialog)Parent).Close();
        }
    }
}
