namespace Core.Models.Settings;

/// <summary>
/// Settings for the GoldenBullet application.
/// </summary>
public class GoldenBulletSettings
{
    /// <summary>
    /// General settings.
    /// </summary>
    public GeneralSettings GeneralSettings { get; set; } = new();

    /// <summary>
    /// Settings related to remote repositories.
    /// </summary>
    public RemoteSettings RemoteSettings { get; set; } = new();

    /// <summary>
    /// Settings related to security.
    /// </summary>
    public SecuritySettings SecuritySettings { get; set; } = new();

    /// <summary>
    /// Settings related to the appearance of the UI.
    /// </summary>
    public CustomizationSettings CustomizationSettings { get; set; } = new();
}
