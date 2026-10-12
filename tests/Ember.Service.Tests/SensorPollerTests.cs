using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public class SensorPollerTests
{
    private static SensorReading R(string id, double v) =>
        new(id, id, HardwareKind.Cpu, SensorKind.Temperature, v);

    private static (SensorPoller Poller, SnapshotHub Hub, FakeSensorSource Src) Make()
    {
        var src = new FakeSensorSource();
        var hub = new SnapshotHub();
        return (new SensorPoller(src, hub, TimeProvider.System, NullLogger<SensorPoller>.Instance), hub, src);
    }

    [Fact]
    public void Tick_publishes_readings_to_hub()
    {
        var (p, hub, src) = Make();
        src.OnRead = () => [R("a", 42)];
        var s = p.Tick();
        Assert.Same(s, hub.Latest);
        Assert.False(s.Stale);
        Assert.Single(s.Readings);
    }

    [Fact]
    public void Nan_and_infinity_are_dropped_but_zero_kept()
    {
        var (p, _, src) = Make();
        src.OnRead = () => [R("nan", double.NaN), R("pinf", double.PositiveInfinity),
                            R("ninf", double.NegativeInfinity), R("zero", 0), R("ok", 5)];
        var s = p.Tick();
        Assert.Equal(["zero", "ok"], s.Readings.Select(r => r.Id));
    }

    [Fact]
    public void Source_exception_publishes_stale_with_previous_readings()
    {
        var (p, hub, src) = Make();
        src.OnRead = () => [R("a", 1)];
        p.Tick();
        src.OnRead = () => throw new InvalidOperationException("boom");
        var s = p.Tick();
        Assert.True(s.Stale);
        Assert.Equal("a", Assert.Single(s.Readings).Id);
        Assert.Same(s, hub.Latest);
    }

    [Fact]
    public void Source_exception_before_first_read_publishes_empty_stale()
    {
        var (p, _, src) = Make();
        src.OnRead = () => throw new InvalidOperationException("boom");
        var s = p.Tick();
        Assert.True(s.Stale);
        Assert.Empty(s.Readings);
    }

    [Fact]
    public void Recovers_after_exception()
    {
        var (p, _, src) = Make();
        src.OnRead = () => throw new InvalidOperationException("boom");
        p.Tick();
        src.OnRead = () => [R("a", 1)];
        Assert.False(p.Tick().Stale);
    }

    [Fact]
    public void Handler_exception_does_not_stop_other_handlers()
    {
        var hub = new SnapshotHub();
        var called = false;
        hub.Published += _ => throw new InvalidOperationException("bad handler");
        hub.Published += _ => called = true;
        hub.Publish(new Snapshot(DateTimeOffset.UnixEpoch, [], false));
        Assert.True(called);
        Assert.NotNull(hub.Latest);
    }

    [Fact]
    public async Task ExecuteAsync_stops_on_cancellation()
    {
        var (p, hub, src) = Make();
        src.OnRead = () => [R("a", 1)];
        var first = new TaskCompletionSource();
        hub.Published += _ => first.TrySetResult();
        using var cts = new CancellationTokenSource();
        await p.StartAsync(cts.Token);
        await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        await p.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(p.ExecuteTask?.IsCompleted ?? true);
    }
}
