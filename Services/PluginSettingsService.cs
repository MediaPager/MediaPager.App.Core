using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPager.App.Core.Services;

/// <summary>
/// IPluginSettingsStore backed by the runtime settings DB under the "plugins." key prefix.
/// Bypasses the IRuntimeSettings whitelist (plugin keys are free-form), opening a scope per
/// call so a singleton can safely use the scoped AuthDbContext. Config values in the
/// "plugins:*" shape are the fallback.
/// </summary>
public sealed class PluginSettingsService(IServiceScopeFactory scopeFactory) : IPluginSettingsStore
{
    public const string KeyPrefix = "plugins.";

    public async Task<string?> GetAsync(string pluginKey, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = ComposeKey(pluginKey, name);
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<AuthDbContext>();
        var stored = await database.RuntimeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored;
        return services.GetRequiredService<IConfiguration>()[key.Replace('.', ':')];
    }

    public async Task SetAsync(string pluginKey, string name, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = ComposeKey(pluginKey, name);
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var existing = await database.RuntimeSettings.FindAsync([key], cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null)
            {
                database.RuntimeSettings.Remove(existing);
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        else if (existing is not null)
        {
            existing.Value = value;
            await database.SaveChangesAsync(cancellationToken);
        }
        else
        {
            database.RuntimeSettings.Add(new RuntimeSetting { Key = key, Value = value });
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginKey);
        var prefix = ComposeKey(pluginKey, "");
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var setting in database.RuntimeSettings.AsNoTracking()
            .Where(entry => entry.Key.StartsWith(prefix))
            .AsAsyncEnumerable()
            .WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(setting.Value))
                values[setting.Key[prefix.Length..]] = setting.Value;
        }

        return values;
    }

    private static string ComposeKey(string pluginKey, string name) =>
        string.IsNullOrEmpty(name) ? $"{KeyPrefix}{pluginKey}." : $"{KeyPrefix}{pluginKey}.{name}";
}