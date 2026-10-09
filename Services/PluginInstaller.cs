using MediaPager.App.Core.Plugins;
using Microsoft.Extensions.Configuration;

namespace MediaPager.App.Core.Services;

public sealed record PluginInstallResult(
    bool Found,
    string Id,
    string? Assembly = null,
    string? Path = null,
    IReadOnlyList<string> Loaded = default!);

/// <summary>
/// Runs the shared install pipeline for a listed plugin — official list → official
/// directory, recognized community list → community directory (clone → publish → copy the
/// dll, same layout as the build-time official deploy) — then registers it live. Singleton:
/// its injected IServiceProvider is the root provider, the same one boot-time plugin
/// construction uses, so plugins built here can never capture a request-scoped dependency.
/// </summary>
public sealed class PluginInstaller(
    IServiceProvider services,
    PluginRegistry registry,
    PluginDeployer deployer,
    IConfiguration configuration)
{
    public async Task<PluginInstallResult> InstallAsync(string key, CancellationToken cancellationToken = default)
    {
        var officialEntries = PluginCatalog.ReadOfficial(configuration);
        var official = officialEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, key, StringComparison.OrdinalIgnoreCase));
        var community = official is null
            ? PluginCatalog.ReadCommunity(configuration).FirstOrDefault(entry =>
                string.Equals(entry.Id, key, StringComparison.OrdinalIgnoreCase))
            : null;
        if (official is null && community is null)
            return new PluginInstallResult(Found: false, Id: key);

        var repo = official?.Repo ?? community!.Repo;
        var assembly = official?.Assembly ?? community?.Assembly ?? DeriveAssemblyName(repo);
        var directory = official is not null
            ? PluginDirectories.Official(configuration)
            : PluginDirectories.Community(configuration);

        var outcome = await deployer.InstallAsync(repo, assembly, directory, cancellationToken: cancellationToken);
        var destination = outcome.Path;

        // Reserve officials, then load just the installed folder and register it live
        // (already-loaded ids are skipped, so a reinstall is a no-op until a restart).
        var reservedIds = new HashSet<string>(
            officialEntries.Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var loaded in registry.Plugins)
            reservedIds.Add(loaded.Descriptor.Id);
        var newPlugins = PluginLoader.LoadFolder(
            services, Path.Combine(directory, assembly), official is not null, reservedIds);
        foreach (var plugin in newPlugins)
            registry.Add(plugin, community: official is null);

        return new PluginInstallResult(
            Found: true,
            Id: key,
            Assembly: assembly,
            Path: destination,
            Loaded: newPlugins.Select(plugin => plugin.Descriptor.Id).ToList());
    }

    // Derive the assembly/project name from the final repository path segment when a list
    // entry does not specify an assembly explicitly.
    public static string DeriveAssemblyName(string repo)
    {
        var name = new Uri(repo).AbsolutePath.TrimEnd('/').Split('/').Last();
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name;
    }
}
