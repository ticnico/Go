using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace GoldenBullet.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for SelectConfigDialog.xaml
    /// </summary>
    public partial class SelectConfigDialog : Page
    {
        private readonly object caller;
        private readonly SelectConfigDialogViewModel vm;
        private readonly VolatileSettingsService volatileSettings;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private void ConfigsListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                // Get the selected item from ListView
                if (configsListView.SelectedItem is ConfigViewModel selected)
                {
                    // Sync with ViewModel property used by ConfirmSelection()
                    vm.HoveredConfig = selected;
                    ConfirmSelection();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                // Optional: Close dialog on Escape
                ((MainDialog)Parent)?.Close();
                e.Handled = true;
            }
        }

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

        public SelectConfigDialog(object caller)
        {
            this.caller = caller;
            volatileSettings = SP.GetService<VolatileSettingsService>();

            vm = new SelectConfigDialogViewModel();
            DataContext = vm;

            InitializeComponent();
            configsListView.Focus();

            if (!string.IsNullOrEmpty(ListViewSortBy))
            {
                configsListView.Items.SortDescriptions.Add(new SortDescription(ListViewSortBy, ListViewSortDir));
            }
        }

        private void UpdateSearch(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                vm.SearchString = filterTextbox.Text;
            }
        }

        private void Search(object sender, RoutedEventArgs e) => vm.SearchString = filterTextbox.Text;

        private void ItemHovered(object sender, SelectionChangedEventArgs e)
        {
            var items = e.AddedItems as IList<object>;

            if (items.Count == 1)
            {
                vm.HoveredConfig = items[0] as ConfigViewModel;
            }
        }

        private void ListItemDoubleClick(object sender, MouseButtonEventArgs e) => ConfirmSelection();
        private void Accept(object sender, RoutedEventArgs e) => ConfirmSelection();

        private void ConfirmSelection()
        {
            if (vm.HoveredConfig is null)
            {
                ShowNoConfigSelectedError();
                return;
            }

            if (caller is MultiRunJobOptionsDialog page)
            {
                page.SelectConfig(vm.HoveredConfig);
            }

            ((MainDialog)Parent).Close();
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

        private void ShowNoConfigSelectedError() => Alert.Error("No config selected", "Please select a config first!");
    }

    public class SelectConfigDialogViewModel : ViewModelBase
    {
        private readonly ConfigsViewModel configsViewModel;
        private readonly ConfigService configService;

        private ObservableCollection<ConfigViewModel> configsCollection;
        public ObservableCollection<ConfigViewModel> ConfigsCollection
        {
            get => configsCollection;
            set
            {
                configsCollection = value;
                OnPropertyChanged();
            }
        }

        private int total;
        public int Total
        {
            get => total;
            set
            {
                total = value;
                OnPropertyChanged();
            }
        }

        private string searchString = string.Empty;
        public string SearchString
        {
            get => searchString;
            set
            {
                searchString = value;

                if (configsViewModel is not null)
                {
                    configsViewModel.SearchString = value;
                }

                OnPropertyChanged();
                var view = CollectionViewSource.GetDefaultView(ConfigsCollection);
                view.Refresh();
                UpdateTotal();
            }
        }

        private ConfigViewModel hoveredConfig;
        public ConfigViewModel HoveredConfig
        {
            get => hoveredConfig;
            set
            {
                hoveredConfig = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConfigHovered));
            }
        }

        public bool IsConfigHovered => HoveredConfig != null;

        public SelectConfigDialogViewModel()
        {
            configService = SP.GetService<ConfigService>();
            CreateCollection();

            configsViewModel = SP.GetService<ViewModelsService>().Configs;

            if (configsViewModel is not null)
            {
                SearchString = configsViewModel.SearchString;
            }
        }

        private void CreateCollection()
        {
            var viewModels = configService.Configs.Select(c => new ConfigViewModel(c));
            ConfigsCollection = new ObservableCollection<ConfigViewModel>(viewModels);
            Application.Current.Dispatcher.Invoke(HookFilters);
        }

        public void HookFilters()
        {
            var view = (CollectionView)CollectionViewSource.GetDefaultView(ConfigsCollection);
            view.Filter = ConfigsFilter;
            UpdateTotal();
        }

        private bool ConfigsFilter(object item)
        {
            if (item is not ConfigViewModel config)
                return false;

            return config.Config.Metadata.Name.Contains(SearchString, StringComparison.OrdinalIgnoreCase) ||
                   config.Author.Contains(SearchString, StringComparison.OrdinalIgnoreCase) ||
                   config.Category.Contains(SearchString, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateTotal()
        {
            var view = CollectionViewSource.GetDefaultView(ConfigsCollection);
            Total = view.Cast<object>().Count();
        }
    }
}
