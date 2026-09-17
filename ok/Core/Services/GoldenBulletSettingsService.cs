using Core.Models.Settings;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Core.Services;

/// <summary>
/// Provides interaction with settings of the GoldenBullet application.
/// </summary>
public class GoldenBulletSettingsService
{
    private string BaseFolder { get; }
    private readonly JsonSerializerSettings jsonSettings;

    /// <summary>
    /// The path of the file where settings are saved.
    /// </summary>
    public string FileName => Path.Combine(BaseFolder, "GoldenBulletSettings.json");

    /// <summary>
    /// The actual settings. After modifying them, call the <see cref="SaveAsync"/> method to persist them.
    /// </summary>
    public GoldenBulletSettings Settings { get; private set; }

    public GoldenBulletSettingsService(string baseFolder)
    {
        BaseFolder = baseFolder;
        Directory.CreateDirectory(baseFolder);

        jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto
        };

        if (File.Exists(FileName))
        {
            Settings = JsonConvert.DeserializeObject<GoldenBulletSettings>(File.ReadAllText(FileName), jsonSettings);
        }
        else
        {
            Recreate();
            SaveAsync().Wait();
        }
    }

    /// <summary>
    /// Saves the <see cref="Settings"/> to disk.
    /// </summary>
    public async Task SaveAsync() => await File.WriteAllTextAsync(FileName, JsonConvert.SerializeObject(Settings, jsonSettings));

    /// <summary>
    /// Restores the default <see cref="Settings"/> (does not save to disk).
    /// </summary>
    public void Recreate() => Settings = new GoldenBulletSettings
    {
        GeneralSettings = new GeneralSettings { ProxyCheckTargets = new List<ProxyCheckTarget> { new ProxyCheckTarget() } },
        RemoteSettings = new RemoteSettings(),
        SecuritySettings = new SecuritySettings().GenerateJwtKey().SetupAdminPassword("admin"),
        CustomizationSettings = new CustomizationSettings()
    };
}
