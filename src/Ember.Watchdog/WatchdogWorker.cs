using System.Diagnostics;
using System.ServiceProcess;
using Ember.Core;
using Ember.Service;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Watchdog;

/// <summary>
/// Hands every fan header back to the board's own control when the Ember service is not running or has stopped
/// answering, including once at boot before the service starts. It only reads the service's pipe (to see that it is
/// alive) and takes no commands. It opens LibreHardwareMonitor only when a release is needed, and closes it again.
/// </summary>
internal sealed class WatchdogWorker(ILogger<WatchdogWorker> log) : BackgroundService
{
    static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);
    // While the service stays down, re-opening the driver every 5 s is wasteful: repeat at most once a minute.
    static readonly TimeSpan RepeatReleaseWhileDown = TimeSpan.FromSeconds(60);

    readonly WatchdogMonitor _monitor = new();
    DateTimeOffset _lastRelease = DateTimeOffset.MinValue;
    LhmHardware? _hardware;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        var reader = Task.Run(() => ReadPipeAsync(stoppingToken), CancellationToken.None);
        using var timer = new PeriodicTimer(PollEvery);
        try
        {
            do
            {
                try { Step(); }
                catch (Exception ex) { log.LogError(ex, "Watchdog step failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
        await reader.ConfigureAwait(false);
    }

    void Step()
    {
        var now = DateTimeOffset.UtcNow;
        var running = ServiceRunning();
        switch (_monitor.Decide(now, running))
        {
            case WatchdogAction.Release:
                if (running || now - _lastRelease >= RepeatReleaseWhileDown || _lastRelease == DateTimeOffset.MinValue)
                {
                    log.LogWarning("Ember service is not running; handing fans back to the board");
                    ReleaseAll(now);
                }
                break;
            case WatchdogAction.StopServiceThenRelease:
                log.LogError("Ember service has stopped answering; stopping it and handing fans back to the board");
                EndService();
                ReleaseAll(now);
                StartServiceIfStopped(); // Windows recovery may bring it back too, after its own delay
                _monitor.OnServiceRestarted(DateTimeOffset.UtcNow); // this step blocked, so Decide never saw it down
                break;
        }
    }

    static bool ServiceRunning()
    {
        try
        {
            using var sc = new ServiceController(EmberNames.ServiceName);
            return sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending;
        }
        catch (Exception) { return false; } // not installed counts as not running
    }

    /// <summary>
    /// Ends the hung service's process. A graceful stop is no use: a frozen service never answers it, and the request
    /// blocks until the service is thawed, which would leave the fans unreleased.
    /// </summary>
    void EndService()
    {
        foreach (var p in Process.GetProcessesByName("Ember.Service"))
        {
            try { p.Kill(); p.WaitForExit(5000); }
            catch (Exception ex) { log.LogError(ex, "Could not end the Ember service process"); }
            finally { p.Dispose(); }
        }
    }

    void StartServiceIfStopped()
    {
        try
        {
            using var sc = new ServiceController(EmberNames.ServiceName);
            for (var i = 0; i < 20 && sc.Status == ServiceControllerStatus.StopPending; i++) { Thread.Sleep(250); sc.Refresh(); }
            if (sc.Status == ServiceControllerStatus.Stopped) sc.Start();
        }
        catch (Exception ex) { log.LogError(ex, "Restarting the Ember service failed"); }
    }

    void ReleaseAll(DateTimeOffset now)
    {
        _lastRelease = now;
        try
        {
            var settings = new SettingsStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ember", "settings.json")).Load();
            var enabled = settings.Headers.ToDictionary(r => r.Id); // every header Ember has a test record for may have been written
            if (enabled.Count == 0)
            {
                log.LogWarning("No tested header in settings, so there is nothing to hand back");
                return; // Ember never wrote anything
            }
            _hardware ??= new LhmHardware(); // opened once and kept: closing it would undo the safe speeds written below
            var fans = new LhmFanController(_hardware);
            var values = fans.Enumerate().Where(h => enabled.ContainsKey(h.Id))
                .ToDictionary(h => h.Id, h => ReleaseRules.ValueFor(h.Name, enabled[h.Id].OriginalPercent));
            log.LogWarning("Handing back {Count} header(s): {Values}", values.Count, string.Join(", ", values.Select(v => v.Key + "=" + v.Value)));
            FanRelease.ReleaseAll(fans, values, message => log.LogWarning("{Message}", message));
            log.LogWarning("Hand-back finished");
        }
        catch (Exception ex) { log.LogError(ex, "Could not open the hardware to release fans"); }
    }

    /// <summary>Reads the service pipe (after the same identity check the app uses) so the monitor sees snapshots.</summary>
    async Task ReadPipeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var stream = await PipeStreamConnector.ConnectAsync(ct).ConfigureAwait(false);
                await foreach (var _ in SnapshotReader.ReadAsync(stream, ct).ConfigureAwait(false))
                    _monitor.OnSnapshot(DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { /* connect failure or broken pipe: treated as disconnected below */ }

            _monitor.OnDisconnected(DateTimeOffset.UtcNow);
            try { await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
