using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Data.Common;

/// <summary>
/// Runtime settings live in the auth database so the SPA and the desktop launcher share
/// one source of truth. Configuration remains the fallback. Database connection details are
/// bootstrap configuration and are deliberately excluded from this store.
/// </summary>
namespace MediaPager.App.Core.Services;

public interface IRuntimeSettings
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SetAllAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);
    Task SeedMissingFromConfigurationAsync(CancellationToken cancellationToken = default);
    Task RemoveDatabaseSettingsAsync(CancellationToken cancellationToken = default);
}

public sealed class RuntimeSettingsService(AuthDbContext database, IConfiguration configuration) : IRuntimeSettings
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (RuntimeSettingKeys.DatabaseConfigurationKeys.Contains(key))
            return SettingsConfigurationKeys.ReadConfiguredValue(configuration, key);

        // The site URL is user-editable runtime state. Honor a saved value first; deployment
        // configuration is its fallback, as described by the Settings UI.
        if (key == SettingsConfigurationKeys.SiteBaseUrl)
        {
            var savedSiteUrl = await database.RuntimeSettings
                .AsNoTracking()
                .Where(setting => setting.Key == key)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(savedSiteUrl)) return savedSiteUrl;
            return SettingsConfigurationKeys.ReadConfiguredValue(configuration, key);
        }

        var environment = SettingsConfigurationKeys.ReadEnvironmentValue(key);
        if (!string.IsNullOrWhiteSpace(environment)) return environment;

        var stored = await database.RuntimeSettings
            .AsNoTracking()
            .Where(setting => setting.Key == key)
            .Select(setting => setting.Value)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stored)) return stored;

        var configured = configuration[key];
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        foreach (var alias in SettingsConfigurationKeys.LegacyAliases(key))
        {
            var legacy = await database.RuntimeSettings.AsNoTracking()
                .Where(setting => setting.Key == alias)
                .Select(setting => setting.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(legacy)) return legacy;
        }

        return SettingsConfigurationKeys.ReadConfiguredValue(configuration, key);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var knownKey in RuntimeSettingKeys.All)
        {
            var value = await GetAsync(knownKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(value)) values[knownKey] = value;
        }

        await foreach (var setting in database.RuntimeSettings.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (RuntimeSettingKeys.All.Contains(setting.Key) && !string.IsNullOrWhiteSpace(setting.Value) &&
                string.IsNullOrWhiteSpace(SettingsConfigurationKeys.ReadEnvironmentValue(setting.Key)))
                values[setting.Key] = setting.Value;
        }

        return values;
    }

    public async Task SeedMissingFromConfigurationAsync(CancellationToken cancellationToken = default)
    {
        foreach (var key in RuntimeSettingKeys.All)
        {
            // Keep the site URL as a fallback unless the user explicitly saves an override.
            // Persisting the default here would make it look like an override on later boots.
            if (key == SettingsConfigurationKeys.SiteBaseUrl) continue;

            var existing = await database.RuntimeSettings.FindAsync([key], cancellationToken);
            if (existing is not null) continue;
            var value = await GetAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(value)) continue;
            database.RuntimeSettings.Add(new RuntimeSetting { Key = key, Value = value.Trim() });
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveDatabaseSettingsAsync(CancellationToken cancellationToken = default)
    {
        var keys = RuntimeSettingKeys.DatabaseConfigurationKeys
            .Concat(["Database:Provider", "Auth:DatabasePath", "ConnectionStrings:AuthDatabase"])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stored = await database.RuntimeSettings
            .Where(setting => keys.Contains(setting.Key))
            .ToListAsync(cancellationToken);
        if (stored.Count == 0) return;
        database.RuntimeSettings.RemoveRange(stored);
        await database.SaveChangesAsync(cancellationToken);
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
/// Settings that may be stored in the DB and edited at runtime. Database connection details
/// are configuration-only and listed separately below, never included in <see cref="All"/>.
/// Provider credentials (TMDB/OpenSubtitles/Subdl/Mailgun/...) are NOT here — they
/// are plugin settings under plugins.&lt;key&gt;.* (see IPluginSettingsStore); these are the
/// host-level keys: active-provider selectors, artwork directories, and site URL.
/// </summary>
public static class RuntimeSettingKeys
{
    public const string EmailProvider = SettingsConfigurationKeys.EmailProvider;
    public const string SubtitlesProvider = SettingsConfigurationKeys.SubtitlesProvider;
    public const string ArtworkDirectory = SettingsConfigurationKeys.ArtworkDirectoryLegacy;
    public const string ArtworkPosterDirectory = SettingsConfigurationKeys.ArtworkPosterDirectory;
    public const string ArtworkBackgroundDirectory = SettingsConfigurationKeys.ArtworkBackgroundDirectory;
    public const string FrontendBaseUrl = SettingsConfigurationKeys.SiteBaseUrl;
    public const string DatabaseProvider = SettingsConfigurationKeys.DbProvider;
    public const string DatabasePath = SettingsConfigurationKeys.DbPath;
    public const string DatabaseHost = SettingsConfigurationKeys.DbHost;
    public const string DatabasePort = SettingsConfigurationKeys.DbPort;
    public const string DatabaseCatalog = SettingsConfigurationKeys.DbCatalog;
    public const string DatabaseUser = SettingsConfigurationKeys.DbUser;
    public const string DatabasePassword = SettingsConfigurationKeys.DbPassword;
    public const string DatabaseConnectionString = SettingsConfigurationKeys.DbConnectionString;
    public const string DatabaseSslMode = SettingsConfigurationKeys.DbSslMode;
    public const string DatabaseConnectionTimeout = SettingsConfigurationKeys.DbConnectionTimeout;
    public const string ScopeSecurityPath = SettingsConfigurationKeys.AppScopeSecurityPath;
    public const string StorageRoot = StorageLayout.RootKey;
    public const string ToolsDirectory = SettingsConfigurationKeys.ToolsDirectory;
    public const string CommunityPluginsDirectory = SettingsConfigurationKeys.CommunityPluginsDirectory;
    public const string InstalledOfficialPluginsDirectory = SettingsConfigurationKeys.InstalledOfficialPluginsDirectory;
    public const string ConfigDirectory = SettingsConfigurationKeys.ConfigDirectory;
    public const string DataProtectionKeysDirectory = SettingsConfigurationKeys.DataProtectionKeysDirectory;
    public const string JwtSigningKeyPath = SettingsConfigurationKeys.JwtSigningKeyPath;

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        EmailProvider,
        SubtitlesProvider,
        ArtworkDirectory,
        ArtworkPosterDirectory,
        ArtworkBackgroundDirectory,
        FrontendBaseUrl,
        StorageRoot,
        ToolsDirectory,
        CommunityPluginsDirectory,
        InstalledOfficialPluginsDirectory,
    };

    public static readonly IReadOnlySet<string> DatabaseConfigurationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        DatabaseProvider,
        DatabasePath,
        DatabaseHost,
        DatabasePort,
        DatabaseCatalog,
        DatabaseUser,
        DatabasePassword,
        DatabaseSslMode,
        DatabaseConnectionTimeout,
        DatabaseConnectionString,
    };
}

public static class SettingsConfigurationKeys
{
    public const string AppIssuer = "Settings:App:Issuer";
    public const string DbProvider = "Settings:Db:Provider";
    public const string DbPath = "Settings:Db:Path";
    public const string DbHost = "Settings:Db:Host";
    public const string DbPort = "Settings:Db:Port";
    public const string DbCatalog = "Settings:Db:Catalog";
    public const string DbUser = "Settings:Db:User";
    public const string DbPassword = "Settings:Db:Password";
    public const string DbConnectionString = "Settings:Db:ConnectionString";
    public const string DbSslMode = "Settings:Db:SslMode";
    public const string DbConnectionTimeout = "Settings:Db:ConnectionTimeoutSeconds";
    public const string AppScopeSecurityPath = "Settings:App:ScopeSecurityPath";
    public const string SiteBaseUrl = "Settings:Site:BaseUrl";
    public const string EmailProvider = "Settings:Email:Provider";
    public const string SubtitlesProvider = "Settings:Subtitles:Provider";
    public const string ArtworkDirectoryLegacy = "Artwork:Directory";
    public const string ArtworkPosterDirectory = "Settings:Storage:Artwork:PosterDirectory";
    public const string ArtworkBackgroundDirectory = "Settings:Storage:Artwork:BackgroundDirectory";
    public const string ToolsDirectory = "Settings:Storage:Tools:Directory";
    public const string CommunityPluginsDirectory = "Settings:Storage:Plugins:CommunityDirectory";
    public const string InstalledOfficialPluginsDirectory = "Settings:Storage:Plugins:InstalledOfficialDirectory";
    public const string ConfigDirectory = "Settings:Storage:Config:Directory";
    public const string DataProtectionKeysDirectory = "Settings:Storage:Security:DataProtectionKeysDirectory";
    public const string JwtSigningKeyPath = "Settings:Storage:Security:JwtSigningKeyPath";

    public static string? ReadEnvironmentValue(string key)
    {
        var direct = Environment.GetEnvironmentVariable("MEDIAPAGER_" + string.Join("__", key.Split(':')));
        if (!string.IsNullOrWhiteSpace(direct)) return direct;
        var legacy = key switch
        {
            AppIssuer => Environment.GetEnvironmentVariable("MEDIAPAGER_Auth__Issuer"),
            AppScopeSecurityPath => Environment.GetEnvironmentVariable("MEDIAPAGER_Auth__ScopeSecurityPath"),
            DbProvider => Environment.GetEnvironmentVariable("MEDIAPAGER_Database__Provider"),
            DbPath => Environment.GetEnvironmentVariable("MEDIAPAGER_DB_PATH") ?? Environment.GetEnvironmentVariable("MEDIAPAGER_Auth__DatabasePath"),
            SiteBaseUrl => Environment.GetEnvironmentVariable("MEDIAPAGER_Frontend__BaseUrl"),
            EmailProvider => Environment.GetEnvironmentVariable("MEDIAPAGER_Email__Provider"),
            SubtitlesProvider => Environment.GetEnvironmentVariable("MEDIAPAGER_Subtitles__Provider"),
            ArtworkPosterDirectory or ArtworkBackgroundDirectory => Environment.GetEnvironmentVariable("MEDIAPAGER_Artwork__Directory"),
            ToolsDirectory => Environment.GetEnvironmentVariable("MEDIAPAGER_Tools__Directory"),
            CommunityPluginsDirectory => LegacyPluginDirectory("community"),
            InstalledOfficialPluginsDirectory => LegacyPluginDirectory("official"),
            DbHost or DbPort or DbCatalog or DbUser or DbPassword => LegacyPostgresConnectionValue(key),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
    }

    public static string? ReadConfiguredValue(IConfiguration configuration, string key)
    {
        var environment = ReadEnvironmentValue(key);
        if (!string.IsNullOrWhiteSpace(environment)) return environment;
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value)) return value;
        if (key == ToolsDirectory)
        {
            value = configuration["Tools:Directory"];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        if (key is CommunityPluginsDirectory or InstalledOfficialPluginsDirectory)
        {
            var legacyRoot = configuration["Plugins:Directory"];
            if (!string.IsNullOrWhiteSpace(legacyRoot))
                return Path.Combine(legacyRoot, key == CommunityPluginsDirectory ? "community" : "official");
        }
        foreach (var alias in LegacyAliases(key))
        {
            value = configuration[alias];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        value = LegacyPostgresConnectionValue(
            configuration["Settings:Db:ConnectionString"] ?? configuration.GetConnectionString("AuthDatabase"), key);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        if (key == DbPath && !string.Equals(
                ReadConfiguredValue(configuration, DbProvider), "Sqlite", StringComparison.OrdinalIgnoreCase))
            return null;
        if (key == DbPath)
        {
            value = DatabasePathFromConnectionString(configuration["Settings:Db:ConnectionString"]
                ?? configuration.GetConnectionString("AuthDatabase"));
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        if (key is ArtworkPosterDirectory or ArtworkBackgroundDirectory)
        {
            value = configuration[ArtworkDirectoryLegacy];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return DefaultValue(configuration, key);
    }

    private static string? DefaultValue(IConfiguration configuration, string key)
    {
        var configuredRoot = ReadEnvironmentValue(StorageLayout.RootKey) ?? configuration[StorageLayout.RootKey];
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot)
            ? StorageLayout.DefaultDataRoot
            : Environment.ExpandEnvironmentVariables(configuredRoot.Trim()));
        return key switch
        {
            DbProvider => "Sqlite",
            DbPath => Path.Combine(root, "db", "mediapager.db"),
            DbPort => "5432",
            DbCatalog => "mediapager",
            DbSslMode => "Prefer",
            DbConnectionTimeout => "15",
            AppScopeSecurityPath => Path.Combine(root, "config", "scope-security.json"),
            SiteBaseUrl => "http://localhost:5173",
            StorageLayout.RootKey => root,
            ArtworkPosterDirectory => Path.Combine(root, "artwork", "posters"),
            ArtworkBackgroundDirectory => Path.Combine(root, "artwork", "backgrounds"),
            ToolsDirectory => Path.Combine(root, "tools"),
            CommunityPluginsDirectory => Path.Combine(root, "plugins", "community"),
            InstalledOfficialPluginsDirectory => Path.Combine(root, "plugins", "official"),
            ConfigDirectory => Path.Combine(root, "config"),
            DataProtectionKeysDirectory => Path.Combine(root, "keys", "data-protection"),
            JwtSigningKeyPath => Path.Combine(root, "keys", "signing.key"),
            _ => null,
        };
    }

    private static string? LegacyPluginDirectory(string child)
    {
        var root = Environment.GetEnvironmentVariable("MEDIAPAGER_Plugins__Directory");
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, child);
    }

    private static string? LegacyPostgresConnectionValue(string key) =>
        LegacyPostgresConnectionValue(Environment.GetEnvironmentVariable("MEDIAPAGER_ConnectionStrings__AuthDatabase"), key);

    private static string? LegacyPostgresConnectionValue(string? connectionString, string key)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var aliases = key switch
            {
                DbHost => new[] { "Host", "Server" },
                DbPort => new[] { "Port" },
                DbCatalog => new[] { "Database", "Initial Catalog" },
                DbUser => new[] { "Username", "User ID", "User Id" },
                DbPassword => new[] { "Password" },
                _ => [],
            };
            foreach (var alias in aliases)
            {
                if (parsed.TryGetValue(alias, out var value)) return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? DatabasePathFromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
            return parsed.TryGetValue("Data Source", out var value) || parsed.TryGetValue("DataSource", out value)
                ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> LegacyAliases(string key) => key switch
    {
        AppIssuer => ["Auth:Issuer"],
        DbProvider => ["Database:Provider"],
        DbPath => ["Auth:DatabasePath"],
        DbHost => [],
        DbPort => [],
        DbCatalog => [],
        DbUser => [],
        DbPassword => [],
        DbSslMode => [],
        DbConnectionTimeout => [],
        AppScopeSecurityPath => ["Auth:ScopeSecurityPath"],
        SiteBaseUrl => ["Frontend:BaseUrl"],
        EmailProvider => ["Email:Provider"],
        SubtitlesProvider => ["Subtitles:Provider"],
        ArtworkPosterDirectory or ArtworkBackgroundDirectory => [ArtworkDirectoryLegacy],
        ToolsDirectory => ["Tools:Directory"],
        _ => [],
    };
}
