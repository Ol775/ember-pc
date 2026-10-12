using Ember.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

public sealed class SensorPoller(ISensorSource source, SnapshotHub hub, TimeProvider clock, ILogger<SensorPoller> log)
    : BackgroundService
{
    private IReadOnlyList<SensorReading> _previous = [];

    public Snapshot Tick()
    {
        Snapshot snap;
        try
        {
            // NaN/Infinity mean a bad sensor; drop them (exactly 0 is a valid value and is kept).
            _previous = source.Read().Where(r => double.IsFinite(r.Value)).ToList();
            snap = new Snapshot(clock.GetUtcNow(), _previous, false);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Sensor read failed; publishing stale snapshot");
            snap = new Snapshot(clock.GetUtcNow(), _previous, true);
        }
        hub.Publish(snap);
        return snap;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
