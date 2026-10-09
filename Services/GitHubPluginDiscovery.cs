using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using MediaPager.App.Core.Plugins;

namespace MediaPager.App.Core.Services;

/// <summary>
/// Discovers plugins for the Settings → Plugins Community tab — repositories named
/// <c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;</c> (the search itself is
/// limited to the known plugin types) from accounts other than the official one, each gated
/// by its repo-root manifest
/// (<c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;.manifest.json</c>). Candidate
/// repos are vetted with a web query only (GitHub search + a manifest fetch per candidate)
/// — no code is ever cloned or compiled during listing, so the Community tab auto-lists
/// every manifest-valid repo without touching a checkout. The install job separately
/// requires and cross-checks the same manifest against the code it builds, so a listing
/// here is never a promise about the plugin — only that it declares itself properly.
/// Official plugins are NOT discovered this way: the official list always comes from the
/// repo's own plugins.official.json (which can differ across branches). All successful
/// results are cached per query because the anonymous GitHub search (10/min) and contents
/// (60/hr) APIs are rate-limited; a typing user must not burn the budget.
/// </summary>
public sealed class GitHubPluginDiscovery
{
    private const string Prefix = "MediaPager.Plugins.";
    private const string OfficialOwner = "MediaPager";
    private const int BrowseLimit = 10;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public GitHubPluginDiscovery(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaPager/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>Community MediaPager plugins. Without a query it auto-lists the top
    /// <see cref="BrowseLimit"/> manifest-valid <c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;</c>
    /// repos; with a query it narrows that set to matching repos (same file gate, still quick).
    /// Results are cached for 30 minutes keyed by the query itself.</summary>
    public async Task<List<GitHubPlugin>> GetCommunityListAsync(string? query, CancellationToken cancellationToken = default)
    {
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var typeGroups = PluginManifestFile.KnownTypeNames
            .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
            .Chunk(5)
            .ToArray();
        var searches = typeGroups.Select((types, index) => SearchAsync(
            BuildSearchQuery(query, types),
            $"{(query ?? "").ToLowerInvariant()}:{index}",
            cancellationToken));
        var results = await Task.WhenAll(searches);
        return results
            .SelectMany(result => result)
            .GroupBy(plugin => plugin.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(plugin => plugin.UpdatedAt)
            .Take(BrowseLimit)
            .ToList();
    }

    /// <summary>A type-limited GitHub search. At most five plugin-type clauses are included
    /// so the query stays under GitHub's five Boolean-operator limit (the remaining groups
    /// are searched separately and merged). User text is quoted to keep it a search term.</summary>
    private static string BuildSearchQuery(string? query, IReadOnlyList<string> types)
    {
        var safeQuery = query is null
            ? ""
            : $" \"{query.Replace("\\", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal)}\"";
        var qualifiers = query is null ? "in:name" : "in:name,description";
        var clauses = types.Select(type => $"{Prefix}{char.ToUpperInvariant(type[0])}{type[1..]}{safeQuery} {qualifiers}");
        return $"{string.Join(" OR ", clauses)} -user:{OfficialOwner}";
    }

    private async Task<List<GitHubPlugin>> SearchAsync(string searchQuery, string cacheKeySalt, CancellationToken cancellationToken)
    {
        var cacheKey = $"mediapager:plugins:community:discover:{cacheKeySalt}";
        if (_cache.TryGetValue(cacheKey, out List<GitHubPlugin>? cached) && cached is not null)
            return cached;

        var url = "https://api.github.com/search/repositories"
            + $"?q={Uri.EscapeDataString(searchQuery)}"
            + $"&per_page={BrowseLimit}&page=1&sort=updated";
        IReadOnlyList<GitHubRepository> candidates = [];
        using (var response = await _http.GetAsync(url, cancellationToken))
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                    ? values.FirstOrDefault()
                    : null;
                if (remaining == "0" || response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new HttpRequestException(
                        "GitHub API rate limit reached. Try again after the rate limit resets.",
                        null,
                        response.StatusCode);
            }
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GitHubSearchResponse>(cancellationToken);
            if (result?.Items is { Count: > 0 })
                candidates = result.Items;
        }

        var plugins = new List<GitHubPlugin>();
        foreach (var repo in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!repo.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || repo.Name.Length == Prefix.Length)
                continue;
            var pluginName = repo.Name[Prefix.Length..];
            if (!PluginManifestFile.IsValidSuffix(pluginName))
                continue; // not MediaPager.Plugins.<PluginType>.<PluginName> → never auto-listed
            if (repo.Archived)
                continue;
            // Community only — the official set is read from plugins.official.json, never
            // from a search (it must track the branch you are on).
            if (string.Equals(repo.Owner.Login, OfficialOwner, StringComparison.OrdinalIgnoreCase))
                continue;

            var manifest = await FetchManifestAsync(repo.Owner.Login, repo.Name, pluginName, repo.DefaultBranch, cancellationToken);
            if (manifest is null)
                continue; // no (valid) manifest → not shown in the Community tab
            if (!manifest.Discoverable)
                continue; // dependency/interface plugins stay out of the standalone Community catalog

            plugins.Add(new GitHubPlugin
            {
                Id = repo.Id,
                PluginId = manifest.Id,
                Name = manifest.Name,
                FullName = repo.FullName,
                Description = manifest.Description.Length > 0 ? manifest.Description : repo.Description,
                Types = manifest.Types.ToArray(),
                RepositoryUrl = repo.HtmlUrl,
                CloneUrl = repo.CloneUrl,
                Owner = repo.Owner.Login,
                OwnerAvatarUrl = repo.Owner.AvatarUrl,
                Stars = repo.StargazersCount,
                DefaultBranch = repo.DefaultBranch,
                UpdatedAt = repo.UpdatedAt,
            });
        }

        _cache.Set(cacheKey, plugins, CacheDuration);
        return plugins;
    }

    /// <summary>Fetch one candidate's repo-root manifest, validating it against the repo
    /// name suffix. Returns null when it's missing or invalid — no exception: discovery
    /// skips such candidates silently. The raw endpoint means zero JSON decoding of the
    /// contents envelope.</summary>
    private async Task<PluginManifest?> FetchManifestAsync(string owner, string repoName, string pluginName, string branch, CancellationToken cancellationToken)
    {
        try
        {
            var manifestPath = Uri.EscapeDataString(PluginManifestFile.FileName(pluginName));
            var url = $"https://api.github.com/repos/{owner}/{Uri.EscapeDataString(repoName)}/contents/{manifestPath}?ref={Uri.EscapeDataString(branch)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null; // 404 (no manifest) or 403/429 → treat as not a candidate
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return PluginManifestFile.TryParse(json, out var manifest, out _)
                ? manifest
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}

/// <summary>Public plugin information for the Community tab, enriched from the manifest
/// it was vetted with.</summary>
public sealed record GitHubPlugin
{
    public long Id { get; init; }
    public string PluginId { get; init; } = "";
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string? Description { get; init; }
    public string[] Types { get; init; } = [];
    public string RepositoryUrl { get; init; } = "";
    public string CloneUrl { get; init; } = "";
    public string Owner { get; init; } = "";
    public string OwnerAvatarUrl { get; init; } = "";
    public int Stars { get; init; }
    public string DefaultBranch { get; init; } = "";
    public DateTimeOffset UpdatedAt { get; init; }
}

internal sealed class GitHubSearchResponse
{
    [JsonPropertyName("items")]
    public List<GitHubRepository> Items { get; set; } = [];
}

internal sealed class GitHubRepository
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("clone_url")] public string CloneUrl { get; set; } = "";
    [JsonPropertyName("owner")] public GitHubOwner Owner { get; set; } = new();
    [JsonPropertyName("stargazers_count")] public int StargazersCount { get; set; }
    [JsonPropertyName("default_branch")] public string DefaultBranch { get; set; } = "";
    [JsonPropertyName("updated_at")] public DateTimeOffset UpdatedAt { get; set; }
    [JsonPropertyName("archived")] public bool Archived { get; set; }
}

internal sealed class GitHubOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
    [JsonPropertyName("avatar_url")] public string AvatarUrl { get; set; } = "";
}
