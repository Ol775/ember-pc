using Ember.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

public interface IDriverProbe
{
    DriverStatus Check();
}

public sealed class SensorPoller(ISensorSource source, SnapshotHub hub, TimeProvider clock, ILogger<SensorPoller> log,
    IDriverProbe? probe = null, Func<FanControlStatus?>? control = null) : BackgroundService
{
    private IReadOnlyList<SensorReading> _previous = [];

    public Snapshot Tick()
    {
        var driver = CheckDriver();
        Snapshot snap;
        try
        {
            // NaN/Infinity mean a bad sensor; drop them (exactly 0 is a valid value and is kept).
            _previous = source.Read().Where(r => double.IsFinite(r.Value)).ToList();
            snap = new Snapshot(clock.GetUtcNow(), _previous, false, driver, control?.Invoke());
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Sensor read failed; publishing stale snapshot");
            snap = new Snapshot(clock.GetUtcNow(), _previous, true, driver, control?.Invoke());
        }
        hub.Publish(snap);
        return snap;
    }

    private DriverStatus CheckDriver()
    {
        try { return probe?.Check() ?? DriverStatus.Unknown; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Driver probe failed; reporting Unknown");
            return DriverStatus.Unknown;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host start (and the pipe server) proceed without waiting for the first, possibly hung, sensor read.
        await Task.Yield();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try
        {
            do
            {
                try { Tick(); }
                catch (Exception ex) { log.LogError(ex, "Poll tick failed"); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) { }
    }
}
