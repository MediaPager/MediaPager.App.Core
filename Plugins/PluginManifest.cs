using System.Collections.Immutable;
using System.Text.Json;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// The plugin manifest — <c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;.manifest.json</c>,
/// kept at the repository root next to the project (a plugin repo may hold its source flat or
/// in subfolders; the manifest is always root-level). Repos, projects, and manifests all follow
/// the same convention: <c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;</c>, where
/// <c>PluginType</c> is one of the known capability types. The same file drives:
/// <list type="bullet">
/// <item><description>Discovery — a candidate repo is only shown in the Community tab when
/// its <c>MediaPager.Plugins.&lt;Name&gt;.manifest.json</c> exists at the repo root and
/// parses, so anyone naming a repo like ours is still vetted by the file.</description></item>
/// <item><description>Install — the deploy pipeline requires it for every install (official
/// and community) and cross-checks it against the code it actually built (id/name/types
/// vs the plugin descriptor and its implemented capability interfaces).</description></item>
/// </list>
/// Types are a comma-separated capability list (e.g. <c>stream,subtitles</c>) in the same
/// vocabulary as the runtime capability flags (stream, metadata, search, subtitles, email,
/// actions).
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Description,
    string Author,
    IReadOnlySet<string> Types,
    string Sdk,
    Version SdkVersion,
    IReadOnlyList<string>? Requires = null,
    IReadOnlyList<string>? References = null,
    IReadOnlyDictionary<string, string>? RequirementRepos = null,
    bool Discoverable = true);

public static class PluginManifestFile
{
    /// <summary>The plugin host SDK every manifest must declare — the package whose
    /// capability contracts the plugin is built against.</summary>
    public const string SdkName = "MediaPager.App.PluginContracts";

    /// <summary>The repo / project naming convention a plugin must follow — the manifest
    /// file name and the discovery/browse gate key off it.</summary>
    public const string Prefix = "MediaPager.Plugins.";

    private static readonly HashSet<string> KnownTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "stream", "metadata", "search", "subtitles", "email", "actions", "interface",
    };

    /// <summary>The known plugin type names — the <c>&lt;PluginType&gt;</c> segment of
    /// <c>MediaPager.Plugins.&lt;PluginType&gt;.&lt;PluginName&gt;</c>.</summary>
    public static IReadOnlyCollection<string> KnownTypeNames => KnownTypes;

    /// <summary>True when a repo/project suffix follows <c>&lt;PluginType&gt;.&lt;PluginName&gt;</c>:
    /// the first segment is a known capability type and a plugin name follows it. This is what
    /// ties a repo name to its plugin type — <c>Stream.Example</c> qualifies, <c>Example</c> (no
    /// type) does not. Discovery only lists and installs only accept conforming names.</summary>
    public static bool IsValidSuffix(string suffix)
    {
        var dot = suffix.IndexOf('.');
        return dot > 0 && dot < suffix.Length - 1 && KnownTypes.Contains(suffix[..dot]);
    }

    /// <summary>The manifest file name for a plugin name — <c>SMTP</c> is described by
    /// <c>MediaPager.Plugins.Email.Smtp.manifest.json</c> where <c>Email.Smtp</c> is the
    /// repo/project suffix (the path is the repo tie; the <c>name</c> field inside is the
    /// display name the code reports — checked against the descriptor at install).
    /// Mirrors the repo-naming convention so a search hit is verifiable without cloning
    /// anything.</summary>
    public static string FileName(string name) => $"MediaPager.Plugins.{name}.manifest.json";

    /// <summary>Parse and validate a manifest. False + error keeps discovery cheap: a
    /// candidate is skipped without touching its code. The repo tie is the file's name —
    /// <c>MediaPager.Plugins.&lt;repo suffix&gt;.manifest.json</c> must exist at the repo
    /// root; <c>name</c> here is the plugin's display name, cross-checked against the
    /// built descriptor at install.</summary>
    public static bool TryParse(string json, out PluginManifest? manifest, out string? error)
    {
        manifest = null;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(json);
            var id = GetString(document.RootElement, "id");
            var name = GetString(document.RootElement, "name");
            var sdk = GetString(document.RootElement, "sdk");
            var sdkVersionText = GetString(document.RootElement, "sdkVersion");
            var typesText = GetString(document.RootElement, "types");
            var requires = new List<string>();
            var requirementRepos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.TryGetProperty("requires", out var requiresElement))
            {
                if (requiresElement.ValueKind != JsonValueKind.Array)
                {
                    error = "'requires' must be an array of plugin ids.";
                    return false;
                }

                foreach (var entry in requiresElement.EnumerateArray())
                {
                    var requiredId = entry.ValueKind switch
                    {
                        JsonValueKind.String => entry.GetString()?.Trim(),
                        JsonValueKind.Object => GetString(entry, "id")?.Trim(),
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(requiredId) ||
                        !System.Text.RegularExpressions.Regex.IsMatch(requiredId, @"^mediapager\.[a-z0-9]+(\.[a-z0-9]+)*$"))
                    {
                        error = "'requires' contains an invalid plugin id.";
                        return false;
                    }

                    if (!requires.Contains(requiredId, StringComparer.OrdinalIgnoreCase))
                        requires.Add(requiredId);

                    if (entry.ValueKind == JsonValueKind.Object && GetString(entry, "repo") is { } requiredRepo)
                    {
                        requiredRepo = requiredRepo.Trim();
                        if (!IsValidReferenceRepo(requiredRepo))
                        {
                            error = $"'requires' has an invalid repo URL for '{requiredId}'.";
                            return false;
                        }
                        requirementRepos[requiredId] = requiredRepo;
                    }
                }
            }

            var references = new List<string>();
            if (document.RootElement.TryGetProperty("references", out var referencesElement))
            {
                if (referencesElement.ValueKind != JsonValueKind.Array)
                {
                    error = "'references' must be an array of repository URLs.";
                    return false;
                }

                foreach (var entry in referencesElement.EnumerateArray())
                {
                    var reference = entry.ValueKind == JsonValueKind.String ? entry.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(reference) || !IsValidReferenceRepo(reference))
                    {
                        error = "'references' contains an invalid repository URL.";
                        return false;
                    }
                    if (!references.Contains(reference, StringComparer.OrdinalIgnoreCase))
                        references.Add(reference);
                }
            }

            var discoverable = true;
            if (document.RootElement.TryGetProperty("discoverable", out var discoverableElement))
            {
                if (discoverableElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = "'discoverable' must be a boolean.";
                    return false;
                }
                discoverable = discoverableElement.GetBoolean();
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                error = "has no 'id'.";
                return false;
            }
            id = id.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"^mediapager\.[a-z0-9]+(\.[a-z0-9]+)*$"))
            {
                error = $"'id' '{id}' is not a valid mediapager plugin id (lowercase dotted, e.g. mediapager.stream.example).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "has no 'name'.";
                return false;
            }
            name = name.Trim();

            if (!string.Equals(sdk, SdkName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"'sdk' must be '{SdkName}'.";
                return false;
            }

            if (!Version.TryParse(sdkVersionText, out var sdkVersion) || sdkVersion.Minor < 0)
            {
                error = "'sdkVersion' is not a valid version (e.g. 0.1.0).";
                return false;
            }

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(typesText))
            {
                error = "has no 'types' — a comma-separated capability list (stream, metadata, search, subtitles, email, actions, interface).";
                return false;
            }
            foreach (var rawType in typesText.Split(','))
            {
                var type = rawType.Trim().ToLowerInvariant();
                if (type.Length == 0)
                    continue;
                if (!KnownTypes.Contains(type))
                {
                    error = $"'types' contains '{type}', which is not a known capability (stream, metadata, search, subtitles, email, actions, interface).";
                    return false;
                }
                types.Add(type);
            }
            if (types.Count == 0)
            {
                error = "'types' must list at least one capability.";
                return false;
            }

            manifest = new PluginManifest(
                id,
                name,
                GetString(document.RootElement, "description")?.Trim() ?? "",
                GetString(document.RootElement, "author")?.Trim() ?? "",
                types.ToImmutableSortedSet(StringComparer.OrdinalIgnoreCase),
                SdkName,
                sdkVersion,
                requires,
                references,
                requirementRepos,
                discoverable);
            return true;
        }
        catch (JsonException)
        {
            error = "not valid JSON.";
            return false;
        }
    }

    /// <summary>Strict variant for the deploy pipeline: throws the failing reason so the
    /// job log shows exactly why a candidate was rejected.</summary>
    public static PluginManifest Parse(string json)
    {
        if (!TryParse(json, out var manifest, out var error))
            throw new InvalidOperationException($"The plugin manifest is invalid: {error!}");
        return manifest!;
    }

    /// <summary>True when the manifest's declared SDK major matches the version of the host
    /// SDK this server was built with — a major bump means breaking contract changes, so a
    /// plugin declaring an older major would surface as a clear failure at install.</summary>
    public static bool IsSdkCompatible(PluginManifest manifest, Version serverSdkVersion)
        => manifest.SdkVersion.Major == serverSdkVersion.Major;

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var stringElement))
            return null;
        return stringElement.ValueKind == JsonValueKind.Null || stringElement.ValueKind == JsonValueKind.Undefined
            ? null
            : stringElement.ValueKind == JsonValueKind.String ? stringElement.GetString() : null;
    }

    private static bool IsValidReferenceRepo(string reference)
    {
        if (reference.Length > 400 || reference.StartsWith('-') ||
            reference.Any(character => char.IsWhiteSpace(character) || character is '\'' or '"' or '`' or '$'))
            return false;

        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            return !string.IsNullOrWhiteSpace(uri.Host) && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

        if (!MediaPager.App.Core.Services.GitRepoUrl.IsSshLike(reference))
            return false;
        if (!reference.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) &&
            !reference.StartsWith("git+ssh://", StringComparison.OrdinalIgnoreCase))
            return true;
        return Uri.TryCreate(reference, UriKind.Absolute, out var sshUri) &&
               !string.IsNullOrWhiteSpace(sshUri.Host) && !sshUri.UserInfo.Contains(':');
    }
}
