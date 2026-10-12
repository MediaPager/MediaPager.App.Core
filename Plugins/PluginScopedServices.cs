using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MediaPager.App.Core.Models;
using MediaPager.App.Core.Services;

namespace MediaPager.App.Core.Plugins;

/// <summary>Plugin-facing settings view. The caller cannot select another plugin's
/// namespace, even though the compatibility contract still carries the historical key
/// parameter. A wrong key fails closed.</summary>
internal sealed class PluginScopedSettingsStore(
    IPluginSettingsRepository repository,
    string pluginId) : IPluginSettingsStore
{
    private readonly string _pluginKey = pluginId.Split('.')[^1];

    public Task<string?> GetAsync(string pluginKey, string name, CancellationToken cancellationToken = default)
    {
        EnsureOwnNamespace(pluginKey);
        return repository.GetAsync(_pluginKey, name, cancellationToken);
    }

    public Task SetAsync(string pluginKey, string name, string? value, CancellationToken cancellationToken = default)
    {
        EnsureOwnNamespace(pluginKey);
        return repository.SetAsync(_pluginKey, name, value, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnNamespace(pluginKey);
        return repository.GetAllAsync(_pluginKey, cancellationToken);
    }

    private void EnsureOwnNamespace(string pluginKey)
    {
        if (!string.Equals(pluginKey, _pluginKey, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Plugin '{pluginId}' may only access its own settings namespace.");
    }
}

/// <summary>Limits live inter-plugin references to declared dependencies. Downloads also
/// receives the stream-provider resolver view and the download-provider view it hosts.</summary>
internal sealed class PluginScopedHost(
    IPluginHost inner,
    string pluginId,
    IReadOnlySet<string> dependencyIds) : IPluginHost
{
    public IReadOnlyList<IMediaPagerPlugin> Plugins => inner.Plugins
        .Where(plugin => dependencyIds.Contains(plugin.Descriptor.Id))
        .ToList();

    public IMediaPagerPlugin? GetPlugin(string id) =>
        dependencyIds.Contains(id) ? inner.GetPlugin(id) : null;

    public TPlugin? GetPlugin<TPlugin>() where TPlugin : class =>
        GetPlugins<TPlugin>().FirstOrDefault();

    public IReadOnlyList<TPlugin> GetPlugins<TPlugin>() where TPlugin : class
    {
        var mayResolveAllStreams = string.Equals(
            pluginId, "mediapager.actions.example", StringComparison.OrdinalIgnoreCase) &&
            typeof(TPlugin) == typeof(IStreamProviderPlugin);
        var mayResolveDownloadProviders = string.Equals(
            pluginId, "mediapager.actions.example", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(typeof(TPlugin).FullName,
                "MediaPager.Plugins.Interface.Download.IDownloadProviderPlugin", StringComparison.Ordinal);
        return inner.Plugins
            .Where(plugin => dependencyIds.Contains(plugin.Descriptor.Id) ||
                             mayResolveAllStreams && plugin is IStreamProviderPlugin ||
                             mayResolveDownloadProviders && plugin.GetType().GetInterfaces().Any(type =>
                                 string.Equals(type.FullName,
                                     "MediaPager.Plugins.Interface.Download.IDownloadProviderPlugin", StringComparison.Ordinal)))
            .OfType<TPlugin>()
            .ToList();
    }
}

/// <summary>Overrides plugin settings with an identity-bound view, scopes IPluginHost to
/// declared dependencies, and hides direct host persistence/configuration entry points.
/// This is a defense-in-depth DI boundary, not a replacement for worker isolation: code
/// still executes in-process with the process's operating-system permissions.</summary>
internal sealed class PluginServiceProvider(
    IServiceProvider inner,
    IPluginSettingsStore settings,
    IPluginHost pluginHost,
    string pluginId)
    : IServiceProvider, IServiceProviderIsService
{
    private static readonly HashSet<Type> HiddenHostServices =
    [
        typeof(IConfiguration),
        typeof(IConfigurationRoot),
        typeof(IServiceScopeFactory),
        typeof(IPluginSettingsRepository),
        typeof(PluginRegistry),
        typeof(PluginDeployer),
        typeof(PluginInstaller),
        typeof(ILibraryStore),
        typeof(AuthDbContext),
    ];

    private readonly IServiceProviderIsService? _serviceProbe =
        inner.GetService<IServiceProviderIsService>();

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IPluginSettingsStore)) return settings;
        if (serviceType == typeof(IPluginHost)) return pluginHost;
        if (serviceType == typeof(IMediaToolBroker) &&
            !string.Equals(pluginId, "mediapager.actions.example", StringComparison.OrdinalIgnoreCase))
            return null;
        if (serviceType == typeof(IBrowserAutomationBroker) &&
            !string.Equals(pluginId, "mediapager.stream.example", StringComparison.OrdinalIgnoreCase))
            return null;
        if (serviceType == typeof(IServiceProvider)) return this;
        if (serviceType == typeof(IServiceProviderIsService)) return this;
        if (HiddenHostServices.Contains(serviceType)) return null;
        return inner.GetService(serviceType);
    }

    public bool IsService(Type serviceType)
    {
        if (serviceType == typeof(IPluginSettingsStore) ||
            serviceType == typeof(IPluginHost) ||
            serviceType == typeof(IServiceProvider) ||
            serviceType == typeof(IServiceProviderIsService))
            return true;
        if (serviceType == typeof(IMediaToolBroker))
            return string.Equals(pluginId, "mediapager.actions.example", StringComparison.OrdinalIgnoreCase) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (serviceType == typeof(IBrowserAutomationBroker))
            return string.Equals(pluginId, "mediapager.stream.example", StringComparison.OrdinalIgnoreCase) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (HiddenHostServices.Contains(serviceType)) return false;
        return _serviceProbe?.IsService(serviceType) ?? false;
    }
}
