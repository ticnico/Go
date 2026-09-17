using Core.Entities;
using Core.Models.Jobs;
using Core.Repositories;
using Core.Services;
using GoldenBullet.Utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GoldenBullet.ViewModels
{
    public class JobsViewModel : ViewModelBase
    {
        private readonly IJobRepository jobRepo;
        private readonly JobManagerService jobManager;
        private readonly JobFactoryService jobFactory;
        private readonly DispatcherTimer timer; // FIX: DispatcherTimer runs on UI thread

        private ObservableCollection<JobViewModel> jobsCollection;
        public ObservableCollection<JobViewModel> JobsCollection
        {
            get => jobsCollection;
            set
            {
                jobsCollection = value;
                OnPropertyChanged();
            }
        }

        public JobsViewModel()
        {
            jobRepo = SP.GetService<IJobRepository>();
            jobManager = SP.GetService<JobManagerService>();
            jobFactory = SP.GetService<JobFactoryService>();

            CreateCollection();

            // FIX: Use DispatcherTimer so RefreshJobs runs on UI thread
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) => RefreshJobs();
            timer.Start();
        }

        private void RefreshJobs()
        {
            foreach (var job in JobsCollection)
            {
                job.UpdateViewModel();
            }
        }

        private void CreateCollection()
        {
            var viewModels = jobManager.Jobs.Select(j => MakeViewModel(j)).OrderBy(j => j.Id).ToList();
            JobsCollection = new ObservableCollection<JobViewModel>();
            foreach (var vm in viewModels)
                JobsCollection.Add(vm);
        }

        public async Task<JobViewModel> CreateJobAsync(JobOptions options)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };

            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = GetJobType(options),
                JobOptions = JsonConvert.SerializeObject(wrapper, settings)
            };

            await jobRepo.AddAsync(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);
            var jobVM = MakeViewModel(job);

            jobManager.AddJob(job);
            JobsCollection.Add(jobVM);

            return jobVM;
        }

        public async Task<JobViewModel> EditJobAsync(JobEntity entity, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            entity.JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings);

            await jobRepo.UpdateAsync(entity);

            var oldJob = jobManager.Jobs.First(j => j.Id == entity.Id);
            var newJob = jobFactory.FromOptions(entity.Id, 0, options);

            jobManager.RemoveJob(oldJob);
            jobManager.AddJob(newJob);

            // FIX: Replace the ViewModel in-place instead of recreating whole collection
            var oldVm = JobsCollection.First(j => j.Id == entity.Id);
            var index = JobsCollection.IndexOf(oldVm);
            var newVm = MakeViewModel(newJob);
            JobsCollection[index] = newVm;

            return newVm;
        }

        public async Task<JobViewModel> CloneJobAsync(JobType type, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = type,
                JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings)
            };

            await jobRepo.AddAsync(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);
            jobManager.AddJob(job);

            JobViewModel jobVM = type switch
            {
                JobType.MultiRun => new MultiRunJobViewModel(job as MultiRunJob),
                JobType.ProxyCheck => new ProxyCheckJobViewModel(job as ProxyCheckJob),
                _ => throw new NotImplementedException()
            };

            JobsCollection.Add(jobVM);

            return jobVM;
        }

        public void RemoveAll()
        {
            var notIdleJobs = jobManager.Jobs.Where(j => j.Status != JobStatus.Idle);

            if (notIdleJobs.Any())
            {
                throw new Exception($"The job #{notIdleJobs.First().Id} is not idle, please stop/abort the job first!");
            }

            jobRepo.Purge();
            jobManager.Clear();
            JobsCollection.Clear();
        }

        public async Task RemoveJobAsync(JobViewModel jobVM)
        {
            if (jobVM.Job.Status != JobStatus.Idle)
            {
                throw new Exception("The job is not idle, please stop/abort the job first!");
            }

            var entity = await jobRepo.GetAll().FirstAsync(e => e.Id == jobVM.Id);
            await jobRepo.DeleteAsync(entity);
            jobManager.RemoveJob(jobVM.Job);
            JobsCollection.Remove(jobVM);
        }

        private static JobViewModel MakeViewModel(Job job) => job switch
        {
            MultiRunJob mr => new MultiRunJobViewModel(mr),
            ProxyCheckJob pc => new ProxyCheckJobViewModel(pc),
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(JobOptions options) => options switch
        {
            MultiRunJobOptions => JobType.MultiRun,
            ProxyCheckJobOptions => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(Job job) => job switch
        {
            MultiRunJob => JobType.MultiRun,
            ProxyCheckJob => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };
    }

    public class JobViewModel : ViewModelBase
    {
        public Job Job { get; init; }

        public string IdAndStatus => $"#{Id} [{Status}]";
        public int Id => Job.Id;
        public JobStatus Status => Job.Status;

        public JobViewModel(Job job)
        {
            Job = job;
        }

        public virtual void UpdateViewModel() { }
    }

    public class MultiRunJobViewModel : JobViewModel
    {
        private MultiRunJob MultiRunJob => Job as MultiRunJob;

        private BitmapImage _configIcon;
        public BitmapImage ConfigIcon
        {
            get => _configIcon;
            set
            {
                _configIcon = value;
                OnPropertyChanged();
            }
        }

        public string ConfigName => MultiRunJob.Config is null ? "No config" : MultiRunJob.Config.Metadata.Name;
        public string DataPoolInfo => MultiRunJob.DataPool switch
        {
            WordlistDataPool w => $"{w.Wordlist.Name} (Wordlist)",
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => $"{Path.GetFileName(f.FileName)} (File)",
            _ => throw new NotImplementedException()
        };

        public int Bots => MultiRunJob.Bots;
        public int Skip => MultiRunJob.Skip;
        public JobProxyMode ProxyMode => MultiRunJob.ProxyMode;

        public int DataTested => MultiRunJob.DataTested;
        public int DataHits => MultiRunJob.DataHits;
        public int DataCustom => MultiRunJob.DataCustom;
        public int DataToCheck => MultiRunJob.DataToCheck;
        public int DataFails => MultiRunJob.DataFails;
        public int DataRetried => MultiRunJob.DataRetried;
        public int DataBanned => MultiRunJob.DataBanned;
        public int DataErrors => MultiRunJob.DataErrors;
        public int DataInvalid => MultiRunJob.DataInvalid;

        public int ProxiesTotal => MultiRunJob.ProxiesTotal;
        public int ProxiesAlive => MultiRunJob.ProxiesAlive;
        public int ProxiesBad => MultiRunJob.ProxiesBad;
        public int ProxiesBanned => MultiRunJob.ProxiesBanned;

        public float Progress => MultiRunJob.Progress;
        public string ProgressString
        {
            get
            {
                var tested = MultiRunJob.Status == JobStatus.Idle ? Skip : DataTested + Skip;
                return $"{tested} / {MultiRunJob.DataPool.Size} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";
            }
        }

        public decimal CaptchaCredit => MultiRunJob.CaptchaCredit;
        public string ElapsedString => $"{(int)MultiRunJob.Elapsed.TotalDays} day(s) {MultiRunJob.Elapsed:hh\\:mm\\:ss}";
        public string RemainingString => $"{(int)MultiRunJob.Remaining.TotalDays} day(s) {MultiRunJob.Remaining:hh\\:mm\\:ss}";

        public int CPM => MultiRunJob.CPM;

        public MultiRunJobViewModel(MultiRunJob job) : base(job)
        {
            LoadConfigIcon();
            MultiRunJob.OnStatusChanged += (sender, status) => UpdateStatus();
            MultiRunJob.OnProgress += (sender, progress) => UpdateStats();
        }

        private void LoadConfigIcon()
        {
            if (MultiRunJob.Config?.Metadata?.Base64Image != null)
            {
                ConfigIcon = Images.Base64ToBitmapImage(MultiRunJob.Config.Metadata.Base64Image);
            }
        }

        // FIX: Call UpdateStats() on timer tick so counters refresh every second
        public override void UpdateViewModel()
        {
            PeriodicUpdate();
            UpdateStatus();
            UpdateStats();
        }

        public void PeriodicUpdate()
        {
            OnPropertyChanged(nameof(ElapsedString));
            OnPropertyChanged(nameof(RemainingString));
            OnPropertyChanged(nameof(CPM));
            OnPropertyChanged(nameof(CaptchaCredit));

            OnPropertyChanged(nameof(DataRetried));
            OnPropertyChanged(nameof(DataBanned));
            OnPropertyChanged(nameof(DataErrors));
            OnPropertyChanged(nameof(DataInvalid));

            OnPropertyChanged(nameof(ProxiesTotal));
            OnPropertyChanged(nameof(ProxiesAlive));
            OnPropertyChanged(nameof(ProxiesBad));
            OnPropertyChanged(nameof(ProxiesBanned));
        }

        public void UpdateStats()
        {
            OnPropertyChanged(nameof(DataTested));
            OnPropertyChanged(nameof(DataHits));
            OnPropertyChanged(nameof(DataCustom));
            OnPropertyChanged(nameof(DataToCheck));
            OnPropertyChanged(nameof(DataFails));

            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressString));
        }

        public void UpdateBots() => OnPropertyChanged(nameof(Bots));

        public void UpdateStatus()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IdAndStatus));
        }
    }

    public class ProxyCheckJobViewModel : JobViewModel
    {
        private ProxyCheckJob ProxyCheckJob => Job as ProxyCheckJob;

        public int Bots => ProxyCheckJob.Bots;
        public string Url => ProxyCheckJob.Url;
        public string SuccessKey => ProxyCheckJob.SuccessKey;
        public bool CheckOnlyUntested => ProxyCheckJob.CheckOnlyUntested;
        public int TimeoutMilliseconds => (int)ProxyCheckJob.Timeout.TotalMilliseconds;

        public int Total => ProxyCheckJob.Total;
        public int Tested => ProxyCheckJob.Tested;
        public int Working => ProxyCheckJob.Working;
        public int NotWorking => ProxyCheckJob.NotWorking;

        public float Progress => ProxyCheckJob.Progress;
        public string ProgressString => $"{Tested} / {Total} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";

        public int CPM => ProxyCheckJob.CPM;
        public string ElapsedString => $"{(int)ProxyCheckJob.Elapsed.TotalDays} day(s) {ProxyCheckJob.Elapsed:hh\\:mm\\:ss}";
        public string RemainingString => $"{(int)ProxyCheckJob.Remaining.TotalDays} day(s) {ProxyCheckJob.Remaining:hh\\:mm\\:ss}";

        public ProxyCheckJobViewModel(ProxyCheckJob job) : base(job)
        {
            ProxyCheckJob.OnStatusChanged += (sender, status) => UpdateStatus();
            ProxyCheckJob.OnProgress += (sender, progress) => UpdateStats();
        }

        // FIX: Call UpdateStats() on timer tick so counters refresh every second
        public override void UpdateViewModel()
        {
            PeriodicUpdate();
            UpdateStatus();
            UpdateStats();
        }

        public void PeriodicUpdate()
        {
            OnPropertyChanged(nameof(ElapsedString));
            OnPropertyChanged(nameof(RemainingString));
            OnPropertyChanged(nameof(CPM));

            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Tested));
            OnPropertyChanged(nameof(Working));
            OnPropertyChanged(nameof(NotWorking));
        }

        public void UpdateStats()
        {
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressString));
        }

        public void UpdateBots() => OnPropertyChanged(nameof(Bots));

        public void UpdateStatus()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IdAndStatus));
        }
    }
}
