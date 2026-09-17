using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Repositories;
using Core.Services;
using GoldenBullet.DTOs;
using GoldenBullet.Utils;
using RuriLib.Models.Configs;
using RuriLib.Models.Configs.Settings;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace GoldenBullet.ViewModels
{
    public enum ViewMode
    {
        Table,
        Grid
    }

    public class ConfigsViewModel : ViewModelBase
    {
        private readonly ConfigService configService;
        private readonly IConfigRepository configRepo;

        private ObservableCollection<ConfigViewModel> configsCollection;
        public ObservableCollection<ConfigViewModel> ConfigsCollection
        {
            get => configsCollection;
            set
            {
                // ✅ Unsubscribe from old collection to prevent memory leaks
                if (configsCollection != null)
                    configsCollection.CollectionChanged -= OnConfigsCollectionChanged;

                configsCollection = value;

                // ✅ Subscribe to new collection for auto-refresh
                if (configsCollection != null)
                    configsCollection.CollectionChanged += OnConfigsCollectionChanged;

                OnPropertyChanged();
                OnPropertyChanged(nameof(Total)); // ✅ Notify UI that Total changed
            }
        }

        // View Mode - Grid is default
        private ViewMode currentViewMode = ViewMode.Grid;
        public ViewMode CurrentViewMode
        {
            get => currentViewMode;
            set
            {
                currentViewMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTableView));
                OnPropertyChanged(nameof(IsGridView));
            }
        }

        public bool IsTableView => CurrentViewMode == ViewMode.Table;
        public bool IsGridView => CurrentViewMode == ViewMode.Grid;

        // Search
        private string searchString = string.Empty;
        public string SearchString
        {
            get => searchString;
            set
            {
                searchString = value;
                OnPropertyChanged();
                ApplyFilter();
                // ✅ Total updates automatically via ApplyFilter -> OnPropertyChanged(nameof(Total))
            }
        }

        // Selection
        private ConfigViewModel selectedConfig;
        public ConfigViewModel SelectedConfig
        {
            get => selectedConfig;
            set
            {
                if (selectedConfig != null)
                    selectedConfig.IsSelected = false;

                selectedConfig = value;

                if (selectedConfig != null)
                    selectedConfig.IsSelected = true;

                configService.SelectedConfig = value?.Config;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConfigSelected));
            }
        }

        public bool IsConfigSelected => SelectedConfig != null;

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

        // ✅ UPDATED: Total auto-refreshes and shows filtered count
        public int Total
        {
            get
            {
                if (ConfigsCollection == null) return 0;

                // ✅ Option A: Return filtered count (matches what user sees in UI)
                var view = CollectionViewSource.GetDefaultView(ConfigsCollection);
                return view.Cast<ConfigViewModel>().Count();

                // Option B: Return raw count (all configs, ignoring search filter)
                // return ConfigsCollection.Count;
            }
        }

        public ConfigsViewModel()
        {
            configService = SP.GetService<ConfigService>();
            configService.OnRemotesLoaded += (s, e) => CreateCollection();

            configRepo = SP.GetService<IConfigRepository>();
            CreateCollection();
        }

        // ✅ NEW: Auto-update Total when collection changes (add/remove/clear)
        private void OnConfigsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Total));
        }

        private void ApplyFilter()
        {
            if (ConfigsCollection == null) return;

            var view = CollectionViewSource.GetDefaultView(ConfigsCollection);
            view.Filter = item =>
            {
                if (item is ConfigViewModel config)
                {
                    return config.Config.Metadata.Name.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                           config.Author.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                           config.Category.Contains(searchString, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };

            // ✅ Notify Total changed when filter is reapplied
            OnPropertyChanged(nameof(Total));
            view.Refresh();
        }

        public async Task CreateAsync(ConfigForCreationDto dto)
        {
            // ✅ Get RuriLib settings to initialize AllowedWordlistTypes
            var rlSettings = SP.GetService<RuriLib.Services.RuriLibSettingsService>();

            // ✅ Create Config object directly
            var newConfig = new Config
            {
                Id = Guid.NewGuid().ToString("N"),
                Metadata = new ConfigMetadata
                {
                    Name = dto.Name,
                    Category = dto.Category,
                    Author = dto.Author,
                    CreationDate = DateTime.Now,
                    LastModified = DateTime.Now
                    // Base64Image will be set below if icon provided
                },
                IsRemote = false,
                Settings = new ConfigSettings
                {
                    DataSettings = new DataSettings
                    {
                        AllowedWordlistTypes = new[]
                        {
                            rlSettings.Environment.WordlistTypes.First().Name
                        }
                    },
                    ProxySettings = new ProxySettings
                    {
                        UseProxies = false
                    }
                }
            };

            // 🔑 HANDLE ICON - EXACT REFERENCE PATTERN 🔑
            if (!string.IsNullOrWhiteSpace(dto.IconPath) && File.Exists(dto.IconPath))
            {
                try
                {
                    // Convert file to Base64 (same as vm.SetIconFromFile in reference)
                    newConfig.Metadata.Base64Image = Convert.ToBase64String(File.ReadAllBytes(dto.IconPath));
                }
                catch (Exception ex)
                {
                    // Log but don't fail config creation
                    System.Diagnostics.Debug.WriteLine($"Icon processing failed: {ex.Message}");
                }
            }

            var newConfigVM = new ConfigViewModel(newConfig);

            // ✅ Save ONLY ONCE
            await Save(newConfigVM);

            ConfigsCollection.Insert(0, newConfigVM);
            configService.SelectedConfig = newConfig;
            configService.Configs.Add(newConfig);
        }

        public void Delete(ConfigViewModel vm)
        {
            if (vm == SelectedConfig)
            {
                SelectedConfig = null;
            }

            configRepo.Delete(vm.Config);
            ConfigsCollection.Remove(vm);
            // ✅ Total auto-updates via CollectionChanged event
        }

        public Task Save(ConfigViewModel vm)
        {
            if (vm.IsRemote)
            {
                throw new Exception("You cannot save remote configs");
            }

            return configRepo.SaveAsync(vm.Config);
        }

        public async Task RescanAsync()
        {
            SelectedConfig = null;
            HoveredConfig = null;
            await configService.ReloadConfigsAsync();
            CreateCollection();
            // ✅ Total auto-updates via CreateCollection -> OnPropertyChanged(nameof(Total))
        }

        public override void UpdateViewModel()
        {
            ConfigsCollection?.ToList().ForEach(c => c.UpdateViewModel());
            base.UpdateViewModel();
        }

        public void CreateCollection()
        {
            var viewModels = configService.Configs.Select(c => new ConfigViewModel(c));

            // ✅ PERFORMANCE FIX: Assign a brand new collection. 
            // The ConfigsCollection setter automatically handles unsubscribing from the old one 
            // and subscribing to the new one. This fires the UI update exactly ONCE instead of N times.
            ConfigsCollection = new ObservableCollection<ConfigViewModel>(viewModels);

            Application.Current.Dispatcher.Invoke(() =>
            {
                ApplyFilter();
            });
        }
    }

    public class ConfigViewModel : ViewModelBase
    {
        public Config Config { get; init; }

        // ✅ PERFORMANCE FIX: Cache the BitmapImage so it's only decoded ONCE
        private BitmapImage _icon;
        public BitmapImage Icon
        {
            get
            {
                if (_icon == null && !string.IsNullOrWhiteSpace(Config.Metadata.Base64Image))
                {
                    try
                    {
                        _icon = Images.Base64ToBitmapImage(Config.Metadata.Base64Image);
                    }
                    catch
                    {
                        // Ignore invalid base64 strings gracefully
                    }
                }
                return _icon;
            }
        }

        public string Id => Config.Id;
        public string Name => Config.Metadata.Name;
        public string Author => Config.Metadata.Author;
        public string Category => Config.Metadata.Category;
        public bool NeedsProxies => Config.Settings.ProxySettings.UseProxies;
        public string ProxiesStatus => NeedsProxies ? "✓" : "×";
        public string RemoteStatus => IsRemote ? "✓" : "×";
        public string AllowedWordlistTypes => Config.Settings.DataSettings.AllowedWordlistTypesString;
        public DateTime CreationDate => Config.Metadata.CreationDate;
        public DateTime LastModified => Config.Metadata.LastModified;
        public string Readme => Config.Readme;
        public bool IsRemote => Config.IsRemote;

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                isSelected = value;
                OnPropertyChanged();
            }
        }

        public ConfigViewModel(Config config)
        {
            Config = config;
        }

        // ✅ Clears the cached icon so it reloads if the image changes
        public void RefreshIcon()
        {
            _icon = null;
            OnPropertyChanged(nameof(Icon));
        }

        // ✅ Added to match the call in ConfigsViewModel.UpdateViewModel()
        public void UpdateViewModel()
        {
            RefreshIcon();
        }
    }
}
