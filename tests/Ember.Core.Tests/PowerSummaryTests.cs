using Ember.Core;

namespace Ember.Core.Tests;

public class PowerSummaryTests
{
    static Snapshot Snap(params SensorReading[] r) => new(DateTimeOffset.UnixEpoch, r, false);
    static SensorReading P(HardwareKind h, string name, double w) => new(h + name, name, h, SensorKind.Power, w);

    [Fact]
    public void Prefers_package_power_for_cpu_and_gpu()
    {
        var s = PowerSummary.From(Snap(P(HardwareKind.Cpu, "CPU Cores", 40), P(HardwareKind.Cpu, "CPU Package", 65), P(HardwareKind.Gpu, "GPU Package", 180)));
        Assert.Equal(65, s.CpuW);
        Assert.Equal(180, s.GpuW);
        Assert.Null(s.BoardW);
        Assert.Equal(245, s.MeasuredW);
    }

    [Fact]
    public void No_power_sensors_means_nothing_measured()
    {
        var s = PowerSummary.From(Snap(new SensorReading("t", "CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 50)));
        Assert.Null(s.CpuW);
        Assert.Null(s.MeasuredW);
    }

    [Fact]
    public void Track_keeps_peak_average_and_energy()
    {
        var t = new PowerTrack();
        var start = DateTimeOffset.UnixEpoch;
        t.Add(start, 100);
        t.Add(start.AddSeconds(5), 200);
        Assert.Equal(200, t.Peak);
        Assert.Equal(150, t.Average);
        Assert.Equal(750.0 / 3_600_000, t.Kwh, 9);
    }

    [Fact]
    public void Track_does_not_count_a_long_gap_or_a_missing_reading()
    {
        var t = new PowerTrack();
        var start = DateTimeOffset.UnixEpoch;
        t.Add(start, 100);
        t.Add(start.AddSeconds(60), 100);
        Assert.Null(t.Average);
        t.Add(start.AddSeconds(62), null);
        Assert.Null(t.Now);
        t.Add(start.AddSeconds(64), 100);
        Assert.Null(t.Average);
    }

    [Fact]
    public void Protocol_round_trips_a_power_reading()
    {
        var line = Protocol.Encode(Snap(P(HardwareKind.Cpu, "CPU Package", 65)));
        Assert.True(Protocol.TryDecode(line, out var back));
        Assert.Equal(SensorKind.Power, back!.Readings[0].Kind);
    }
}
