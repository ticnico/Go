using GoldenBullet.Models;
using GoldenBullet.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace GoldenBullet.ViewModels
{
    public class CookiesViewModel : INotifyPropertyChanged
    {
        private readonly CookieManagerService _cookieService;
        private CookieJarModel _selectedJar;
        private CookieModel _selectedCookie;
        private string _searchText;
        private string _statusMessage;

        public ObservableCollection<CookieJarModel> CookieJars => _cookieService.CookieJars;

        public CookieJarModel SelectedJar
        {
            get => _selectedJar;
            set
            {
                _selectedJar = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredCookies));
            }
        }

        public CookieModel SelectedCookie
        {
            get => _selectedCookie;
            set { _selectedCookie = value; OnPropertyChanged(); }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilteredCookies));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CookieModel> FilteredCookies
        {
            get
            {
                if (SelectedJar == null)
                    return new ObservableCollection<CookieModel>();

                var cookies = SelectedJar.Cookies;

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    var lowerSearch = SearchText.ToLower();
                    cookies = new ObservableCollection<CookieModel>(
                        cookies.Where(c =>
                            c.Name.ToLower().Contains(lowerSearch) ||
                            c.Domain.ToLower().Contains(lowerSearch) ||
                            c.Value.ToLower().Contains(lowerSearch)));
                }

                return cookies;
            }
        }

        // Commands
        public ICommand CreateNewJarCommand { get; }
        public ICommand DeleteJarCommand { get; }
        public ICommand ImportJarCommand { get; }
        public ICommand ExportJarCommand { get; }
        public ICommand SaveJarCommand { get; }
        public ICommand ImportFromBrowserCommand { get; }
        public ICommand AddCookieCommand { get; }
        public ICommand EditCookieCommand { get; }
        public ICommand DeleteCookieCommand { get; }
        public ICommand ClearCookiesCommand { get; }
        public ICommand ExportNetscapeCommand { get; }
        public ICommand CopyCookieValueCommand { get; }

        public CookiesViewModel()
        {
            _cookieService = CookieManagerService.Instance;

            CreateNewJarCommand = new RelayCommand(CreateNewJar);
            DeleteJarCommand = new RelayCommand(DeleteJar, () => SelectedJar != null);
            ImportJarCommand = new RelayCommand(ImportJar);
            ExportJarCommand = new RelayCommand(ExportJar, () => SelectedJar != null);
            SaveJarCommand = new RelayCommand(async () => await SaveJarAsync(), () => SelectedJar != null);
            ImportFromBrowserCommand = new RelayCommand(ImportFromBrowser);
            AddCookieCommand = new RelayCommand(AddCookie, () => SelectedJar != null);
            EditCookieCommand = new RelayCommand(EditCookie, () => SelectedCookie != null);
            DeleteCookieCommand = new RelayCommand(DeleteCookie, () => SelectedCookie != null);
            ClearCookiesCommand = new RelayCommand(ClearCookies, () => SelectedJar != null && SelectedJar.Cookies.Any());
            ExportNetscapeCommand = new RelayCommand(ExportNetscape, () => SelectedJar != null);
            CopyCookieValueCommand = new RelayCommand(CopyCookieValue, () => SelectedCookie != null);
        }

        private void CreateNewJar()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Create New Cookie Jar",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"CookieJar_{DateTime.Now:yyyyMMdd_HHmmss}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var jar = _cookieService.CreateNewJar(Path.GetFileNameWithoutExtension(dialog.FileName));
                jar.FilePath = dialog.FileName;
                SelectedJar = jar;
                StatusMessage = $"Created new jar: {jar.Name}";
            }
        }

        private void DeleteJar()
        {
            if (SelectedJar == null) return;

            var result = MessageBox.Show(
                $"Delete jar '{SelectedJar.Name}'?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _cookieService.DeleteJar(SelectedJar);
                SelectedJar = null;
                StatusMessage = "Jar deleted";
            }
        }

        private async void ImportJar()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Cookie Jar",
                Filter = "All supported files (*.json;*.txt)|*.json;*.txt|JSON files (*.json)|*.json|Netscape cookies (*.txt)|*.txt|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        await _cookieService.ImportJarFromFileAsync(file);
                        StatusMessage = $"Imported: {Path.GetFileName(file)}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to import {file}: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ExportJar()
        {
            if (SelectedJar == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Cookie Jar",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{SelectedJar.Name}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    SelectedJar.SaveToFileAsync(dialog.FileName).Wait();
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task SaveJarAsync()
        {
            if (SelectedJar == null) return;

            try
            {
                await _cookieService.SaveJarAsync(SelectedJar);
                StatusMessage = $"Saved: {SelectedJar.Name} ({SelectedJar.CookieCount} cookies)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportFromBrowser()
        {
            // Show browser selection dialog
            var browsers = new[] { "Chrome", "Firefox", "Edge" };
            var selected = MessageBox.Show("Import from Chrome? (Click No for Firefox, Cancel for Edge)", "Select Browser", MessageBoxButton.YesNoCancel);

            string browserName = selected switch
            {
                MessageBoxResult.Yes => "Chrome",
                MessageBoxResult.No => "Firefox",
                MessageBoxResult.Cancel => "Edge",
                _ => null
            };

            if (browserName != null)
            {
                try
                {
                    await _cookieService.ImportFromBrowserAsync(browserName);
                    StatusMessage = $"Imported cookies from {browserName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddCookie()
        {
            if (SelectedJar == null) return;

            var newCookie = new CookieModel
            {
                Name = "NewCookie",
                Value = "",
                Domain = "example.com",
                Path = "/",
                Expires = DateTime.Now.AddDays(7)
            };

            SelectedJar.AddCookie(newCookie);
            SelectedCookie = newCookie;
            OnPropertyChanged(nameof(FilteredCookies));
            StatusMessage = "Added new cookie";
        }

        private void EditCookie()
        {
            // The cookie is already bound to the UI for editing
            // This could open a detailed edit dialog if needed
            StatusMessage = "Editing cookie (changes are live)";
        }

        private void DeleteCookie()
        {
            if (SelectedCookie == null || SelectedJar == null) return;

            SelectedJar.RemoveCookie(SelectedCookie);
            SelectedCookie = null;
            OnPropertyChanged(nameof(FilteredCookies));
            StatusMessage = "Cookie deleted";
        }

        private void ClearCookies()
        {
            if (SelectedJar == null) return;

            var result = MessageBox.Show(
                $"Clear all {SelectedJar.CookieCount} cookies from '{SelectedJar.Name}'?",
                "Confirm Clear",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                SelectedJar.ClearCookies();
                SelectedCookie = null;
                OnPropertyChanged(nameof(FilteredCookies));
                StatusMessage = "All cookies cleared";
            }
        }

        private async void ExportNetscape()
        {
            if (SelectedJar == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export as Netscape Format",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"{SelectedJar.Name}_cookies.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _cookieService.ExportJarToNetscapeFormatAsync(SelectedJar, dialog.FileName);
                    StatusMessage = $"Exported Netscape format to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyCookieValue()
        {
            if (SelectedCookie == null) return;

            Clipboard.SetText(SelectedCookie.Value);
            StatusMessage = $"Copied value of '{SelectedCookie.Name}' to clipboard";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
