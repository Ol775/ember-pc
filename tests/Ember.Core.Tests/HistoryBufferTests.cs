using Ember.Core;

namespace Ember.Core.Tests;

public class HistoryBufferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Snapshot Snap(int i, double value = 0, bool stale = false) =>
        new(T0.AddSeconds(i),
            new[] { new SensorReading("cpu/temp", "CPU", HardwareKind.Cpu, SensorKind.Temperature, value) },
            stale);

    [Fact]
    public void Series_returns_values_in_time_order()
    {
        var b = new HistoryBuffer(10);
        for (var i = 0; i < 4; i++) b.Add(Snap(i, i * 10));
        var s = b.Series("cpu/temp");
        Assert.Equal(new double[] { 0, 10, 20, 30 }, s.Select(x => x.Value));
        Assert.Equal(s.OrderBy(x => x.Time).Select(x => x.Time), s.Select(x => x.Time));
    }

    [Fact]
    public void Capacity_drops_oldest()
    {
        var b = new HistoryBuffer(3);
        for (var i = 0; i < 5; i++) b.Add(Snap(i, i));
        Assert.Equal(new double[] { 2, 3, 4 }, b.Series("cpu/temp").Select(x => x.Value));
    }

    [Fact]
    public void Unknown_sensor_returns_empty()
    {
        var b = new HistoryBuffer(3);
        b.Add(Snap(0));
        Assert.Empty(b.Series("nope"));
    }

    [Fact]
    public void Stale_snapshot_is_ignored()
    {
        var b = new HistoryBuffer(3);
        b.Add(Snap(0, 1, stale: true));
        Assert.Empty(b.Series("cpu/temp"));
    }

    [Fact]
    public async Task Concurrent_adds_do_not_throw()
    {
        var b = new HistoryBuffer(100);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++) b.Add(Snap(i, i));
        })));
        Assert.Equal(100, b.Series("cpu/temp").Count);
    }
}
