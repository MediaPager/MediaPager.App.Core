using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPager.App.Core.Services;

/// <summary>
/// Plugin values are namespaced by full descriptor id under Settings:Plugins. Legacy
/// plugins.<short-key>.* rows/configuration are read as migration fallbacks. Every DB value
/// is protected at rest; plugins receive plaintext only through their bound SDK settings view.
/// </summary>
public sealed class PluginSettingsService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IPluginSettingsValueProtector protector) : IPluginSettingsRepository
{
    public const string KeyPrefix = "Settings:Plugins:";
    private const string LegacyKeyPrefix = "plugins.";
    private const string ProtectedPrefix = "enc:v1:";

    public async Task<string?> GetAsync(string pluginId, string name, CancellationToken cancellationToken = default)
    {
        Validate(pluginId, name);
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var key = ComposeKey(pluginId, name);

        var environmentValue = SettingsConfigurationKeys.ReadEnvironmentValue(key)
            ?? Environment.GetEnvironmentVariable($"MEDIAPAGER_plugins__{pluginId.Split('.')[^1]}__{name}");
        if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue;

        var stored = await database.RuntimeSettings.AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored)) return UnprotectIfNeeded(stored);

        var legacyKey = ComposeLegacyKey(pluginId, name);
        stored = await database.RuntimeSettings.AsNoTracking()
            .Where(setting => setting.Key == legacyKey)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored)) return UnprotectIfNeeded(stored);

        var configured = configuration[key];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        return configuration[legacyKey.Replace('.', ':')];
    }

    public async Task SetAsync(string pluginId, string name, string? value, CancellationToken cancellationToken = default)
    {
        Validate(pluginId, name);
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var key = ComposeKey(pluginId, name);
        var setting = await database.RuntimeSettings.FindAsync([key], cancellationToken);
        var legacy = await database.RuntimeSettings.FindAsync([ComposeLegacyKey(pluginId, name)], cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (setting is not null) database.RuntimeSettings.Remove(setting);
            if (legacy is not null) database.RuntimeSettings.Remove(legacy);
        }
        else
        {
            var protectedValue = ProtectedPrefix + protector.Protect(value);
            if (setting is null)
                database.RuntimeSettings.Add(new RuntimeSetting { Key = key, Value = protectedValue });
            else
                setting.Value = protectedValue;
            if (legacy is not null) database.RuntimeSettings.Remove(legacy);
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        Validate(pluginId, "placeholder");
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicalPrefix = ComposeKey(pluginId, "");
        await foreach (var setting in database.RuntimeSettings.AsNoTracking()
            .Where(entry => entry.Key.StartsWith(canonicalPrefix))
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(setting.Value))
                values[setting.Key[canonicalPrefix.Length..]] = UnprotectIfNeeded(setting.Value);
        }

        var legacyPrefix = ComposeLegacyKey(pluginId, "");
        await foreach (var setting in database.RuntimeSettings.AsNoTracking()
            .Where(entry => entry.Key.StartsWith(legacyPrefix))
            .AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            var field = setting.Key[legacyPrefix.Length..];
            if (!values.ContainsKey(field) && !string.IsNullOrWhiteSpace(setting.Value))
                values[field] = UnprotectIfNeeded(setting.Value);
        }

        var configuredPrefix = ComposeKey(pluginId, "");
        foreach (var pair in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || !pair.Key.StartsWith(configuredPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var field = pair.Key[configuredPrefix.Length..];
            if (field.Length == 0) continue;
            var environmentValue = SettingsConfigurationKeys.ReadEnvironmentValue(ComposeKey(pluginId, field));
            if (!string.IsNullOrWhiteSpace(environmentValue)) values[field] = environmentValue;
            else if (!values.ContainsKey(field)) values[field] = pair.Value;
        }
        var oldConfiguredPrefix = legacyPrefix.Replace('.', ':');
        foreach (var pair in configuration.AsEnumerable())
        {
            if (string.IsNullOrWhiteSpace(pair.Value) || !pair.Key.StartsWith(oldConfiguredPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var field = pair.Key[oldConfiguredPrefix.Length..].TrimStart(':');
            if (field.Length == 0) continue;
            var environmentValue = Environment.GetEnvironmentVariable($"MEDIAPAGER_plugins__{pluginId.Split('.')[^1]}__{field}");
            if (!string.IsNullOrWhiteSpace(environmentValue)) values[field] = environmentValue;
            else if (!values.ContainsKey(field)) values[field] = pair.Value;
        }
        return values;
    }

    public async Task SeedSchemaDefaultsAsync(
        IMediaPagerPlugin plugin,
        IPluginSettingsSchema schema,
        CancellationToken cancellationToken = default)
    {
        foreach (var field in schema.Settings)
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var existing = await database.RuntimeSettings.AsNoTracking()
                .AnyAsync(setting => setting.Key == ComposeKey(plugin.Descriptor.Id, field.Key), cancellationToken);
            if (existing) continue;
            var value = await GetAsync(plugin.Descriptor.Id, field.Key, cancellationToken);
            if (string.IsNullOrWhiteSpace(value)) value = field.Default;
            if (!string.IsNullOrWhiteSpace(value))
                await SetAsync(plugin.Descriptor.Id, field.Key, value, cancellationToken);
        }
    }

    private string UnprotectIfNeeded(string value) => value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)
        ? protector.Unprotect(value[ProtectedPrefix.Length..])
        : value;

    private static string ComposeKey(string pluginId, string name) =>
        string.IsNullOrEmpty(name) ? $"{KeyPrefix}{pluginId}:" : $"{KeyPrefix}{pluginId}:{name}";

    private static string ComposeLegacyKey(string pluginId, string name)
    {
        var shortKey = pluginId.Split('.')[^1];
        return string.IsNullOrEmpty(name) ? $"{LegacyKeyPrefix}{shortKey}." : $"{LegacyKeyPrefix}{shortKey}.{name}";
    }

    private static void Validate(string pluginId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (pluginId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')) ||
            name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("The plugin id or setting name contains unsupported characters.");
    }
}
