using Core;
using Core.Models.Proxies;
using Core.Repositories;
using Core.Services;
using GoldenBullet.Helpers;
using GoldenBullet.Logging;
using GoldenBullet.Services;
using GoldenBullet.Views.Pages.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RuriLib.Logging;
using RuriLib.Providers.RandomNumbers;
using RuriLib.Providers.UserAgents;
using RuriLib.Services;
using System.IO;
using System.Net;
using System.Threading; // Added for Thread
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GoldenBullet
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly ServiceProvider serviceProvider;
        private readonly IConfiguration config;

        // Added for Splash Screen
        private static Dispatcher splashDispatcher;

        public App()
        {
            // ==========================================
            // SPLASH SCREEN START (Runs on separate thread)
            // ==========================================
            Thread splashThread = new Thread(() =>
            {
                SplashScreen splash = new SplashScreen();
                splash.Show();
                splashDispatcher = Dispatcher.CurrentDispatcher;
                Dispatcher.Run();
            });
            splashThread.SetApartmentState(ApartmentState.STA);
            splashThread.IsBackground = true;
            splashThread.Start();

            // Small delay to let the splash screen render before heavy tasks start
            Thread.Sleep(200);
            // ==========================================

            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskException;

            Directory.CreateDirectory("UserData");

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddTransient<IConfiguration>(_ => builder.Build());
            ConfigureServices(serviceCollection);
            serviceProvider = serviceCollection.BuildServiceProvider();
            SP.Init(serviceProvider);

            config = SP.GetService<IConfiguration>();
            var workerThreads = config.GetSection("Resources").GetValue("WorkerThreads", 1000);
            var ioThreads = config.GetSection("Resources").GetValue("IOThreads", 1000);
            var connectionLimit = config.GetSection("Resources").GetValue("ConnectionLimit", 1000);

            ThreadPool.SetMinThreads(workerThreads, ioThreads);
            ServicePointManager.DefaultConnectionLimit = connectionLimit;

            // Apply DB migrations or create a DB if it doesn't exist
            using (var serviceScope = serviceProvider.GetService<IServiceScopeFactory>().CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.Migrate();
            }

            // Load the configs
            var configService = serviceProvider.GetService<ConfigService>();
            configService.ReloadConfigsAsync().Wait();

            AutocompletionProvider.Init();

            // Start the job monitor at the start of the application,
            // otherwise it will only be started when navigating to the page
            _ = serviceProvider.GetService<JobMonitorService>();

            // Register a class handler so PreviewMouseWheel from any ScrollBar is
            // observed (including already-handled events) and forwarded to the
            // parent ScrollViewer. This covers cases where EventSetter doesn't run
            // (e.g. some controls/templates) and helps two-finger trackpad scrolling.
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ScrollBar),
                UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ScrollBar_PreviewMouseWheel), true);

            // Also handle mouse wheel on inner ScrollViewers so that when an inner
            // ScrollViewer reaches its top/bottom it forwards the wheel to the
            // containing ScrollViewer (common for nested viewers in block settings).
            EventManager.RegisterClassHandler(typeof(ScrollViewer),
                UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(ScrollViewer_PreviewMouseWheel), true);
        }

        // Forward mouse wheel events when the pointer is over a ScrollBar so the
        // parent ScrollViewer still scrolls. This makes mouse wheel work even when
        // hovering the visible scrollbar thumbs/track.
        private void ScrollBar_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollBar sb) return;

            // Find parent ScrollViewer
            DependencyObject? parent = sb;
            while (parent is not null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ScrollViewer sv)
            {
                // Raise a MouseWheel event on the ScrollViewer so it handles scrolling
                var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };

                sv.RaiseEvent(args);

                // Mark original as handled to avoid double processing
                e.Handled = true;
            }
        }

        // When an inner ScrollViewer receives mouse wheel, forward it to parent
        // if inner cannot scroll further in the wheel direction. This enables
        // two-finger and mouse wheel propagation in nested scroll scenarios.
        private void ScrollViewer_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;

            // If inner can scroll in the direction, let it handle the event
            if ((e.Delta > 0 && sv.VerticalOffset > 0) || (e.Delta < 0 && sv.VerticalOffset < sv.ScrollableHeight))
            {
                // Not at boundary — do nothing
                return;
            }

            // Find an ancestor ScrollViewer
            DependencyObject? parent = VisualTreeHelper.GetParent(sv);
            while (parent is not null && parent is not ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is ScrollViewer psv)
            {
                var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };
                psv.RaiseEvent(args);
                e.Handled = true;
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Windows and pages
            services.AddSingleton<MainWindow>();
            services.AddSingleton<Debugger>();

            // EF
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(config.GetConnectionString("DefaultConnection"),
                b => b.MigrationsAssembly("Core")), ServiceLifetime.Transient);

            // Repositories
            services.AddSingleton<IConfigRepository>(sp =>
                new FileConfigRepository(Path.Combine(Directory.GetCurrentDirectory(), "UserData", "Configs")));
            services.AddSingleton<GoldenBullet.ViewModels.ProxiesViewModel>();
            services.AddSingleton<IProxyRepository, DbProxyRepository>();
            services.AddSingleton<IProxyGroupRepository, DbProxyGroupRepository>();
            services.AddSingleton<IHitRepository, DbHitRepository>();
            services.AddSingleton<IJobRepository, DbJobRepository>();
            services.AddSingleton<IRecordRepository, DbRecordRepository>();
            services.AddSingleton<IConfigRepository>(service =>
                new DiskConfigRepository(service.GetService<RuriLibSettingsService>(),
                "UserData/Configs"));
            services.AddSingleton<IWordlistRepository>(service =>
                new HybridWordlistRepository(service.GetService<ApplicationDbContext>(),
                "UserData/Wordlists"));

            // Singletons
            services.AddSingleton<VolatileSettingsService>();
            services.AddSingleton<ViewModelsService>();
            services.AddSingleton<AnnouncementService>();
            services.AddSingleton<UpdateService>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<ProxyReloadService>();
            services.AddSingleton<ProxyCheckOutputFactory>();
            services.AddSingleton<JobFactoryService>();
            services.AddSingleton<JobManagerService>();
            services.AddSingleton<JobMonitorService>();
            services.AddSingleton<HitStorageService>();
            services.AddSingleton<DataPoolFactoryService>();
            services.AddSingleton<ProxySourceFactoryService>();
            services.AddSingleton(_ => new RuriLibSettingsService("UserData"));
            services.AddSingleton(_ => new GoldenBulletSettingsService("UserData"));
            services.AddSingleton(_ => new PluginRepository("UserData/Plugins"));
            services.AddSingleton<IRandomUAProvider>(_ => new IntoliRandomUAProvider("user-agents.json"));
            services.AddSingleton<IRNGProvider, DefaultRNGProvider>();
            services.AddSingleton<MemoryJobLogger>();
            services.AddSingleton<IJobLogger>(service =>
                new FileJobLogger(service.GetService<RuriLibSettingsService>(),
                "UserData/Logs/Jobs"));
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Load license and save it for Home page to read
            var license = Core.Services.Validate.GetLicenseInfo();
            var vms = SP.GetService<ViewModelsService>();
            vms.License = license;

            var mainWindow = serviceProvider.GetService<MainWindow>();
            mainWindow.NavigateTo(MainWindowPage.Home);
            mainWindow.Show();

            // ==========================================
            // SPLASH SCREEN CLOSE
            // ==========================================
            if (splashDispatcher != null && !splashDispatcher.HasShutdownStarted)
            {
                splashDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
            // ==========================================
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ReportCrash(e.Exception);
            e.Handled = true; // Set to false to close the app on exception
        }

        private void OnTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved(); // Comment this line to close the app on task exception

            // I decided to disable the code below since usually task exceptions are not critical to the application

            /*
            if (e.Exception.InnerException is not null)
            {
                if (e.Exception.InnerException is PuppeteerSharp.PuppeteerException // https://github.com/hardkoded/puppeteer-sharp/issues/891
                    or System.Net.Sockets.SocketException // Seems like all networking-related things can cause unhandled task exceptions
                    or TimeoutException) // This is again thrown by Puppeteer
                {
                    return;
                }
            }

            ReportCrash(e.Exception);
            */
        }

        private static void ReportCrash(Exception ex)
        {
            File.WriteAllText("crash.log", $"Unhandled exception thrown on {DateTime.Now}\r\n{ex}");

            Alert.Error("Unhandled exception", $"An unhandled exception was thrown, the application will try to continue running." +
                $" Please open the crash.log file, copy the error message inside it and open an issue on the official github repository." +
                $" A few details about the exception: {ex.Message}");
        }
    }
}
