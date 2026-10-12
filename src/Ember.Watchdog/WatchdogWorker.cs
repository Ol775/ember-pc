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
                var cleanStop = StopService();
                ReleaseAll(now);
                if (cleanStop) StartService(); // a clean stop exits with 0, so Windows recovery will not bring it back
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

    /// <summary>Stops the hung service. A stop is a clean exit, so Windows will not restart it: the caller starts it again after the release.</summary>
    bool StopService()
    {
        var stoppedCleanly = false;
        try
        {
            using var sc = new ServiceController(EmberNames.ServiceName);
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            stoppedCleanly = true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Stopping the Ember service failed; ending its process");
            foreach (var p in Process.GetProcessesByName("Ember.Service"))
            {
                try { p.Kill(); } catch (Exception) { /* already gone */ } finally { p.Dispose(); }
            }
        }
        return stoppedCleanly;
    }

    void StartService()
    {
        try
        {
            using var sc = new ServiceController(EmberNames.ServiceName);
            sc.Start();
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
            if (enabled.Count == 0) return; // no header was ever tested or enabled, so Ember never wrote anything
            using var hardware = new LhmHardware();
            using var fans = new LhmFanController(hardware);
            var values = fans.Enumerate().Where(h => enabled.ContainsKey(h.Id))
                .ToDictionary(h => h.Id, h => ReleaseRules.ValueFor(h.Name, enabled[h.Id].OriginalPercent));
            FanRelease.ReleaseAll(fans, values, message => log.LogWarning("{Message}", message));
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
