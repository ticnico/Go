using GoldenBullet.ViewModels;
using RuriLib.Services;

namespace GoldenBullet.Services
{
    public class ViewModelsService
    {
        public JobsViewModel Jobs { get; set; }
        public ProxiesViewModel Proxies { get; set; }
        public WordlistsViewModel Wordlists { get; set; }
        public ConfigsViewModel Configs { get; set; }
        public HitsViewModel Hits { get; set; }
        public GBSettingsViewModel GBSettings { get; set; }
        public RLSettingsViewModel RLSettings { get; set; }
        public PluginsViewModel Plugins { get; set; }

        public ConfigMetadataViewModel ConfigMetadata { get; set; }
        public ConfigReadmeViewModel ConfigReadme { get; set; }
        public ConfigStackerViewModel ConfigStacker { get; set; }
        public ConfigSettingsViewModel ConfigSettings { get; set; }

        public DebuggerViewModel Debugger { get; set; }

        public pipo License { get; set; }

        // 1. Inject RuriLibSettingsService into the constructor
        public ViewModelsService(RuriLibSettingsService ruriLibSettingsService)
        {
            Jobs = new();
            Proxies = new();
            Wordlists = new();
            Configs = new();
            Hits = new();
            GBSettings = new();

            // 2. Pass the service to the RLSettingsViewModel constructor
            RLSettings = new RLSettingsViewModel(ruriLibSettingsService);

            Plugins = new();

            ConfigMetadata = new();
            ConfigReadme = new();
            ConfigStacker = new();
            ConfigSettings = new();

            Debugger = new();

            _ = Proxies.InitializeAsync();
            _ = Wordlists.InitializeAsync();
            _ = Hits.InitializeAsync();
        }
    }
}
