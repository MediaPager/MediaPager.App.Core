namespace MediaPager.App.Core.Services;

public static class PluginBuildSdkVersion
{
    public const string Version = "10.0.401";
}

public sealed record PluginBuildSdk(
    string Executable,
    string InstallDirectory,
    string NuGetPackagesDirectory);

public sealed record PluginBuildSdkProgress(int Percent, string Message);

/// <summary>Resolves the pinned SDK used for on-demand plugin builds. The host runtime and
/// the SDK used to launch the API are independent of this tool.</summary>
public interface IPluginBuildSdkProvider
{
    Task<PluginBuildSdk> EnsureInstalledAsync(
        IProgress<PluginBuildSdkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
