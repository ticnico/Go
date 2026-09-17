using Core.Entities;
using GoldenBullet.DTOs;
using GoldenBullet.Extensions;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using GoldenBullet.Views.Dialogs;
using Microsoft.Win32;
using RuriLib.Models.Proxies;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media; // ✅ Required for VisualTreeHelper

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for Proxies.xaml
    /// </summary>
    public partial class Proxies : Page
    {
        private readonly ProxiesViewModel vm;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        // ✅ Scroll sync fields
        private ScrollViewer _headerScroll;
        private ScrollViewer _contentScroll;
        private bool _isSyncing;

        private IEnumerable<ProxyEntity> SelectedProxies => proxiesListView.SelectedItems.Cast<ProxyEntity>().ToList();

        public Proxies()
        {
            vm = SP.GetService<ViewModelsService>().Proxies;
            DataContext = vm;
            _ = vm.InitializeAsync();

            InitializeComponent();

            // ✅ Attach Loaded event for scroll sync initialization
            proxiesListView.Loaded += ProxiesListView_Loaded;
        }

        // ✅ Scroll sync initialization
        private void ProxiesListView_Loaded(object sender, RoutedEventArgs e)
        {
            var listView = sender as ListView;
            if (listView == null) return;

            _headerScroll = FindVisualChild<ScrollViewer>(listView, "HeaderScroll");
            _contentScroll = FindVisualChild<ScrollViewer>(listView, "ContentScroll");

            if (_contentScroll != null)
            {
                _contentScroll.ScrollChanged += ContentScroll_ScrollChanged;
            }
        }

        // ✅ Scroll sync handler with recursion guard
        private void ContentScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0 && _headerScroll != null && !_isSyncing)
            {
                _isSyncing = true;
                try
                {
                    _headerScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
                }
                finally
                {
                    _isSyncing = false;
                }
            }
        }

        // ✅ Helper: Find named element in visual tree (was missing!)
        private T FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T fe && fe.Name == name)
                    return fe;

                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }

        private void AddGroup(object sender, RoutedEventArgs e)
            => new MainDialog(new AddProxyGroupDialog(this), "Add proxy group").ShowDialog();

        private void EditGroup(object sender, RoutedEventArgs e)
        {
            if (!vm.GroupIsValid)
            {
                ShowInvalidGroupError();
                return;
            }
            new MainDialog(new AddProxyGroupDialog(this, vm.SelectedGroup), "Edit proxy group").ShowDialog();
        }

        private async void DeleteGroup(object sender, RoutedEventArgs e)
        {
            if (!vm.GroupIsValid)
            {
                ShowInvalidGroupError();
                return;
            }
            try { await vm.DeleteSelectedGroupAsync(); }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        private async void DeleteNotWorking(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.DeleteNotWorkingAsync();
                Alert.Success("Done", "Successfully deleted the not working proxies from the group");
            }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        private async void DeleteUntested(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.DeleteUntestedAsync();
                Alert.Success("Done", "Successfully deleted the untested proxies from the group");
            }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        private void Import(object sender, RoutedEventArgs e)
        {
            if (!vm.GroupIsValid) { ShowInvalidGroupError(); return; }
            new MainDialog(new ImportProxiesDialog(this), "Import proxies").ShowDialog();
        }

        public async void AddGroup(ProxyGroupEntity entity)
        {
            try { await vm.AddGroupAsync(entity); }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        public async void EditGroup(ProxyGroupEntity entity)
        {
            try { await vm.EditGroupAsync(entity); }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

        private void ExportSelected(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog { Filter = "Text File |*.txt", Title = "Export proxies" };
            sfd.ShowDialog();

            if (!string.IsNullOrWhiteSpace(sfd.FileName))
            {
                if (SelectedProxies.Any())
                    SelectedProxies.SaveToFile(sfd.FileName, p => p.ToString());
                else
                    Alert.Error("Uh-oh", "No proxies selected");
            }
        }

        // ✅ REMOVE OR COMMENT OUT THIS METHOD - not needed anymore
        // private void ListView_Loaded(object sender, RoutedEventArgs e) { ... }

        private void CopySelectedProxies(object sender, RoutedEventArgs e)
            => SelectedProxies.CopyToClipboard(p => $"{p.Host}:{p.Port}");

        private void CopySelectedProxiesFull(object sender, RoutedEventArgs e)
            => SelectedProxies.CopyToClipboard(p => p.ToString());

        public async void AddProxies(ProxiesForImportDto dto)
        {
            try { await vm.AddProxiesAsync(dto); }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        private async void DeleteSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.DeleteAsync(SelectedProxies);
                Alert.Success("Done", "Successfully deleted the selected proxies from the group");
            }
            catch (Exception ex) { Alert.Exception(ex); }
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol)?.Remove(listViewSortAdorner);
                proxiesListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;
            if (listViewSortCol == column && listViewSortAdorner?.Direction == newDir)
                newDir = ListSortDirection.Descending;

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol)?.Add(listViewSortAdorner);
            proxiesListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));

            // ✅ Refresh header layout after sort
            _headerScroll?.UpdateLayout();
        }

        private void ProxyListViewDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files.Where(f => f.EndsWith(".txt")))
                {
                    var lines = File.ReadAllLines(file);
                    var dto = new ProxiesForImportDto { Lines = lines };

                    if (file.Contains("socks4a", StringComparison.OrdinalIgnoreCase))
                        dto.DefaultType = ProxyType.Socks4a;
                    else if (file.Contains("socks4", StringComparison.OrdinalIgnoreCase))
                        dto.DefaultType = ProxyType.Socks4;
                    else if (file.Contains("socks5", StringComparison.OrdinalIgnoreCase))
                        dto.DefaultType = ProxyType.Socks5;
                    else
                        dto.DefaultType = ProxyType.Http;

                    // ✅ FIX: Actually trigger the import!
                    AddProxies(dto);
                }
            }
        }

        private void ItemRightClick(object sender, MouseButtonEventArgs e) { }

        private void ShowInvalidGroupError()
            => Alert.Error("Invalid group", "Please select or create a valid group first!");
    }
}
