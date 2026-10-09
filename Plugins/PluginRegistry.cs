namespace MediaPager.App.Core.Plugins;

/// <summary>Runtime catalog of loaded plugins. Official plugins (loaded from the official
/// directory) and community plugins (community directory) share one pipeline; officials
/// system-required plugins and plugins with loaded dependents are locked; optional
/// plugins can be turned off at runtime.</summary>
public sealed class PluginRegistry : IPluginHost
{
    private readonly object gate = new();
    private List<IMediaPagerPlugin> plugins = [];
    private HashSet<string> communityIds = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, HashSet<string>> requirements = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All loaded plugin instances (official first, then directory-loaded).</summary>
    public IReadOnlyList<IMediaPagerPlugin> Plugins
    {
        get
        {
            lock (gate) return plugins;
        }
    }

    /// <summary>Whether a plugin is community-installed (removable) vs official (locked).</summary>
    public bool IsCommunity(IMediaPagerPlugin plugin)
    {
        lock (gate) return communityIds.Contains(plugin.Descriptor.Id);
    }

    public void Reset(IEnumerable<IMediaPagerPlugin> loaded, IEnumerable<string>? communityPluginIds = null)
    {
        lock (gate)
        {
            plugins = loaded.ToList();
            communityIds = communityPluginIds is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(communityPluginIds, StringComparer.OrdinalIgnoreCase);
            var loadedIds = plugins.Select(plugin => plugin.Descriptor.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            requirements = requirements
                .Where(entry => loadedIds.Contains(entry.Key))
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetRequirements(string pluginId, IEnumerable<string>? requiredIds)
    {
        lock (gate)
            requirements[pluginId] = new HashSet<string>(requiredIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IMediaPagerPlugin> GetDependents(string pluginId)
    {
        lock (gate)
        {
            var requiredBy = requirements
                .Where(entry => entry.Value.Contains(pluginId))
                .Select(entry => entry.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return plugins.Where(plugin => requiredBy.Contains(plugin.Descriptor.Id)).ToList();
        }
    }

    public IReadOnlyList<string> GetRequirements(string pluginId)
    {
        lock (gate)
            return requirements.TryGetValue(pluginId, out var ids) ? ids.ToList() : [];
    }

    /// <summary>Community dependencies that can be removed together with a plugin because
    /// no other loaded plugin requires them. Returned in dependent-first unload order.</summary>
    public IReadOnlyList<IMediaPagerPlugin> GetUnusedDependencies(string pluginId)
    {
        lock (gate)
        {
            if (!requirements.TryGetValue(pluginId, out var rootRequirements)) return [];

            var loadedById = plugins.ToDictionary(plugin => plugin.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(rootRequirements);
            while (pending.TryDequeue(out var id))
            {
                if (!closure.Add(id) || !loadedById.ContainsKey(id)) continue;
                if (requirements.TryGetValue(id, out var nested))
                    foreach (var nestedId in nested) pending.Enqueue(nestedId);
            }

            var removable = closure
                .Where(id => communityIds.Contains(id) && loadedById.ContainsKey(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var id in removable.ToList())
                {
                    var usedElsewhere = requirements
                        .Where(entry => !string.Equals(entry.Key, pluginId, StringComparison.OrdinalIgnoreCase))
                        .Any(entry => entry.Value.Contains(id) && !removable.Contains(entry.Key));
                    if (usedElsewhere)
                    {
                        removable.Remove(id);
                        changed = true;
                    }
                }
            }

            var ordered = new List<IMediaPagerPlugin>();
            while (removable.Count > 0)
            {
                var noDependents = removable
                    .Where(id => !requirements.Any(entry => removable.Contains(entry.Key) && entry.Value.Contains(id)))
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (noDependents.Count == 0) break; // A dependency cycle is retained safely.
                foreach (var id in noDependents)
                {
                    ordered.Add(loadedById[id]);
                    removable.Remove(id);
                }
            }
            return ordered;
        }
    }

    /// <summary>Register a plugin installed at runtime (deployer). Community ids become
    /// removable; officials are locked like every build-deployed one.</summary>
    public void Add(IMediaPagerPlugin plugin, bool community)
    {
        lock (gate)
        {
            if (plugins.Any(candidate =>
                    string.Equals(candidate.Descriptor.Id, plugin.Descriptor.Id, StringComparison.OrdinalIgnoreCase)))
                return;
            plugins.Add(plugin);
            if (community) communityIds.Add(plugin.Descriptor.Id);
            requirements.TryAdd(plugin.Descriptor.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Unload a community plugin at runtime. Returns false for official plugins.</summary>
    public bool Remove(string pluginId)
    {
        lock (gate)
        {
            if (!communityIds.Contains(pluginId)) return false;
            if (requirements.Values.Any(requiredIds => requiredIds.Contains(pluginId))) return false;
            var plugin = plugins.FirstOrDefault(candidate =>
                string.Equals(candidate.Descriptor.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (plugin is not null) plugins.Remove(plugin);
            communityIds.Remove(pluginId);
            requirements.Remove(pluginId);
            return true;
        }
    }

    /// <summary>Unload any plugin regardless of origin — the official "turn off" path
    /// (drops it from Plugins:Required first, so the next boot skips it too).</summary>
    public bool Unload(string pluginId)
    {
        lock (gate)
        {
            if (requirements.Values.Any(requiredIds => requiredIds.Contains(pluginId))) return false;
            var plugin = plugins.FirstOrDefault(candidate =>
                string.Equals(candidate.Descriptor.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (plugin is null) return false;
            plugins.Remove(plugin);
            communityIds.Remove(pluginId);
            requirements.Remove(pluginId);
            return true;
        }
    }

    public TPlugin? Resolve<TPlugin>() where TPlugin : class =>
        Plugins.OfType<TPlugin>().FirstOrDefault();

    public IReadOnlyList<TPlugin> ResolveAll<TPlugin>() where TPlugin : class =>
        Plugins.OfType<TPlugin>().ToList();

    public IMediaPagerPlugin? GetPlugin(string id) => Plugins.FirstOrDefault(plugin =>
        string.Equals(plugin.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));

    public TPlugin? GetPlugin<TPlugin>() where TPlugin : class => Resolve<TPlugin>();

    public IReadOnlyList<TPlugin> GetPlugins<TPlugin>() where TPlugin : class => ResolveAll<TPlugin>();
}
