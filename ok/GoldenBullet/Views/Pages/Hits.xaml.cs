using Core.Entities;
using Core.Models.Data;
using Core.Models.Jobs;
using Core.Services;
using GoldenBullet.Extensions;
using GoldenBullet.Helpers;
using GoldenBullet.Services;
using GoldenBullet.ViewModels;
using Microsoft.Win32;
using Newtonsoft.Json;
using RuriLib.Extensions;
using RuriLib.Services;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GoldenBullet.Views.Pages
{
    /// <summary>
    /// Interaction logic for Hits.xaml
    /// </summary>
    public partial class Hits : Page
    {
        private readonly HitsViewModel vm;
        private readonly ConfigService configService;
        private readonly MainWindow window;
        private readonly RuriLibSettingsService rlSettingsService;
        private GridViewColumnHeader listViewSortCol;
        private SortAdorner listViewSortAdorner;

        private IEnumerable<HitEntity> SelectedHits => hitsListView.SelectedItems.Cast<HitEntity>().ToList();

        private readonly Func<HitEntity, string> captureMapping = new(hit => $"{hit.Data} | {hit.CapturedData}");
        private readonly Func<HitEntity, string> fullMapping = new(hit =>
            "Data = " + hit.Data +
            " | Type = " + hit.Type +
            " | Config = " + hit.ConfigName +
            " | Wordlist = " + hit.WordlistName +
            " | Proxy = " + hit.Proxy +
            " | Date = " + hit.Date.ToLongDateString() +
            " | CapturedData = " + hit.CapturedData);

        public Hits()
        {
            // 1. Initialize ViewModel FIRST
            vm = SP.GetService<ViewModelsService>().Hits;
            DataContext = vm;
            _ = vm.InitializeAsync();

            // 2. Initialize XAML components BEFORE accessing Resources
            InitializeComponent();

            // 3. NOW access services and resources (safe after InitializeComponent)
            window = SP.GetService<MainWindow>();
            configService = SP.GetService<ConfigService>();
            rlSettingsService = SP.GetService<RuriLibSettingsService>();
            var env = SP.GetService<RuriLibSettingsService>().Environment;

            // 4. Set up empty state monitoring
            vm.HitsCollection.CollectionChanged += (s, e) =>
            {
                UpdateEmptyState();
            };

            // 5. HACK: Hardcoded stuff - ContextMenu submenu population
            var menu = (ContextMenu)Resources["ItemContextMenu"];
            var copyMenu = (MenuItem)menu.Items[0];
            var saveMenu = (MenuItem)menu.Items[1];

            foreach (var f in env.ExportFormats)
            {
                var copyItem = new MenuItem();
                copyItem.Header = f.Format;
                copyItem.Click += new RoutedEventHandler(CopySelectedCustom);
                ((MenuItem)copyMenu.Items[4]).Items.Add(copyItem);

                var saveItem = new MenuItem();
                saveItem.Header = f.Format;
                saveItem.Click += new RoutedEventHandler(SaveSelectedCustom);
                ((MenuItem)saveMenu.Items[3]).Items.Add(saveItem);
            }

            // 6. Update empty state initially
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (emptyMessage != null)
            {
                emptyMessage.Visibility =
                    vm.HitsCollection != null && vm.HitsCollection.Any()
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        public void UpdateViewModel() => vm.UpdateViewModel();

        private async void DeleteSelected(object sender, RoutedEventArgs e)
        {
            try
            {
                await vm.DeleteAsync(SelectedHits);
                Alert.Success("Done", "Successfully deleted the selected hits from the DB");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void ExportAll(object sender, RoutedEventArgs e)
        {
            try
            {
                var allHits = vm.HitsCollection.ToList();

                if (!allHits.Any())
                {
                    System.Windows.MessageBox.Show("There are no hits to export.", "No Hits",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                // Let user choose the folder location (fully qualified to avoid ambiguity)
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select the folder where Core exports will be saved",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };

                // Try to set initial path to Documents\Core if it exists
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var defaultPath = System.IO.Path.Combine(documentsPath, "Core");
                if (System.IO.Directory.Exists(defaultPath))
                {
                    folderDialog.SelectedPath = defaultPath;
                }
                else
                {
                    folderDialog.SelectedPath = documentsPath;
                }

                var result = folderDialog.ShowDialog();

                if (result != System.Windows.Forms.DialogResult.OK)
                {
                    // User cancelled
                    return;
                }

                var exportFolder = System.IO.Path.Combine(folderDialog.SelectedPath, "Core");

                // Create Core subfolder if it doesn't exist
                System.IO.Directory.CreateDirectory(exportFolder);

                // Group hits by ConfigName and export each group
                var groupedHits = allHits.GroupBy(h => h.ConfigName)
                                         .Where(g => !string.IsNullOrWhiteSpace(g.Key));

                int exportedCount = 0;
                int fileCount = 0;

                foreach (var group in groupedHits)
                {
                    var configName = group.Key;

                    // Sanitize filename: remove invalid path characters
                    var safeConfigName = string.Join("_", configName.Split(System.IO.Path.GetInvalidFileNameChars()));
                    var fileName = $"{safeConfigName}.txt";
                    var filePath = System.IO.Path.Combine(exportFolder, fileName);

                    // Format: Data | CapturedData
                    var lines = group.Select(h => $"{h.Data} | {h.CapturedData}").ToList();

                    await System.IO.File.WriteAllLinesAsync(filePath, lines, System.Text.Encoding.UTF8);

                    exportedCount += lines.Count;
                    fileCount++;
                }

                // Export hits with no config name to "Uncategorized.txt"
                var uncategorized = allHits.Where(h => string.IsNullOrWhiteSpace(h.ConfigName)).ToList();
                if (uncategorized.Any())
                {
                    var uncategorizedPath = System.IO.Path.Combine(exportFolder, "Uncategorized.txt");
                    var lines = uncategorized.Select(h => $"{h.Data} | {h.CapturedData}").ToList();
                    await System.IO.File.WriteAllLinesAsync(uncategorizedPath, lines, System.Text.Encoding.UTF8);
                    exportedCount += lines.Count;
                    fileCount++;
                }

                Alert.Success("Export Complete",
                    $"Successfully exported {exportedCount} hits to {fileCount} file(s):\n{exportFolder}");
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private async void Purge(object sender, RoutedEventArgs e)
        {
            if (Alert.Choice("Are you REALLY sure?", "This will delete ALL your hits, not just the ones you filtered. Are you sure you want to do this?"))
            {
                try
                {
                    await vm.PurgeAsync();
                    Alert.Success("Done", "Successfully deleted all hits from the DB");
                }
                catch (Exception ex)
                {
                    Alert.Exception(ex);
                }
            }
        }

        private void UpdateSearch(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                vm.SearchString = filterTextbox.Text;
            }
        }

        private void Search(object sender, RoutedEventArgs e) => vm.SearchString = filterTextbox.Text;

        private async void DeleteDuplicates(object sender, RoutedEventArgs e)
        {
            var deleted = await vm.DeleteDuplicatesAsync();
            Alert.Success("Done", $"Successfully deleted {deleted} duplicate hits");
        }

        private void ColumnHeaderClicked(object sender, RoutedEventArgs e)
        {
            var column = sender as GridViewColumnHeader;
            var sortBy = column.Tag.ToString();

            if (listViewSortCol != null)
            {
                AdornerLayer.GetAdornerLayer(listViewSortCol).Remove(listViewSortAdorner);
                hitsListView.Items.SortDescriptions.Clear();
            }

            var newDir = ListSortDirection.Ascending;

            if (listViewSortCol == column && listViewSortAdorner.Direction == newDir)
            {
                newDir = ListSortDirection.Descending;
            }

            listViewSortCol = column;
            listViewSortAdorner = new SortAdorner(listViewSortCol, newDir);
            AdornerLayer.GetAdornerLayer(listViewSortCol).Add(listViewSortAdorner);
            hitsListView.Items.SortDescriptions.Add(new SortDescription(sortBy, newDir));
        }

        private void SelectAll(object sender, RoutedEventArgs e) => hitsListView.SelectAll();

        private async void SendToRecheck(object sender, RoutedEventArgs e)
        {
            if (!SelectedHits.Any())
            {
                return;
            }

            var firstHit = SelectedHits.First();

            var jobOptions = (MultiRunJobOptions)JobOptionsFactory.CreateNew(JobType.MultiRun);
            var wordlistType = rlSettingsService.Environment.RecognizeWordlistType(firstHit.Data);

            // Get the config
            var config = configService.Configs.FirstOrDefault(c => c.Metadata.Name == firstHit.ConfigName);

            // If we cannot find a config with that id anymore, don't set it
            if (config == null)
            {
                Alert.Warning("Config not found", $"Could not find the config these hits refer to ({firstHit.ConfigName})");
            }
            else
            {
                jobOptions.ConfigId = config.Id;
                jobOptions.Bots = config.Settings.GeneralSettings.SuggestedBots;
                wordlistType = config.Settings.DataSettings.AllowedWordlistTypes.First();
            }

            // Write the temporary file
            var tempFile = Path.GetTempFileName();
            await File.WriteAllLinesAsync(tempFile, SelectedHits.Select(h => h.Data)).ConfigureAwait(false);
            var dataPoolOptions = new FileDataPoolOptions
            {
                FileName = tempFile,
                WordlistType = wordlistType
            };
            jobOptions.DataPool = dataPoolOptions;

            // Create the job entity and add it to the database
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var jobOptionsWrapper = new JobOptionsWrapper { Options = jobOptions };

            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = JobType.MultiRun,
                JobOptions = JsonConvert.SerializeObject(jobOptionsWrapper, jsonSettings)
            };

            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                var jobs = SP.GetService<ViewModelsService>().Jobs;
                var jobVM = await jobs.CreateJobAsync(jobOptions);
                window.DisplayJob(jobVM);
            });
        }

        private void CopySelected(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(h => h.Data);

        private void CopySelectedProxies(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(h => h.Proxy);

        private void CopySelectedWithCapture(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(captureMapping);

        private void CopySelectedFull(object sender, RoutedEventArgs e)
            => SelectedHits.CopyToClipboard(fullMapping);

        private void CopySelectedCustom(object sender, RoutedEventArgs e)
        {
            var format = (sender as MenuItem).Header.ToString().Unescape();
            SelectedHits.CopyToClipboard(h => ApplyCustomFormat(h, format));
        }

        private string GetSaveFile()
        {
            var sfd = new SaveFileDialog();
            sfd.Filter = "TXT files | *.txt";
            sfd.FilterIndex = 1;
            sfd.ShowDialog();
            return sfd.FileName;
        }

        private void SaveSelected(object sender, RoutedEventArgs e)
            => TrySave(h => h.Data);

        private void SaveSelectedWithCapture(object sender, RoutedEventArgs e)
            => TrySave(captureMapping);

        private void SaveSelectedFull(object sender, RoutedEventArgs e)
            => TrySave(fullMapping);

        private void SaveSelectedCustom(object sender, RoutedEventArgs e)
        {
            var format = (sender as MenuItem).Header.ToString().Unescape();
            TrySave(h => ApplyCustomFormat(h, format));
        }

        private void TrySave(Func<HitEntity, string> mapping)
        {
            try
            {
                SelectedHits.SaveToFile(GetSaveFile(), mapping);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }

        private static string ApplyCustomFormat(HitEntity hit, string format)
            => new StringBuilder(format)
                .Replace("<DATA>", hit.Data)
                .Replace("<PROXY>", hit.Proxy)
                .Replace("<DATE>", hit.Date.ToLongDateString() + " " + hit.Date.ToLongTimeString())
                .Replace("<CONFIG>", hit.ConfigName)
                .Replace("<WORDLIST>", hit.WordlistName)
                .Replace("<TYPE>", hit.Type)
                .Replace("<CAPTURE>", hit.CapturedData)
                .ToString();

        private void LVIMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
    }
}
