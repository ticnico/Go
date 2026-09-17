using Core;
using Core.Entities;
using Core.Helpers;
using Core.Repositories;
using Core.Services;
using GoldenBullet.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Models.Jobs;
using RuriLib.Models.Proxies;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GoldenBullet.ViewModels
{
    public class ProxiesViewModel : ViewModelBase
    {
        private ObservableCollection<ProxyGroupEntity> _proxyGroupsCollection;
        private ObservableCollection<ProxyEntity> _proxiesCollection;
        private IProxyGroupRepository _proxyGroupRepo;
        private IProxyRepository _proxyRepo;
        private readonly JobManagerService _jobManager;
        private bool _initialized;
        private ProxyGroupEntity _selectedGroup;
        private readonly ProxyGroupEntity _allGroup = new() { Id = -1, Name = "All" };

        private int _total;
        private int _working;
        private int _notWorking;

        public ObservableCollection<ProxyEntity> ProxiesCollection
        {
            get => _proxiesCollection;
            private set
            {
                _proxiesCollection = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ProxyGroupEntity> ProxyGroupsCollection
        {
            get => _proxyGroupsCollection;
            private set
            {
                _proxyGroupsCollection = value;
                OnPropertyChanged();
            }
        }

        public int Total { get => _total; private set { _total = value; OnPropertyChanged(); } }
        public int Working { get => _working; private set { _working = value; OnPropertyChanged(); } }
        public int NotWorking { get => _notWorking; private set { _notWorking = value; OnPropertyChanged(); } }

        public int SelectedGroupId
        {
            get => _selectedGroup.Id;
            set
            {
                if (_selectedGroup?.Id == value) return;
                _selectedGroup = ProxyGroupsCollection.First(g => g.Id == value);
                OnPropertyChanged();
                _ = RefreshListAsync();
            }
        }

        public bool GroupIsValid => _selectedGroup != _allGroup;
        public ProxyGroupEntity SelectedGroup => _selectedGroup;

        public ProxiesViewModel()
        {
            _proxyGroupRepo = SP.GetService<IProxyGroupRepository>();
            _proxyRepo = SP.GetService<IProxyRepository>();
            _jobManager = SP.GetService<JobManagerService>();

            ProxyGroupsCollection = new ObservableCollection<ProxyGroupEntity> { _allGroup };
            ProxiesCollection = new ObservableCollection<ProxyEntity>();

            Total = 0; Working = 0; NotWorking = 0;
            SelectedGroupId = _allGroup.Id;
            _ = InitializeAsync();
        }

        public async Task InitializeAsync()
        {
            if (!_initialized) { await RefreshGroupsAsync(); _initialized = true; }
        }

        public async Task RefreshGroupsAsync()
        {
            SelectedGroupId = _allGroup.Id;
            var entities = await _proxyGroupRepo.GetAll().ToListAsync();

            ProxyGroupsCollection.Clear();
            ProxyGroupsCollection.Add(_allGroup);
            foreach (var e in entities) ProxyGroupsCollection.Add(e);

            await RefreshListAsync();
        }

        // ✅ Used ONLY when switching groups or initial load
        public async Task RefreshListAsync()
        {
            var items = _selectedGroup == _allGroup
                ? await _proxyRepo.GetAll().ToListAsync()
                : await _proxyRepo.GetAll().Include(p => p.Group).Where(p => p.Group.Id == _selectedGroup.Id).ToListAsync();

            ProxiesCollection = new ObservableCollection<ProxyEntity>(items);
            Total = items.Count;
            Working = items.Count(p => p.Status == ProxyWorkingStatus.Working);
            NotWorking = items.Count(p => p.Status == ProxyWorkingStatus.NotWorking);
        }

        public async Task AddGroupAsync(ProxyGroupEntity group)
        {
            ProxyGroupsCollection.Add(group);
            SelectedGroupId = _allGroup.Id;
            await _proxyGroupRepo.AddAsync(group);
        }

        public async Task EditGroupAsync(ProxyGroupEntity group)
        {
            await _proxyGroupRepo.UpdateAsync(group);
            await RefreshGroupsAsync();
        }

        public async Task DeleteSelectedGroupAsync()
        {
            if (_selectedGroup == _allGroup) throw new Exception("Select a group first");

            var firstProxies = _jobManager.Jobs.OfType<ProxyCheckJob>()
                .Select(j => j.Proxies.FirstOrDefault()).Where(p => p != null);

            foreach (var f in firstProxies)
            {
                if (ProxiesCollection.Any(p => p.Id == f.Id))
                    throw new Exception("Group in use by a proxy check job");
            }

            await _proxyGroupRepo.DeleteAsync(_selectedGroup);
            SelectedGroupId = _allGroup.Id;
            await RefreshGroupsAsync();
        }

        public async Task AddProxiesAsync(ProxiesForImportDto dto)
        {
            if (_selectedGroup == _allGroup) throw new Exception("Select a group first");

            var proxies = new List<Proxy>();
            foreach (var line in dto.Lines.Where(l => !string.IsNullOrEmpty(l)).Distinct())
            {
                try { proxies.Add(Proxy.Parse(line, dto.DefaultType, dto.DefaultUsername, dto.DefaultPassword)); }
                catch { }
            }

            var entities = proxies.Select(p => Mapper.MapProxyToProxyEntity(p)).ToList();

            try
            {
                using var scope = SP.GetService<IServiceScopeFactory>().CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var currentGroup = await db.ProxyGroups.FindAsync(_selectedGroup.Id);

                entities.ForEach(e => e.Group = currentGroup);
                db.Proxies.AddRange(entities);
                await db.SaveChangesAsync();

                await _proxyRepo.RemoveDuplicatesAsync(_selectedGroup.Id);

                // ✅ For adding, we still need to refresh to handle duplicates correctly
                await RefreshListAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddProxiesAsync error: {ex}");
                throw;
            }
        }

        // ✅ OPTIMIZED: Updates UI in memory instead of re-fetching from DB
        // ✅ OPTIMIZED: Bulk delete by IDs + In-memory UI update
        public async Task DeleteAsync(IEnumerable<ProxyEntity> proxies)
        {
            var list = proxies.ToList();
            if (!list.Any()) return;

            var ids = list.Select(p => p.Id).ToList();

            // ✅ Use the new ultra-fast bulk delete
            await _proxyRepo.DeleteByIdsAsync(ids);

            var toRemoveIds = new HashSet<int>(ids);
            var remaining = ProxiesCollection.Where(p => !toRemoveIds.Contains(p.Id)).ToList();

            ProxiesCollection = new ObservableCollection<ProxyEntity>(remaining);

            Total = ProxiesCollection.Count;
            Working = remaining.Count(p => p.Status == ProxyWorkingStatus.Working);
            NotWorking = remaining.Count(p => p.Status == ProxyWorkingStatus.NotWorking);
        }

        // ✅ OPTIMIZED: Bulk delete by IDs + In-memory UI update
        public async Task DeleteNotWorkingAsync()
        {
            var toRemove = ProxiesCollection.Where(p => p.Status == ProxyWorkingStatus.NotWorking).ToList();
            if (!toRemove.Any()) return;

            var ids = toRemove.Select(p => p.Id).ToList();

            // ✅ Use the new ultra-fast bulk delete
            await _proxyRepo.DeleteByIdsAsync(ids);

            var remaining = ProxiesCollection.Where(p => p.Status != ProxyWorkingStatus.NotWorking).ToList();
            ProxiesCollection = new ObservableCollection<ProxyEntity>(remaining);

            Total = ProxiesCollection.Count;
            NotWorking = 0; // We just deleted them all
        }

        // ✅ OPTIMIZED: Bulk delete by IDs + In-memory UI update
        public async Task DeleteUntestedAsync()
        {
            var toRemove = ProxiesCollection.Where(p => p.Status == ProxyWorkingStatus.Untested).ToList();
            if (!toRemove.Any()) return;

            var ids = toRemove.Select(p => p.Id).ToList();

            // ✅ Use the new ultra-fast bulk delete
            await _proxyRepo.DeleteByIdsAsync(ids);

            var remaining = ProxiesCollection.Where(p => p.Status != ProxyWorkingStatus.Untested).ToList();
            ProxiesCollection = new ObservableCollection<ProxyEntity>(remaining);

            Total = ProxiesCollection.Count;
        }

        public void UpdateProxyStatus(ProxyEntity proxyEntity)
        {
            try
            {
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => UpdateProxyStatus(proxyEntity));
                    return;
                }

                var existingProxy = ProxiesCollection.FirstOrDefault(p =>
                    p.Host == proxyEntity.Host &&
                    p.Port == proxyEntity.Port &&
                    p.Username == proxyEntity.Username &&
                    p.Password == proxyEntity.Password);

                if (existingProxy != null)
                {
                    bool wasWorking = existingProxy.Status == ProxyWorkingStatus.Working;
                    bool wasNotWorking = existingProxy.Status == ProxyWorkingStatus.NotWorking;

                    existingProxy.Status = proxyEntity.Status;
                    existingProxy.Ping = proxyEntity.Ping;
                    existingProxy.LastChecked = proxyEntity.LastChecked;
                    if (!string.IsNullOrEmpty(proxyEntity.Country))
                        existingProxy.Country = proxyEntity.Country;

                    bool isWorking = existingProxy.Status == ProxyWorkingStatus.Working;
                    bool isNotWorking = existingProxy.Status == ProxyWorkingStatus.NotWorking;

                    if (wasWorking != isWorking || wasNotWorking != isNotWorking)
                    {
                        if (isWorking) { Working++; if (wasNotWorking) NotWorking--; }
                        else if (isNotWorking) { NotWorking++; if (wasWorking) Working--; }
                        else { if (wasWorking) Working--; if (wasNotWorking) NotWorking--; }

                        OnPropertyChanged(nameof(Working));
                        OnPropertyChanged(nameof(NotWorking));
                    }

                    int index = ProxiesCollection.IndexOf(existingProxy);
                    if (index >= 0)
                    {
                        // ✅ MICRO-OPTIMIZATION: Use indexer to fire a single 'Replace' event 
                        // instead of Remove+Insert (which fires 2 events). Crucial for proxy checking!
                        ProxiesCollection[index] = existingProxy;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateProxyStatus error: {ex.Message}");
            }
        }

        public override void UpdateViewModel()
        {
            _ = RefreshListAsync();
            base.UpdateViewModel();
        }
    }
}
