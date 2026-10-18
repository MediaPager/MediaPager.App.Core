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
        return repository.GetAsync(pluginId, name, cancellationToken);
    }

    public Task SetAsync(string pluginKey, string name, string? value, CancellationToken cancellationToken = default)
    {
        EnsureOwnNamespace(pluginKey);
        return repository.SetAsync(pluginId, name, value, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(string pluginKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnNamespace(pluginKey);
        return repository.GetAllAsync(pluginId, cancellationToken);
    }

    private void EnsureOwnNamespace(string pluginKey)
    {
        if (!string.Equals(pluginKey, _pluginKey, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Plugin '{pluginId}' may only access its own settings namespace.");
    }
}

/// <summary>Limits live inter-plugin references to the dependencies declared in the plugin manifest.</summary>
internal sealed class PluginScopedHost(
    IPluginHost inner,
    IReadOnlySet<string> dependencyIds) : IPluginHost
{
    public IReadOnlyList<IMediaPagerPlugin> Plugins => inner.Plugins
        .Where(plugin => dependencyIds.Contains(plugin.Descriptor.Id))
        .ToList();

    public IMediaPagerPlugin? GetPlugin(string id) =>
        dependencyIds.Contains(id) ? inner.GetPlugin(id) : null;

    public TPlugin? GetPlugin<TPlugin>() where TPlugin : class =>
        GetPlugins<TPlugin>().FirstOrDefault();

    public IReadOnlyList<TPlugin> GetPlugins<TPlugin>() where TPlugin : class => inner.Plugins
        .Where(plugin => dependencyIds.Contains(plugin.Descriptor.Id))
        .OfType<TPlugin>()
        .ToList();
}

/// <summary>Overrides plugin settings with an identity-bound view, scopes IPluginHost to
/// declared dependencies, and hides direct host persistence/configuration entry points.
/// This is a defense-in-depth DI boundary, not a replacement for worker isolation: code
/// still executes in-process with the process's operating-system permissions.</summary>
internal sealed class PluginServiceProvider(
    IServiceProvider inner,
    IPluginSettingsStore settings,
    IPluginHost pluginHost,
    IReadOnlySet<string> hostCapabilities)
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
        typeof(AuthDbContext),
    ];

    private readonly IServiceProviderIsService? _serviceProbe =
        inner.GetService<IServiceProviderIsService>();

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IPluginSettingsStore)) return settings;
        if (serviceType == typeof(IPluginHost)) return pluginHost;
        if (serviceType == typeof(IMediaToolBroker) &&
            !hostCapabilities.Contains(PluginHostCapabilityNames.MediaTools))
            return null;
        if (serviceType == typeof(IBrowserAutomationBroker) &&
            !hostCapabilities.Contains(PluginHostCapabilityNames.BrowserAutomation))
            return null;
        if (serviceType == typeof(ILibraryStore) &&
            !hostCapabilities.Contains(PluginHostCapabilityNames.LibraryStore))
            return null;
        if (serviceType == typeof(IPluginEmailSender) &&
            !hostCapabilities.Contains(PluginHostCapabilityNames.EmailSending))
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
            return hostCapabilities.Contains(PluginHostCapabilityNames.MediaTools) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (serviceType == typeof(IBrowserAutomationBroker))
            return hostCapabilities.Contains(PluginHostCapabilityNames.BrowserAutomation) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (serviceType == typeof(ILibraryStore))
            return hostCapabilities.Contains(PluginHostCapabilityNames.LibraryStore) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (serviceType == typeof(IPluginEmailSender))
            return hostCapabilities.Contains(PluginHostCapabilityNames.EmailSending) &&
                   (_serviceProbe?.IsService(serviceType) ?? false);
        if (HiddenHostServices.Contains(serviceType)) return false;
        return _serviceProbe?.IsService(serviceType) ?? false;
    }
}
