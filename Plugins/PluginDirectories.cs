using Microsoft.Extensions.Configuration;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// The two plugin install directories under the plugins root (Plugins:Directory, default
/// ~/.MediaPager/plugins): <c>official</c> (shipped — deployed there at build — or installed
/// on demand from the official list) and <c>community</c> (installed from the recognized
/// community list). Boot scans both; the directory is the load origin for official vs
/// community tagging.
/// </summary>
public static class PluginDirectories
{
    public static string Root(IConfiguration configuration) =>
        configuration["Plugins:Directory"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".MediaPager", "plugins");

    public static string Official(IConfiguration configuration) => Path.Combine(Root(configuration), "official");

    public static string Community(IConfiguration configuration) => Path.Combine(Root(configuration), "community");
}