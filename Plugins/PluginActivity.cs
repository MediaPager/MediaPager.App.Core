using System.Collections.Concurrent;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// Host-owned pool of plugin jobs and notifications. A single instance is registered in DI
/// and shared by every plugin (through the per-plugin <see cref="PluginActivity"/> view) and
/// the API that surfaces the notifications panel. Plugins never render UI for it.
/// </summary>
public sealed class PluginActivityStore
{
    private readonly ConcurrentDictionary<string, PluginJob> jobs = new();
    private readonly ConcurrentDictionary<string, PluginNotification> notifications = new();

    public PluginJob CreateJob(string pluginId, string title)
    {
        var job = new PluginJob(pluginId, title, this);
        jobs[job.Id] = job;
        return job;
    }

    public bool RemoveJob(string id)
    {
        if (!jobs.TryGetValue(id, out var job) || job.Snapshot().State == PluginJobState.Running)
            return false;
        return jobs.TryRemove(id, out _);
    }

    public void AddNotification(string pluginId, string title, string? message, PluginNotificationLevel level)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        notifications[id] = new PluginNotification(id, pluginId, title, message, level);
    }

    public bool DismissNotification(string id) => notifications.TryRemove(id, out _);

    public bool CancelJob(string id)
    {
        if (!jobs.TryGetValue(id, out var job) || job.Snapshot().State != PluginJobState.Running) return false;
        job.Cancel();
        return true;
    }

    public IReadOnlyList<PluginJobInfo> SnapshotJobs() =>
        jobs.Values
            .OrderByDescending(job => job.CreatedAt)
            .Select(job => job.Snapshot())
            .ToList();

    public IReadOnlyList<PluginNotification> SnapshotNotifications() =>
        notifications.Values
            .OrderByDescending(notification => notification.CreatedAt)
            .ToList();

    public IReadOnlyList<PluginJobInfo> JobsFor(string pluginId) =>
        jobs.Values.Where(job => job.PluginId == pluginId).OrderByDescending(j => j.CreatedAt)
            .Select(job => job.Snapshot()).ToList();

    public IReadOnlyList<PluginNotification> NotificationsFor(string pluginId) =>
        notifications.Values.Where(n => n.PluginId == pluginId).OrderByDescending(n => n.CreatedAt).ToList();
}

/// <summary>A plugin-bound view of <see cref="PluginActivityStore"/>. Constructed once per
/// plugin by the loader (its id is stamped after the descriptor is known) and injected into
/// the plugin constructor.</summary>
public sealed class PluginActivity(PluginActivityStore store) : IPluginActivity
{
    public string PluginId { get; set; } = "unknown";

    public void Notify(string title, string? message = null, PluginNotificationLevel level = PluginNotificationLevel.Info)
        => store.AddNotification(PluginId, title, message, level);

    public IPluginJob BeginJob(string title) => store.CreateJob(PluginId, title);

    public IReadOnlyList<PluginJobInfo> Jobs => store.JobsFor(PluginId);

    public IReadOnlyList<PluginNotification> Notifications => store.NotificationsFor(PluginId);
}
