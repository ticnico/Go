using Core.Services;
using GoldenBullet.DTOs;
using GoldenBullet.Helpers;
using GoldenBullet.Views.Pages;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GoldenBullet.Views.Dialogs
{
    public partial class CreateConfigDialog : Page
    {
        private readonly object caller;

        public CreateConfigDialog(object caller)
        {
            InitializeComponent();
            this.caller = caller;

            var settings = SP.GetService<GoldenBulletSettingsService>().Settings;
            authorTextbox.Text = settings.GeneralSettings.DefaultAuthor;
            nameTextbox.Focus();

            categoryCombobox.Items.Add("Default");

            var categories = SP.GetService<ConfigService>().Configs
                .Select(c => c.Metadata.Category)
                .Where(category => category != "Default")
                .Distinct();

            foreach (var category in categories)
            {
                categoryCombobox.Items.Add(category);
            }

            categoryCombobox.SelectedIndex = 0;
        }

        private void OpenIcon(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Images | *.ico;*.jpg;*.jpeg;*.png;*.bmp",
                FilterIndex = 1,
                Title = "Select Config Icon"
            };

            if (ofd.ShowDialog() == true && !string.IsNullOrEmpty(ofd.FileName))
            {
                iconPathTextBox.Text = ofd.FileName;
            }
        }

        private void CreateAndClose()
        {
            if (caller is Configs page)
            {
                var iconPath = iconPathTextBox.Text?.Trim();

                // Validate icon path if provided
                if (!string.IsNullOrWhiteSpace(iconPath) && !File.Exists(iconPath))
                {
                    Alert.Error("Invalid Icon", "The selected icon file does not exist.");
                    return;
                }

                var dto = new ConfigForCreationDto
                {
                    Name = nameTextbox.Text,
                    Category = categoryCombobox.Text,
                    Author = authorTextbox.Text,
                    IconPath = iconPath // Now safely passed
                };

                if (string.IsNullOrWhiteSpace(dto.Name))
                {
                    Alert.Error("Invalid name", "The name cannot be blank");
                    return;
                }

                page.CreateConfig(dto);
            }
            ((MainDialog)Parent).Close();
        }

        private void Accept(object sender, RoutedEventArgs e) => CreateAndClose();

        private void TextboxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CreateAndClose();
            }
        }
    }
}
