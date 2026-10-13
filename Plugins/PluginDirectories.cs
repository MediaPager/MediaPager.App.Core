using Microsoft.Extensions.Configuration;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// The two plugin install directories under the plugins root (Plugins:Directory, default
/// ~/.MediaPager/plugins): <c>official</c> (shipped — deployed there at build — or installed
/// on demand from the official list) and <c>community</c> (installed from the recognized
/// community list). Boot scans both; the directory is the load origin for official vs
/// community tagging.
/// </summary>
public static class PluginDirectories
{
    public static string Root(IConfiguration configuration) =>
        Path.Combine(DataRoot(configuration), "plugins");

    /// <summary>Writable official installs. Packaged official assemblies are scanned separately.</summary>
    public static string Official(IConfiguration configuration) => Resolve(
        configuration,
        SettingsConfigurationKeys.ReadConfiguredValue(configuration, SettingsConfigurationKeys.InstalledOfficialPluginsDirectory),
        Path.Combine(Root(configuration), "official"),
        configuration["Plugins:Directory"] is { Length: > 0 } legacy ? Path.Combine(legacy, "official") : null);

    public static string BundledOfficial(IConfiguration configuration) =>
        Resolve(configuration, configuration["Settings:Storage:Plugins:BundledOfficialDirectory"],
            Path.Combine(AppContext.BaseDirectory, "plugins", "official"), null);

    public static string Community(IConfiguration configuration) => Resolve(
        configuration,
        SettingsConfigurationKeys.ReadConfiguredValue(configuration, SettingsConfigurationKeys.CommunityPluginsDirectory),
        Path.Combine(Root(configuration), "community"),
        configuration["Plugins:Directory"] is { Length: > 0 } legacy ? Path.Combine(legacy, "community") : null);

    private static string DataRoot(IConfiguration configuration)
    {
        var configured = configuration[StorageLayout.RootKey];
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
        return StorageLayout.DefaultDataRoot;
    }

    private static string Resolve(IConfiguration configuration, string? configured, string defaultPath, string? legacyPath)
    {
        var value = !string.IsNullOrWhiteSpace(configured) ? configured :
            !string.IsNullOrWhiteSpace(legacyPath) ? legacyPath : defaultPath;
        value = Environment.ExpandEnvironmentVariables(value);
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(DataRoot(configuration), value));
    }
}
