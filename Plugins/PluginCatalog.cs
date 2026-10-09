using Microsoft.Extensions.Configuration;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// One entry of <c>plugins.official.json</c> (Plugins:List:Official) — the source of truth
/// for what counts as an official plugin. The same list drives reserved-id enforcement at
/// boot, GET /plugins/official, setup-checklist naming, and the deployer pipeline
/// (repo → build → install into the official directory). Official vs community is exactly
/// "listed here or not", plus which install directory the binary is loaded from.
/// </summary>
public sealed record OfficialPluginCatalogEntry(
    string Id,
    string Name,
    string Category,
    string Repo,
    string? Assembly = null,
    bool SystemRequired = false);

/// <summary>
/// One entry of <c>plugins.community.json</c> (Plugins:List:Community) — a recognized,
/// curated community plugin: available and deployable by id (repeatable deployments and
/// the install endpoint reference it) but never official; its binary only ever comes from
/// the community directory. <c>assembly</c> overrides the assembly/folder name when the
/// repo name doesn't match it (install derives it from the repo name otherwise).
/// </summary>
public sealed record CommunityPluginCatalogEntry(
    string Id,
    string Name,
    string Repo,
    string? Category = null,
    string? Author = null,
    string? Assembly = null,
    bool Discoverable = true,
    bool SystemRequired = false);

/// <summary>
/// <c>Plugins:Required</c> ids from appsettings — what the app enforces (installed and
/// configured) and drives the setup checklist from.
/// </summary>
public sealed record RequiredPluginSets(
    IReadOnlySet<string> Official,
    IReadOnlySet<string> Community);

public static class PluginCatalog
{
    public static IReadOnlyList<OfficialPluginCatalogEntry> ReadOfficial(IConfiguration configuration)
    {
        var entries = new List<OfficialPluginCatalogEntry>();
        foreach (var section in configuration.GetSection("Plugins:List:Official").GetChildren())
        {
            entries.Add(new OfficialPluginCatalogEntry(
                Id: section["id"] ?? "",
                Name: section["name"] ?? "",
                Category: section["category"] ?? "",
                Repo: section["repo"] ?? "",
                Assembly: section["assembly"],
                SystemRequired: bool.TryParse(section["systemRequired"], out var systemRequired) && systemRequired));
        }
        return entries;
    }

    public static IReadOnlyList<CommunityPluginCatalogEntry> ReadCommunity(IConfiguration configuration)
    {
        var entries = new List<CommunityPluginCatalogEntry>();
        foreach (var section in configuration.GetSection("Plugins:List:Community").GetChildren())
        {
            entries.Add(new CommunityPluginCatalogEntry(
                Id: section["id"] ?? "",
                Name: section["name"] ?? "",
                Repo: section["repo"] ?? "",
                Category: section["category"],
                Author: section["author"],
                Assembly: section["assembly"],
                Discoverable: !bool.TryParse(section["discoverable"], out var discoverable) || discoverable,
                SystemRequired: bool.TryParse(section["systemRequired"], out var systemRequired) && systemRequired));
        }
        return entries;
    }

    public static RequiredPluginSets ReadRequired(IConfiguration configuration) => new(
        ReadIdSet(configuration.GetSection("Plugins:Required:Official")),
        ReadIdSet(configuration.GetSection("Plugins:Required:Community")));

    public static bool IsSystemRequired(IConfiguration configuration, string pluginId) =>
        ReadOfficial(configuration).Any(entry => entry.SystemRequired &&
            string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase)) ||
        ReadCommunity(configuration).Any(entry => entry.SystemRequired &&
            string.Equals(entry.Id, pluginId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlySet<string> ReadIdSet(IConfigurationSection section) =>
        new HashSet<string>(
            section.GetChildren().Select(child => child.Value ?? "").Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
}
