namespace MediaPager.App.Core.Services;

/// <summary>Host-only storage interface. API/controller code supplies the full plugin ID;
/// plugin code receives only a plugin-bound <see cref="MediaPager.App.PluginContracts.IPluginSettingsStore"/>.</summary>
public interface IPluginSettingsRepository
{
    Task<string?> GetAsync(string pluginId, string name, CancellationToken cancellationToken = default);

    Task SetAsync(string pluginId, string name, string? value, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginId, CancellationToken cancellationToken = default);

    Task SeedSchemaDefaultsAsync(IMediaPagerPlugin plugin, IPluginSettingsSchema schema, CancellationToken cancellationToken = default);
}

public interface IPluginSettingsValueProtector
{
    string Protect(string value);
    string Unprotect(string value);
}
