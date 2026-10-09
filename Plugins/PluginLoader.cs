using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// Loads plugin assemblies from install directories. Both kinds install the same way —
/// one folder per plugin, its compiled dll inside (an optional .disabled marker written by
/// uninstall keeps it off) — and differ only by directory: official (shipped, deployed at
/// build, or installed from the official list) vs community (installed from the recognized
/// community list). Assemblies load into the default load context so their Contracts types
/// unify with the host's — a collectible ALC would give every <c>IMediaPagerPlugin</c> a
/// different identity and the registry's IsAssignableFrom checks would silently fail.
/// Uninstall therefore unloads the plugin from the registry and blocks it on the next boot
/// rather than trying to unload the assembly.
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// Load every plugin under a plugins directory (official or community). Community
    /// loads reject ids in <paramref name="reservedIds"/> (the official list, installed or
    /// not, plus loaded officials) so a community plugin can never shadow an official one.
    /// When <paramref name="requiredIds"/> is given (official dir at boot), an official
    /// plugin whose id isn't in the set is not registered — the directory holds every
    /// shipped official, but only <c>Plugins:Required:Official</c> is active by default.
    /// </summary>
    public static IReadOnlyList<IMediaPagerPlugin> LoadDirectory(
        IServiceProvider services, string directory, bool official,
        IReadOnlySet<string>? reservedIds = null, IReadOnlySet<string>? requiredIds = null)
    {
        var plugins = new List<IMediaPagerPlugin>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return plugins;

        var registry = services.GetService<PluginRegistry>();
        var pending = Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        while (pending.Count > 0)
        {
            var loadedIds = new HashSet<string>(
                (registry?.Plugins ?? plugins).Select(plugin => plugin.Descriptor.Id),
                StringComparer.OrdinalIgnoreCase);
            var ready = pending.Where(path => TryReadManifest(path, out var manifest) &&
                    (manifest?.Requires ?? []).All(loadedIds.Contains))
                .ToList();
            if (ready.Count == 0)
            {
                foreach (var path in pending)
                {
                    if (!TryReadManifest(path, out var manifest))
                    {
                        Console.Error.WriteLine($"[plugins] skipped {Path.GetFileName(path)}: invalid manifest");
                        continue;
                    }
                    var missing = (manifest?.Requires ?? []).Where(id => !loadedIds.Contains(id));
                    Console.Error.WriteLine(
                        $"[plugins] skipped {Path.GetFileName(path)}: missing or cyclic plugin dependencies: {string.Join(", ", missing)}");
                }
                break;
            }

            foreach (var pluginDir in ready)
            {
                var loaded = LoadFolder(services, pluginDir, official, reservedIds, requiredIds);
                plugins.AddRange(loaded);
                foreach (var plugin in loaded)
                    registry?.Add(plugin, community: !official);
                pending.Remove(pluginDir);
            }
        }
        return plugins;
    }

    /// <summary>
    /// Load one installed plugin folder (assembly name == folder name), used by
    /// <see cref="LoadDirectory"/> and for live registration right after an install.
    /// </summary>
    public static IReadOnlyList<IMediaPagerPlugin> LoadFolder(
        IServiceProvider services, string pluginDir, bool official,
        IReadOnlySet<string>? reservedIds = null, IReadOnlySet<string>? requiredIds = null)
    {
        var plugins = new List<IMediaPagerPlugin>();
        if (string.IsNullOrWhiteSpace(pluginDir) || !Directory.Exists(pluginDir))
            return plugins;
        if (File.Exists(Path.Combine(pluginDir, ".disabled")))
            return plugins;

        if (!TryReadManifest(pluginDir, out var manifest))
            return plugins;
        var host = services.GetService<IPluginHost>();
        var missingDependencies = (manifest?.Requires ?? [])
            .Where(id => host?.GetPlugin(id) is null)
            .ToList();
        if (missingDependencies.Count > 0)
        {
            Console.Error.WriteLine(
                $"[plugins] skipped {Path.GetFileName(pluginDir)}: required plugin(s) are not loaded: {string.Join(", ", missingDependencies)}");
            return plugins;
        }

        var folderName = Path.GetFileName(pluginDir);
        var assemblyPath = Path.Combine(pluginDir, $"{folderName}.dll");
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"[plugins] plugin folder {folderName} has no {folderName}.dll — skipped");
            return plugins;
        }

        // A plugin's own package dependencies (e.g. Playwright) publish as sibling dlls in
        // the same folder, but the default context only probes the host's own assets —
        // pre-load any sibling that isn't loaded yet. Skips what the host already provides
        // (notably PluginContracts: its types must stay the host's single copy or the
        // registry's IsAssignableFrom checks would see two identities).
        foreach (var dependency in Directory.GetFiles(pluginDir, "*.dll"))
        {
            if (string.Equals(Path.GetFileName(dependency), Path.GetFileName(assemblyPath),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            var dependencyName = Path.GetFileNameWithoutExtension(dependency);
            if (AssemblyLoadContext.Default.Assemblies.Any(loaded =>
                    string.Equals(loaded.GetName().Name, dependencyName, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"[plugins] could not pre-load {dependencyName} for {folderName}: {exception.Message}");
            }
        }

        Assembly assembly;
        try
        {
            // Load into the default context so Contracts types unify with the host's.
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[plugins] could not load plugin assembly {assemblyPath}: {exception.Message}");
            return plugins;
        }

        foreach (var plugin in ConstructPlugins(assembly, services))
        {
            if (!official && reservedIds is not null && reservedIds.Contains(plugin.Descriptor.Id))
            {
                Console.Error.WriteLine(
                    $"[plugins] rejected community plugin {plugin.Descriptor.Id}: id collides with an official plugin");
                continue;
            }

            if (official && requiredIds is not null && !requiredIds.Contains(plugin.Descriptor.Id))
            {
                Console.WriteLine(
                    $"[plugins] skipped official {plugin.Descriptor.Id}: not in Plugins:Required:Official");
                continue;
            }

            services.GetService<PluginRegistry>()?.SetRequirements(
                plugin.Descriptor.Id, manifest?.Requires);
            plugins.Add(plugin);
            Console.WriteLine(
                $"[plugins] loaded {(official ? "official" : "community")} {plugin.Descriptor.Id} v{plugin.Descriptor.Version}");
        }
        return plugins;
    }

    private static List<IMediaPagerPlugin> ConstructPlugins(Assembly assembly, IServiceProvider services)
    {
        var constructed = new List<IMediaPagerPlugin>();
        var activityStore = services.GetService<PluginActivityStore>();
        foreach (var type in assembly.GetExportedTypes())
        {
            if (typeof(IMediaPagerPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsClass: true, IsNested: false })
            {
                try
                {
                    var needsActivity = type.GetConstructors()
                        .SelectMany(constructor => constructor.GetParameters())
                        .Any(parameter => parameter.ParameterType == typeof(IPluginActivity));
                    var activity = needsActivity ? new PluginActivity(activityStore
                        ?? throw new InvalidOperationException("Plugin activity services are not registered.")) : null;
                    var instance = activity is null
                        ? ActivatorUtilities.CreateInstance(services, type)
                        : ActivatorUtilities.CreateInstance(services, type, activity);
                    if (instance is IMediaPagerPlugin plugin)
                    {
                        if (activity is not null)
                            activity.PluginId = plugin.Descriptor.Id;
                        constructed.Add(plugin);
                    }
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"[plugins] failed to construct {type.FullName}: {exception.Message}");
                }
            }
        }

        return constructed;
    }

    private static bool TryReadManifest(string pluginDir, out PluginManifest? manifest)
    {
        manifest = null;
        var folder = Path.GetFileName(pluginDir);
        if (!folder.StartsWith(PluginManifestFile.Prefix, StringComparison.OrdinalIgnoreCase))
            return true;
        var suffix = folder[PluginManifestFile.Prefix.Length..];
        var path = Path.Combine(pluginDir, PluginManifestFile.FileName(suffix));
        if (!File.Exists(path))
            return true; // Existing installations predate dependency manifests.
        try
        {
            manifest = PluginManifestFile.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[plugins] invalid manifest in {folder}: {exception.Message}");
            return false;
        }
    }
}
