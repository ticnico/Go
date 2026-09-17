using GoldenBullet.Extensions;
using GoldenBullet.Helpers;
using GoldenBullet.Views.Pages;
using Microsoft.Win32;
using RuriLib.Models.Proxies;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ImportProxiesDialog.xaml
    /// </summary>
    public partial class ImportProxiesDialog : Page
    {
        private readonly object caller;
        private string[] selectedFiles;

        public ImportProxiesDialog(object caller)
        {
            this.caller = caller;
            InitializeComponent();

            proxyTypeCombobox.ItemsSource = Enum.GetNames(typeof(ProxyType));
            proxyTypeCombobox.SelectedIndex = 0;
        }

        private void SearchInFolder(object sender, MouseButtonEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Proxy files | *.txt",
                FilterIndex = 1,
                Multiselect = true // Enable multiple file selection
            };

            // Only process if the user actually selected files and clicked OK
            if (ofd.ShowDialog() == true)
            {
                selectedFiles = ofd.FileNames;

                // Display the single path if 1 file is selected, otherwise show the count
                locationTextbox.Text = selectedFiles.Length == 1
                    ? selectedFiles[0]
                    : $"{selectedFiles.Length} files selected";
            }
        }

        private async void Accept(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (modeTabControl.SelectedIndex)
                {
                    // File
                    case 0:
                        // Fallback to manual textbox entry if no files were selected via dialog
                        var filesToRead = selectedFiles ?? new[] { locationTextbox.Text };
                        var sb = new System.Text.StringBuilder();
                        bool hasContent = false;

                        foreach (var file in filesToRead)
                        {
                            if (File.Exists(file))
                            {
                                sb.AppendLine(await File.ReadAllTextAsync(file).ConfigureAwait(false));
                                hasContent = true;
                            }
                        }

                        if (hasContent)
                        {
                            await ReturnLinesAsync(sb.ToString());
                        }
                        break;

                    // Paste
                    case 1:
                        await ReturnLinesAsync(proxiesBox.Text);
                        break;

                    // Remote
                    case 2:
                        await ImportFromUrlAsync(urlTextbox.Text);
                        break;
                }
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async Task ImportFromUrlAsync(string url)
        {
            using var client = new HttpClient();
            using var request = new HttpRequestMessage();

            request.RequestUri = new Uri(url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/83.0.4103.116 Safari/537.36");

            using var response = await client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            await ReturnLinesAsync(text);
        }

        private async Task ReturnLinesAsync(string text)
        {
            var lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            var dto = await Dispatcher.InvokeAsync(() => new DTOs.ProxiesForImportDto
            {
                Lines = lines,
                DefaultType = proxyTypeCombobox.SelectedItem.AsEnum<ProxyType>(),
                DefaultUsername = usernameTextbox.Text,
                DefaultPassword = passwordTextbox.Text
            });

            // Ensure the call that mutates UI-bound collections runs on the UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                if (caller is Proxies page)
                {
                    page.AddProxies(dto);
                }

                ((MainDialog)Parent).Close();
            });
        }

        private void SelectFileMode(object sender, MouseButtonEventArgs e)
        {
            fileMode.Foreground = Brush.Get("ForegroundMenuSelected");
            pasteMode.Foreground = Brush.Get("ForegroundMain");
            remoteMode.Foreground = Brush.Get("ForegroundMain");
            modeTabControl.SelectedIndex = 0;
        }

        private void SelectPasteMode(object sender, MouseButtonEventArgs e)
        {
            fileMode.Foreground = Brush.Get("ForegroundMain");
            pasteMode.Foreground = Brush.Get("ForegroundMenuSelected");
            remoteMode.Foreground = Brush.Get("ForegroundMain");
            modeTabControl.SelectedIndex = 1;
        }

        private void SelectRemoteMode(object sender, MouseButtonEventArgs e)
        {
            fileMode.Foreground = Brush.Get("ForegroundMain");
            pasteMode.Foreground = Brush.Get("ForegroundMain");
            remoteMode.Foreground = Brush.Get("ForegroundMenuSelected");
            modeTabControl.SelectedIndex = 2;
        }
    }
}
