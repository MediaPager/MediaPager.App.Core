namespace MediaPager.App.Core.Services;

/// <summary>Host-only storage interface. API/controller code supplies the namespace; plugin
/// code receives only a plugin-bound <see cref="MediaPager.App.PluginContracts.IPluginSettingsStore"/>.</summary>
public interface IPluginSettingsRepository
{
    Task<string?> GetAsync(string pluginKey, string name, CancellationToken cancellationToken = default);

    Task SetAsync(string pluginKey, string name, string? value, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginKey, CancellationToken cancellationToken = default);
}
