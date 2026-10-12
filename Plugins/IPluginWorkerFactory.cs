namespace MediaPager.App.Core.Plugins;

/// <summary>Host adapter that starts a plugin in an isolated process and returns typed
/// proxy objects for the capability interfaces that process implements.</summary>
public interface IPluginWorkerFactory
{
    bool ShouldRunInWorker(string pluginId, bool official);

    IReadOnlyList<IMediaPagerPlugin> LoadWorker(
        IServiceProvider hostServices,
        string pluginDirectory,
        string pluginId);
}
