using System.Diagnostics;

namespace MediaPager.App.Core.Plugins;

/// <summary>
/// One plugin-started job. Owned by the plugin (its <see cref="IPluginJob"/> handle is handed
/// back from <see cref="IPluginActivity.BeginJob"/>) and observable by the host's activity
/// panel. Cancellation flows host → plugin via <see cref="Cancel"/>.
/// </summary>
public sealed class PluginJob : IPluginJob
{
    private readonly PluginActivityStore store;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly object gate = new();

    private string? status;
    private double? progress;
    private PluginJobState state = PluginJobState.Running;

    internal PluginJob(string pluginId, string title, PluginActivityStore store)
    {
        this.store = store;
        PluginId = pluginId;
        Title = title;
        Id = Guid.NewGuid().ToString("N")[..12];
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public string PluginId { get; }

    public string Title { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsCancellationRequested => cancellation.IsCancellationRequested;

    public CancellationToken CancellationToken => cancellation.Token;

    public TimeSpan Elapsed
    {
        get { lock (gate) return stopwatch.Elapsed; }
    }

    public void Report(double progress, string? status = null)
    {
        lock (gate)
        {
            if (state != PluginJobState.Running) return;
            this.progress = Math.Clamp(progress, 0, 1);
            if (status is not null) this.status = status;
        }
    }

    public void Complete(string? status = null)
    {
        lock (gate)
        {
            if (state != PluginJobState.Running) return;
            state = PluginJobState.Completed;
            progress = 1;
            if (status is not null) this.status = status;
            stopwatch.Stop();
        }
    }

    public void Fail(string status)
    {
        lock (gate)
        {
            if (state != PluginJobState.Running) return;
            state = PluginJobState.Failed;
            this.status = status;
            stopwatch.Stop();
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (state != PluginJobState.Running) return;
            state = PluginJobState.Cancelled;
            stopwatch.Stop();
        }
        try { cancellation.Cancel(); } catch { }
    }

    internal PluginJobInfo Snapshot()
    {
        lock (gate)
        {
            return new PluginJobInfo(Id, PluginId, Title, status, progress, state);
        }
    }
}
