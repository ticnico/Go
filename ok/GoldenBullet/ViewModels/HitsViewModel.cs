using Core.Entities;
using Core.Repositories;
using Core.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace GoldenBullet.ViewModels
{
    public class HitsViewModel : ViewModelBase
    {
        private readonly GoldenBulletSettingsService obSettingsService;
        private readonly IHitRepository hitRepo;
        private bool initialized;
        private bool _isRefreshing;

        private ObservableCollection<HitEntity> hitsCollection;
        public ObservableCollection<HitEntity> HitsCollection
        {
            get => hitsCollection;
            private set
            {
                hitsCollection = value;
                OnPropertyChanged();

                // ✅ CRITICAL: Re-hook filters whenever collection changes
                HookFilters();

                // ✅ Invalidate caches when data changes
                _cachedConfigNames = null;
                _cachedHitTypes = null;
                OnPropertyChanged(nameof(ConfigNames));
                OnPropertyChanged(nameof(HitTypes));

                UpdateTotal();
            }
        }

        private int _total;
        public int Total
        {
            get => _total;
            private set
            {
                _total = value;
                OnPropertyChanged();
            }
        }

        private string searchString = string.Empty;
        private DispatcherTimer _filterDebounceTimer;

        public string SearchString
        {
            get => searchString;
            set
            {
                if (searchString == value) return;
                searchString = value;
                OnPropertyChanged();
                DebounceFilterRefresh();
            }
        }

        private List<string> _cachedConfigNames;
        private List<string> _cachedHitTypes;

        public IEnumerable<string> ConfigNames
        {
            get
            {
                if (_cachedConfigNames != null) return _cachedConfigNames;

                var configs = HitsCollection?
                    .Where(h => !string.IsNullOrWhiteSpace(h.ConfigName))
                    .Select(h => h.ConfigName)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList() ?? new List<string>();

                _cachedConfigNames = new List<string> { "All" };
                _cachedConfigNames.AddRange(configs);
                return _cachedConfigNames;
            }
        }

        private string configFilter = "All";
        public string ConfigFilter
        {
            get => configFilter;
            set
            {
                if (configFilter == value) return;
                configFilter = value;
                OnPropertyChanged();
                DebounceFilterRefresh();
            }
        }

        public IEnumerable<string> HitTypes
        {
            get
            {
                if (_cachedHitTypes != null) return _cachedHitTypes;

                var types = HitsCollection?
                    .Where(h => !string.IsNullOrWhiteSpace(h.Type))
                    .Select(h => h.Type)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList() ?? new List<string>();

                _cachedHitTypes = new List<string> { "All" };
                _cachedHitTypes.AddRange(types);
                return _cachedHitTypes;
            }
        }

        private string typeFilter = "All";
        public string TypeFilter
        {
            get => typeFilter;
            set
            {
                if (typeFilter == value) return;
                typeFilter = value;
                OnPropertyChanged();
                DebounceFilterRefresh();
            }
        }

        public HitsViewModel()
        {
            obSettingsService = SP.GetService<GoldenBulletSettingsService>();
            hitRepo = SP.GetService<IHitRepository>();
            HitsCollection = new ObservableCollection<HitEntity>();

            _filterDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150),
                IsEnabled = false
            };
            _filterDebounceTimer.Tick += (s, e) =>
            {
                _filterDebounceTimer.Stop();
                RefreshFilter();
            };

            // ✅ Hook filters once - they'll be re-applied when collection changes
            HookFilters();
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            if (initialized) return;
            await RefreshListAsync();
            initialized = true;
        }

        public void HookFilters()
        {
            var view = CollectionViewSource.GetDefaultView(HitsCollection);
            view.Filter = null; // Clear old filter first
            view.Filter = HitsFilter; // Apply new filter
            view.Refresh(); // Force initial refresh
        }

        private bool HitsFilter(object item)
        {
            if (item is not HitEntity hit) return false;

            // ✅ Config filter - strict match
            if (configFilter != "All" && hit.ConfigName != configFilter)
                return false;

            // ✅ Type filter - strict match  
            if (typeFilter != "All" && hit.Type != typeFilter)
                return false;

            // ✅ Search filter - check multiple fields
            if (!string.IsNullOrEmpty(searchString))
            {
                var search = searchString.ToLowerInvariant();
                return (hit.CapturedData?.ToLowerInvariant().Contains(search) == true)
                    || (hit.Data?.ToLowerInvariant().Contains(search) == true)
                    || (hit.ConfigName?.ToLowerInvariant().Contains(search) == true)
                    || (hit.WordlistName?.ToLowerInvariant().Contains(search) == true)
                    || (hit.Proxy?.ToLowerInvariant().Contains(search) == true);
            }

            return true;
        }

        private void DebounceFilterRefresh()
        {
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private void RefreshFilter()
        {
            // ✅ Get current view and refresh - this re-applies the filter
            var view = CollectionViewSource.GetDefaultView(HitsCollection);
            if (view != null)
            {
                view.Refresh();
                UpdateTotal();
            }
        }

        private void UpdateTotal()
        {
            var view = CollectionViewSource.GetDefaultView(HitsCollection);
            Total = view?.Cast<object>().Count() ?? HitsCollection?.Count ?? 0;
        }

        public async Task RefreshListAsync()
        {
            if (_isRefreshing) return;
            _isRefreshing = true;

            try
            {
                var items = await hitRepo.GetAll()
                    .AsNoTracking()
                    .ToListAsync()
                    .ConfigureAwait(false);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // ✅ Setting the property triggers HookFilters() automatically
                    HitsCollection = new ObservableCollection<HitEntity>(items);

                    // ✅ Force one final refresh to ensure filters apply to new data
                    RefreshFilter();

                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HitsViewModel] Refresh failed: {ex}");
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        public async Task AddHitAsync(HitEntity hit)
        {
            await hitRepo.AddAsync(hit).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HitsCollection.Add(hit);

                // ✅ Invalidate caches only if new values appear
                if (_cachedConfigNames != null && !string.IsNullOrWhiteSpace(hit.ConfigName)
                    && !_cachedConfigNames.Contains(hit.ConfigName))
                {
                    _cachedConfigNames = null;
                    OnPropertyChanged(nameof(ConfigNames));
                }
                if (_cachedHitTypes != null && !string.IsNullOrWhiteSpace(hit.Type)
                    && !_cachedHitTypes.Contains(hit.Type))
                {
                    _cachedHitTypes = null;
                    OnPropertyChanged(nameof(HitTypes));
                }

                UpdateTotal();
            }, DispatcherPriority.Background);
        }

        public Task Update(HitEntity hit) => hitRepo.UpdateAsync(hit);

        public async Task DeleteAsync(IEnumerable<HitEntity> hits)
        {
            var hitsList = hits.ToList();
            await hitRepo.DeleteAsync(hitsList).ConfigureAwait(false);
            await RefreshListAsync();
        }

        public async Task PurgeAsync()
        {
            await hitRepo.PurgeAsync().ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HitsCollection.Clear();
                // ✅ Clearing collection triggers property setter which re-hooks filters
                UpdateTotal();
            }, DispatcherPriority.Background);
        }

        public async Task<int> DeleteDuplicatesAsync()
        {
            var duplicateIds = await Task.Run(() =>
            {
                return HitsCollection
                    .AsParallel()
                    .GroupBy(h => h.GetHashCode(obSettingsService.Settings.GeneralSettings.IgnoreWordlistNameOnHitsDedupe))
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g.OrderBy(h => h.Date).Reverse().Skip(1))
                    .Select(h => h.Id)
                    .Distinct() // ✅ ADDED: Ensures we don't accidentally query the same ID twice
                    .ToList();
            });

            if (!duplicateIds.Any()) return 0;

            // ✅ FIX: REMOVED .AsNoTracking()!
            // If the DbContext is already tracking these hits, EF will return the 
            // already-tracked instances, preventing the "already being tracked" conflict.
            var duplicates = await hitRepo.GetAll()
                .Where(h => duplicateIds.Contains(h.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            if (!duplicates.Any()) return 0;

            await hitRepo.DeleteAsync(duplicates).ConfigureAwait(false);
            await RefreshListAsync();
            return duplicates.Count;
        }

        public override void UpdateViewModel()
        {
            _ = RefreshListAsync();
            base.UpdateViewModel();
        }
    }
}
