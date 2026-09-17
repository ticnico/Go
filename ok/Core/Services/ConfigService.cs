using Core.Models.Settings;
using Core.Repositories;
using Microsoft.Scripting.Utils;
using RuriLib.Functions.Conversion;
using RuriLib.Helpers;
using RuriLib.Legacy.Configs;
using RuriLib.Models.Configs;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Core.Services;

/// <summary>
/// Manages the list of available configs.
/// </summary>
public class ConfigService
{
    /// <summary>
    /// The list of available configs.
    /// </summary>
    public List<Config> Configs { get; set; } = new();

    /// <summary>
    /// Called when a new config is selected.
    /// </summary>
    public event EventHandler<Config> OnConfigSelected;

    /// <summary>
    /// Called when all configs from configured remote endpoints are loaded.
    /// </summary>
    public event EventHandler OnRemotesLoaded;

    private Config selectedConfig = null;
    private readonly IConfigRepository configRepo;
    private readonly GoldenBulletSettingsService GoldenBulletSettingsService;

    /// <summary>
    /// The currently selected config.
    /// </summary>
    public Config SelectedConfig
    {
        get => selectedConfig;
        set
        {
            selectedConfig = value;
            OnConfigSelected?.Invoke(this, selectedConfig);
        }
    }

    public ConfigService(IConfigRepository configRepo, GoldenBulletSettingsService GoldenBulletSettingsService)
    {
        this.configRepo = configRepo;
        this.GoldenBulletSettingsService = GoldenBulletSettingsService;
    }

    /// <summary>
    /// Persists the provided config using the repository.
    /// </summary>
    public async Task SaveAsync(Config config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        await configRepo.SaveAsync(config).ConfigureAwait(false);
    }

    /// <summary>
    /// Imports a config from a file (.Tic, .obconfig, .json, .loli)
    /// Preserves original filename and avoids overwrites.
    /// </summary>
    public async Task<Config> ImportAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Config file not found", filePath);

        Config config;
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var originalName = Path.GetFileNameWithoutExtension(filePath);

        // Packed formats (.tic/.opk) - handled by CorePacker
        if (extension is ".tic" or ".opk")
        {
            using var fileStream = File.OpenRead(filePath);
            config = await CorePacker.UnpackAsync(fileStream);
            config.Metadata.Name = originalName; // ✅ Preserve filename
        }
        // JSON-like formats
        else if (extension is ".json" or ".obconfig")
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            config = JsonSerializer.Deserialize<Config>(json, options)
                ?? throw new InvalidDataException("Failed to deserialize config from JSON");
            config.Metadata.Name = originalName; // ✅ Preserve filename
        }
        // SVB / legacy script-like formats: import as Legacy (LoliScript)
        else if (extension is ".svb" or ".loli" or ".anom")
        {
            var text = await File.ReadAllTextAsync(filePath);

            // Try to convert using the legacy ConfigConverter which parses the [SETTINGS] block
            try
            {
                config = ConfigConverter.Convert(text, Guid.NewGuid().ToString("N"));
            }
            catch
            {
                // Fallback: keep only the [SCRIPT] section and everything after it (remove preceding [SETTINGS] block)
                var idx = text.IndexOf("[SCRIPT]", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    text = text.Substring(idx).TrimStart();
                }

                config = new Config
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Metadata = new ConfigMetadata { Name = originalName },
                    Mode = ConfigMode.Legacy,
                    LoliScript = text
                };
            }
        }
        else
        {
            throw new NotSupportedException($"Unsupported config file format: {extension}");
        }

        // ✅ Generate unique name to avoid overwrites
        config.Metadata.Name = GetUniqueConfigName(originalName);

        // ✅ Use the unique metadata name as the storage id so the imported file
        // keeps the original filename (the repository will sanitize it).
        config.Id = config.Metadata.Name;
        config.IsRemote = false;

        // ✅ Save to repository (uses Metadata.Name as filename)
        await configRepo.SaveAsync(config);

        // ✅ Add to in-memory list
        lock (Configs)
        {
            Configs.Add(config);
        }

        return config;
    }

    /// <summary>
    /// Generates a unique config name by appending _1, _2, etc. if needed.
    /// Thread-safe and case-insensitive.
    /// </summary>
    private string GetUniqueConfigName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Untitled";

        lock (Configs)
        {
            if (!Configs.Any(c => c.Metadata.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
                return baseName;

            string uniqueName;
            int counter = 1;
            do
            {
                uniqueName = $"{baseName}_{counter++}";
            }
            while (Configs.Any(c => c.Metadata.Name.Equals(uniqueName, StringComparison.OrdinalIgnoreCase)));

            return uniqueName;
        }
    }

    /// <summary>
    /// Reloads all configs from the repository and remote endpoints.
    /// </summary>
    public async Task ReloadConfigsAsync()
    {
        Configs = (await configRepo.GetAllAsync()).ToList();
        SelectedConfig = null;
        LoadFromRemotes();
    }

    private async void LoadFromRemotes()
    {
        List<Config> remoteConfigs = new();

        var func = new Func<RemoteConfigsEndpoint, Task>(async endpoint =>
        {
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("Api-Key", endpoint.ApiKey);
                using var response = await client.GetAsync(endpoint.Url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new UnauthorizedAccessException();
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileNotFoundException();

                var fileStream = await response.Content.ReadAsStreamAsync();

                using ZipArchive archive = new(fileStream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".Tic")) continue;

                    try
                    {
                        using var entryStream = entry.Open();
                        var config = await CorePacker.UnpackAsync(entryStream);
                        config.Id = HexConverter.ToHexString(config.Metadata.GetUniqueHash());
                        config.IsRemote = true;

                        if (!remoteConfigs.Any(c => c.Id == config.Id))
                            remoteConfigs.Add(config);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{endpoint.Url}] Failed to pull configs: {ex.Message}");
            }
        });

        var tasks = GoldenBulletSettingsService.Settings.RemoteSettings.ConfigsEndpoints
            .Select(endpoint => func.Invoke(endpoint));

        await Task.WhenAll(tasks).ConfigureAwait(false);

        lock (Configs)
        {
            Configs.AddRange(remoteConfigs);
        }

        OnRemotesLoaded?.Invoke(this, EventArgs.Empty);
    }
}
