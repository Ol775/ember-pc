namespace Ember.Core;

public enum WatchdogAction { None, Release, StopServiceThenRelease }

/// <summary>
/// Decides when the watchdog must hand fans back to the board. Pure logic: the worker feeds it snapshot and
/// disconnect events plus whether the Windows service is running, and acts on the answer.
/// <list type="bullet">
/// <item>Service not running: release (at once, then every 5 s while it stays down).</item>
/// <item>Service running but no snapshot for more than 10 s (hung): stop it, then release.</item>
/// <item>Otherwise nothing. A broken pipe alone is not enough while the service runs, because releasing then could
/// fight a live service that is still driving the headers.</item>
/// </list>
/// </summary>
public sealed class WatchdogMonitor
{
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(5), Hung = TimeSpan.FromSeconds(10);

    DateTimeOffset? _started, _lastSnapshot, _lastAction;

    public void OnSnapshot(DateTimeOffset now)
    {
        _started ??= now;
        _lastSnapshot = now;
        _lastAction = null; // the service is alive again: a later outage releases immediately
    }

    public void OnDisconnected(DateTimeOffset now) => _started ??= now;

    public WatchdogAction Decide(DateTimeOffset now, bool serviceRunning)
    {
        _started ??= now;
        if (_lastAction is { } last && now - last < Silence) return WatchdogAction.None;

        if (!serviceRunning) return Act(now, WatchdogAction.Release);

        var gap = now - (_lastSnapshot ?? _started.Value);
        return gap > Hung ? Act(now, WatchdogAction.StopServiceThenRelease) : WatchdogAction.None;
    }

    WatchdogAction Act(DateTimeOffset now, WatchdogAction action)
    {
        _lastAction = now;
        return action;
    }
}
