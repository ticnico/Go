using Core.Models.Settings;
using Core.Services;
using GoldenBullet.DTOs;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Microsoft.Win32;
using RuriLib.Models.Configs;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace GoldenBullet.Views.Pages
{
    public partial class Configs : Page
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly ConfigService configService;
        private readonly ConfigsViewModel vm;
        private readonly VolatileSettingsService volatileSettings;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;
        private string ListViewSortBy
        {
            get => volatileSettings.ListViewSorting["configs"].By;
            set => volatileSettings.ListViewSorting["configs"].By = value;
        }
        private ListSortDirection ListViewSortDir
        {
            get => volatileSettings.ListViewSorting["configs"].Direction;
            set => volatileSettings.ListViewSorting["configs"].Direction = value;
        }

        public Configs()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            configService = SP.GetService<ConfigService>();
            volatileSettings = SP.GetService<VolatileSettingsService>();
            vm = SP.GetService<ViewModelsService>().Configs;
            DataContext = vm;
            InitializeComponent();

            // ✅ Initialize Category Filter
            categoryFilterCombobox.Items.Add("All");
            var categories = configService.Configs
                .Select(c => c.Metadata.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct();

            foreach (var category in categories)
            {
                categoryFilterCombobox.Items.Add(category);
            }
            categoryFilterCombobox.SelectedIndex = 0;

            // ✅ Subscribe to ViewModel changes to re-apply filter if collection is replaced during search
            vm.PropertyChanged += Vm_PropertyChanged;
        }

        public void UpdateViewModel()
        {
            vm.SelectedConfig?.UpdateViewModel();
            if (!string.IsNullOrEmpty(ListViewSortBy))
            {
                configsListView.Items.SortDescriptions.Add(new SortDescription(ListViewSortBy, ListViewSortDir));
            }

            // ✅ Refresh Category Filter
            var currentSelection = categoryFilterCombobox.SelectedItem as string;
            categoryFilterCombobox.Items.Clear();
            categoryFilterCombobox.Items.Add("All");

            var categories = configService.Configs
                .Select(c => c.Metadata.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct();

            foreach (var category in categories)
            {
                categoryFilterCombobox.Items.Add(category);
            }

            // Restore previous selection or default to "All"
            if (currentSelection != null && categoryFilterCombobox.Items.Contains(currentSelection))
            {
                categoryFilterCombobox.SelectedItem = currentSelection;
            }
            else
            {
                categoryFilterCombobox.SelectedIndex = 0;
            }

            // Re-apply filter after refreshing
            ApplyCategoryFilter(categoryFilterCombobox.SelectedItem as string);
            ApplyFilters();
        }

        // ✅ NEW: Trigger filter on every keystroke
        private void FilterTextbox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        // ✅ UPDATED: Combined Category + Search Filter
        private void ApplyFilters()
        {
            if (configsGridView.ItemsSource == null) return;

            var view = CollectionViewSource.GetDefaultView(configsGridView.ItemsSource);
            if (view != null)
            {
                string selectedCategory = categoryFilterCombobox.SelectedItem as string;
                string searchText = filterTextbox.Text?.Trim().ToLowerInvariant() ?? string.Empty;

                view.Filter = item =>
                {
                    if (item is ConfigViewModel config)
                    {
                        // 1. Category Filter
                        bool categoryMatch = (selectedCategory == "All" || string.IsNullOrWhiteSpace(selectedCategory)) ||
                                             config.Category == selectedCategory;

                        if (!categoryMatch) return false;

                        // 2. Search Filter (Searches Name, Author, and Category)
                        if (!string.IsNullOrWhiteSpace(searchText))
                        {
                            bool nameMatch = config.Name?.ToLowerInvariant().Contains(searchText) == true;
                            bool authorMatch = config.Author?.ToLowerInvariant().Contains(searchText) == true;
                            bool categorySearchMatch = config.Category?.ToLowerInvariant().Contains(searchText) == true;

                            // If search text is present, it MUST match at least one of these fields
                            return nameMatch || authorMatch || categorySearchMatch;
                        }

                        // If no search text, just pass the category filter
                        return true;
                    }
                    return false;
                };

                view.Refresh(); // Apply the combined filter to the UI
            }
        }

        // ✅ Re-apply filter if the ViewModel replaces the collection (e.g. during search)
        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(vm.ConfigsCollection) || e.PropertyName == nameof(vm.SearchString))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ApplyFilters(); // Changed from ApplyCategoryFilter
                }, DispatcherPriority.Background);
            }
        }

        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters(); // Changed from ApplyCategoryFilter
        }

        private void ApplyCategoryFilter(string selectedCategory)
        {
            if (configsGridView.ItemsSource == null) return;

            // Get the default view of the collection to apply WPF filtering
            var view = CollectionViewSource.GetDefaultView(configsGridView.ItemsSource);
            if (view != null)
            {
                if (selectedCategory == "All" || string.IsNullOrWhiteSpace(selectedCategory))
                {
                    view.Filter = null; // Remove filter
                }
                else
                {
                    view.Filter = item =>
                    {
                        if (item is ConfigViewModel config)
                        {
                            return config.Category == selectedCategory;
                        }
                        return false;
                    };
                }
                view.Refresh(); // Apply the filter to both Grid and Table views
            }
        }

        private void SwitchToTableView(object sender, RoutedEventArgs e)
        {
            vm.CurrentViewMode = ViewMode.Table;
        }

        private void SwitchToGridView(object sender, RoutedEventArgs e)
        {
            vm.CurrentViewMode = ViewMode.Grid;
        }

        private void Search(object sender, RoutedEventArgs e) => vm.SearchString = filterTextbox.Text;

        private void UpdateSearch(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                vm.SearchString = filterTextbox.Text;
            }
        }

        // ✅ GRID VIEW: Selection Changed (MATCHES TABLE VIEW - ItemHovered pattern)
        private void GridItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var items = e.AddedItems as IList<object>;
            if (items.Count == 1)
            {
                vm.HoveredConfig = items[0] as ConfigViewModel;
            }
        }

        // ✅ GRID VIEW: Mouse Left Button Down (Border doesn't support MouseDoubleClick)
        // Checks ClickCount == 2 to match Table View's MouseDoubleClick behavior
        private void GridItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                EditConfig();
                e.Handled = true;
            }
        }

        // ✅ TABLE VIEW: Selection Changed (Original ItemHovered pattern)
        private void ItemHovered(object sender, SelectionChangedEventArgs e)
        {
            var items = e.AddedItems as IList<object>;
            if (items.Count == 1)
            {
                vm.HoveredConfig = items[0] as ConfigViewModel;
            }
        }

        // ✅ TABLE VIEW: Mouse Double Click (Original)
        private void ListItemDoubleClick(object sender, MouseButtonEventArgs e) => EditConfig();

        private ConfigViewModel HoveredItem =>
            vm.CurrentViewMode == ViewMode.Grid
            ? configsGridView.SelectedItem as ConfigViewModel
            : configsListView.SelectedItem as ConfigViewModel;

        private void Create(object sender, RoutedEventArgs e)
        {
            new MainDialog(new CreateConfigDialog(this), "Create config").ShowDialog();
        }

        private async void Import(object sender, RoutedEventArgs e)
        {
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    return;
                }
            }
            var dialog = new OpenFileDialog
            {
                Title = "Import Configs",
                // Include .svb (legacy) in the import filter
                Filter = "Config Files|*.opk;*.tic;*.loli;*.svb;*.anom|All Files|*.*",
                Multiselect = true
            };
            if (dialog.ShowDialog() == true)
            {
                int successCount = 0;
                int failCount = 0;
                var errors = new List<string>();
                try
                {
                    foreach (var filePath in dialog.FileNames)
                    {
                        try
                        {
                            await configService.ImportAsync(filePath);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            errors.Add($"{Path.GetFileName(filePath)}: {ex.Message}");
                        }
                    }
                    await vm.RescanAsync();
                    if (failCount == 0 && successCount > 0)
                    {
                        Alert.Success("Import Complete", $"Successfully imported {successCount} config(s)!");
                    }
                    else if (successCount > 0 && failCount > 0)
                    {
                        var errorPreview = string.Join("\n• ", errors.Take(3));
                        var moreText = errors.Count > 3 ? $"\n• ...and {errors.Count - 3} more" : "";
                        Alert.Warning("Import Partially Complete", $"✅ Imported: {successCount}\n❌ Failed: {failCount}\nErrors:\n• {errorPreview}{moreText}");
                    }
                    else if (failCount > 0)
                    {
                        var errorPreview = string.Join("\n• ", errors.Take(5));
                        Alert.Error("Import Failed", $"All {failCount} import(s) failed.\nErrors:\n• {errorPreview}");
                    }
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        public async void CreateConfig(ConfigForCreationDto dto) => await vm.CreateAsync(dto);

        private void Edit(object sender, RoutedEventArgs e) => EditConfig();

        private async void Save(object sender, RoutedEventArgs e)
        {
            if (vm.SelectedConfig is null)
            {
                ShowNoConfigSelectedError();
                return;
            }
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    return;
                }
            }
            try
            {
                await vm.Save(vm.SelectedConfig);
                Alert.Success("Success", $"{vm.SelectedConfig.Config.Metadata.Name} was saved successfully!");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void Delete(object sender, RoutedEventArgs e)
        {
            var selected = vm.CurrentViewMode == ViewMode.Grid
                ? configsGridView.SelectedItems.Cast<ConfigViewModel>().ToList()
                : configsListView.SelectedItems.Cast<ConfigViewModel>().ToList();
            if (!selected.Any())
            {
                ShowNoConfigSelectedError();
                return;
            }
            var configNames = string.Join(", ", selected.Take(5).Select(c => c.Name));
            var moreText = selected.Count > 5 ? $" and {selected.Count - 5} more" : "";
            if (Alert.Choice("Are you sure?", $"Do you really want to delete {selected.Count} config(s)?\n{configNames}{moreText}"))
            {
                foreach (var config in selected.ToList())
                    vm.Delete(config);
            }
        }

        private async void Rescan(object sender, RoutedEventArgs e) => await vm.RescanAsync();

        private void OpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start("explorer.exe", Path.Combine(Directory.GetCurrentDirectory(), "UserData\\Configs"));
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private void ShowNoConfigSelectedError() => Alert.Error("No config selected", "Please select a config first!");

        private void NavigateToConfigSection()
        {
            var mode = vm.SelectedConfig.Config.Mode;
            var page = obSettingsService.Settings.GeneralSettings.ConfigSectionOnLoad switch
            {
                ConfigSection.Metadata => MainWindowPage.ConfigMetadata,
                ConfigSection.Readme => MainWindowPage.ConfigReadme,
                ConfigSection.Stacker => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigStacker,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.LoliCode => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigLoliCode,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.Settings => MainWindowPage.ConfigSettings,
                ConfigSection.CSharpCode => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack or ConfigMode.CSharp => MainWindowPage.ConfigLoliCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                ConfigSection.LoliScript => mode switch
                {
                    ConfigMode.LoliCode or ConfigMode.Stack => MainWindowPage.ConfigLoliCode,
                    ConfigMode.CSharp => MainWindowPage.ConfigCSharpCode,
                    ConfigMode.Legacy => MainWindowPage.ConfigLoliScript,
                    _ => MainWindowPage.ConfigMetadata
                },
                _ => throw new NotImplementedException(),
            };
            SP.GetService<MainWindow>().NavigateTo(page);
        }

        // ✅ UNIFIED: EditConfig (uses HoveredItem for both modes now)
        private async void EditConfig()
        {
            var selected = HoveredItem;
            if (selected is null)
            {
                ShowNoConfigSelectedError();
                return;
            }
            if (!FLS.IsActivated())
            {
                await ShowRequiredDialogAsync();
                if (!FLS.IsActivated())
                {
                    return;
                }
            }
            if (selected.Config.IsRemote)
            {
                Alert.Error("Remote", "You cannot edit remote configs!");
                return;
            }
            if (obSettingsService.Settings.GeneralSettings.WarnConfigNotSaved
                && configService.SelectedConfig != null
                && configService.SelectedConfig.HasUnsavedChanges()
                && !Alert.Choice("Config not saved", $"The currently selected config ({configService.SelectedConfig.Metadata.Name}) has unsaved changes," +
                $" are you sure you want to edit another config?"))
            {
                return;
            }
            vm.SelectedConfig = selected;
            SP.GetService<ViewModelsService>().Debugger.ClearLog();
            // Ensure main window shows configs arrow when we navigate to the editor
            var mainWindow = SP.GetService<MainWindow>();
            mainWindow?.ShowConfigsArrowForEditor();
            NavigateToConfigSection();
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            ListViewSortBy = column.Tag.ToString();
            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                configsListView.Items.SortDescriptions.Clear();
            }
            ListViewSortDir = ListSortDirection.Ascending;
            if (listViewSortCol == column && listViewSortAdorner.Direction == ListViewSortDir)
            {
                ListViewSortDir = ListSortDirection.Descending;
            }
            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, ListViewSortDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            configsListView.Items.SortDescriptions.Add(new SortDescription(ListViewSortBy, ListViewSortDir));
        }

        private async Task ShowRequiredDialogAsync()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var activationPage = new RequiredDialog();
                var dialog = new MainDialog(activationPage, "Activation Required", 500, 600);
                dialog.Owner = Window.GetWindow(this);
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowDialog();
            });
            if (FLS.IsActivated() && mainWindow != null)
            {
                await mainWindow.Dispatcher.InvokeAsync(() =>
                {
                    mainWindow.CheckLicense();
                    mainWindow.ForceRefresh();
                });
            }
        }
    }
}
