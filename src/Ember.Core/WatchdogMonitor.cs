namespace Ember.Core;

public enum WatchdogAction { None, Release, StopServiceThenRelease }

/// <summary>
/// Decides when the watchdog must hand fans back to the board. Pure logic: the worker feeds it snapshot and
/// disconnect events plus whether the Windows service is running, and acts on the answer.
/// <list type="bullet">
/// <item>Service not running: release (at once, then every 5 s while it stays down).</item>
/// <item>Service running but silent: hung. A service that has already sent a snapshot since it (re)started is hung
/// after 10 s of silence; one that has not yet sent any gets 60 s, because opening the hardware can be slow right
/// after a crash restart, at boot or after resume. Then stop it and release.</item>
/// <item>Otherwise nothing. A broken pipe alone is not enough while the service runs, because releasing then could
/// fight a live service that is still driving the headers.</item>
/// </list>
/// </summary>
public sealed class WatchdogMonitor
{
    public static readonly TimeSpan Silence = TimeSpan.FromSeconds(5), Hung = TimeSpan.FromSeconds(10),
        StartGrace = TimeSpan.FromSeconds(60);

    DateTimeOffset? _started, _lastSnapshot, _lastAction;
    bool? _wasRunning;

    public void OnSnapshot(DateTimeOffset now)
    {
        _started ??= now;
        _lastSnapshot = now;
        _lastAction = null; // the service is alive again: a later outage releases immediately
    }

    public void OnDisconnected(DateTimeOffset now) => _started ??= now;

    /// <summary>The worker stopped and started the service within one step, so Decide never saw it down: start a fresh grace.</summary>
    public void OnServiceRestarted(DateTimeOffset now)
    {
        _started = now;
        _lastSnapshot = null;
    }

    public WatchdogAction Decide(DateTimeOffset now, bool serviceRunning)
    {
        _started ??= now;
        if (_wasRunning == false && serviceRunning)
        {
            // Restarted: the old snapshot time says nothing about the new process.
            _started = now;
            _lastSnapshot = null;
        }
        _wasRunning = serviceRunning;

        if (_lastAction is { } last && now - last < Silence) return WatchdogAction.None;
        if (!serviceRunning) return Act(now, WatchdogAction.Release);

        var gap = _lastSnapshot is { } s ? now - s : now - _started.Value;
        var limit = _lastSnapshot is null ? StartGrace : Hung;
        return gap > limit ? Act(now, WatchdogAction.StopServiceThenRelease) : WatchdogAction.None;
    }

    WatchdogAction Act(DateTimeOffset now, WatchdogAction action)
    {
        _lastAction = now;
        return action;
    }
}
