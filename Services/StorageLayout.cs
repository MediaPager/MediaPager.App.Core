using Microsoft.Extensions.Configuration;

namespace MediaPager.App.Core.Services;

/// <summary>
/// Resolves app-owned storage locations once from the Settings:Storage configuration tree.
/// Relative paths are rooted below DataRoot; explicit absolute paths remain independent.
/// </summary>
public sealed class StorageLayout
{
    private readonly IConfiguration _configuration;
    public const string RootKey = "Settings:Storage:Root";

    public StorageLayout(IConfiguration configuration)
    {
        _configuration = configuration;
        Refresh();
    }

    public void Refresh()
    {
        var configuration = _configuration;
        var configuredRoot = SettingsConfigurationKeys.ReadConfiguredValue(configuration, RootKey);
        DataRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? DefaultDataRoot
            : Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));

        DatabasePath = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, SettingsConfigurationKeys.DbPath), Path.Combine("db", "mediapager.db"));
        ArtworkPosterDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, SettingsConfigurationKeys.ArtworkPosterDirectory), Path.Combine("artwork", "posters"));
        ArtworkBackgroundDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, SettingsConfigurationKeys.ArtworkBackgroundDirectory), Path.Combine("artwork", "backgrounds"));
        ConfigDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Config:Directory"), "config");
        DataProtectionKeysDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Security:DataProtectionKeysDirectory"), Path.Combine("keys", "data-protection"));
        JwtSigningKeyPath = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Security:JwtSigningKeyPath"), Path.Combine("keys", "signing.key"));
        ToolsDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Tools:Directory"), "tools");
        CommunityPluginsDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Plugins:CommunityDirectory"), Path.Combine("plugins", "community"));
        InstalledOfficialPluginsDirectory = Resolve(SettingsConfigurationKeys.ReadConfiguredValue(configuration, "Settings:Storage:Plugins:InstalledOfficialDirectory"), Path.Combine("plugins", "official"));
    }

    public string DataRoot { get; private set; } = "";
    public string DatabasePath { get; private set; } = "";
    public string ArtworkPosterDirectory { get; private set; } = "";
    public string ArtworkBackgroundDirectory { get; private set; } = "";
    public string ConfigDirectory { get; private set; } = "";
    public string DataProtectionKeysDirectory { get; private set; } = "";
    public string JwtSigningKeyPath { get; private set; } = "";
    public string ToolsDirectory { get; private set; } = "";
    public string CommunityPluginsDirectory { get; private set; } = "";
    public string InstalledOfficialPluginsDirectory { get; private set; } = "";

    public string Resolve(string? path, string defaultRelativePath)
    {
        var candidate = string.IsNullOrWhiteSpace(path) ? defaultRelativePath : Environment.ExpandEnvironmentVariables(path.Trim());
        return Path.GetFullPath(Path.IsPathRooted(candidate) ? candidate : Path.Combine(DataRoot, candidate));
    }

    public static string DefaultDataRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(string.IsNullOrWhiteSpace(appData)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : appData, "MediaPager");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".MediaPager");
        }
    }
}
