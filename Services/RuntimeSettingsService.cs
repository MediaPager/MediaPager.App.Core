using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Runtime settings live in the auth database so the SPA and the desktop launcher share
/// one source of truth. Configuration (appsettings/user-secrets/env vars) remains the fallback.
/// The database location itself always comes from configuration, never from this store.
/// </summary>
namespace MediaPager.App.Core.Services;

public interface IRuntimeSettings
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SetAllAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
}

public sealed class RuntimeSettingsService(AuthDbContext database, IConfiguration configuration) : IRuntimeSettings
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var stored = await database.RuntimeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrWhiteSpace(stored) ? stored : configuration[key];
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var knownKey in RuntimeSettingKeys.All)
        {
            var value = configuration[knownKey];
            if (!string.IsNullOrWhiteSpace(value)) values[knownKey] = value;
        }

        await foreach (var setting in database.RuntimeSettings.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (RuntimeSettingKeys.All.Contains(setting.Key) && !string.IsNullOrWhiteSpace(setting.Value))
                values[setting.Key] = setting.Value;
        }

        return values;
    }

    public async Task SetAllAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        foreach (var (key, value) in values)
        {
            if (!RuntimeSettingKeys.All.Contains(key)) continue;

            var existing = await database.RuntimeSettings.FindAsync([key], cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing is not null) database.RuntimeSettings.Remove(existing);
            }
            else if (existing is not null)
            {
                existing.Value = value;
            }
            else
            {
                database.RuntimeSettings.Add(new RuntimeSetting { Key = key, Value = value });
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Settings that may be stored in the DB and edited at runtime. DB location is intentionally
/// excluded. Provider credentials (TMDB/OpenSubtitles/Subdl/Mailgun/...) are NOT here — they
/// are plugin settings under plugins.&lt;key&gt;.* (see IPluginSettingsStore); these are the
/// host-level keys: active-provider selectors, artwork directory, and site URL.
/// </summary>
public static class RuntimeSettingKeys
{
    public const string EmailProvider = "Email:Provider";
    public const string SubtitlesProvider = "Subtitles:Provider";
    public const string ArtworkDirectory = "Artwork:Directory";
    public const string FrontendBaseUrl = "Frontend:BaseUrl";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        EmailProvider,
        SubtitlesProvider,
        ArtworkDirectory,
        FrontendBaseUrl,
    };
}
