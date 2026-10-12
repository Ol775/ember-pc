namespace Ember.Core.Tests;

public class SafetyMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<TempSource, double?> Cpu(double? v) =>
        new Dictionary<TempSource, double?> { [TempSource.Cpu] = v };

    [Fact]
    public void Trips_at_90() => Assert.True(new SafetyMonitor().Evaluate(T0, Cpu(90)).AllFansMax);

    [Fact]
    public void Below_90_does_not_trip() => Assert.False(new SafetyMonitor().Evaluate(T0, Cpu(89.9)).AllFansMax);

    [Fact]
    public void Stays_tripped_between_85_and_90()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(92));
        Assert.True(m.Evaluate(T0.AddSeconds(1), Cpu(87)).AllFansMax);
        Assert.True(m.Evaluate(T0.AddSeconds(2), Cpu(85)).AllFansMax);
    }

    [Fact]
    public void Releases_below_85()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(92));
        Assert.False(m.Evaluate(T0.AddSeconds(1), Cpu(84.9)).AllFansMax);
    }

    [Fact]
    public void Trip_on_one_source_applies_even_if_others_are_cool()
    {
        var m = new SafetyMonitor();
        var r = m.Evaluate(T0, new Dictionary<TempSource, double?> { [TempSource.Cpu] = 50, [TempSource.Gpu] = 91 });
        Assert.True(r.AllFansMax);
    }

    [Fact]
    public void Hot_source_that_goes_silent_keeps_trip()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(95));
        Assert.True(m.Evaluate(T0.AddSeconds(1), Cpu(null)).AllFansMax);
    }

    [Fact]
    public void Null_for_4s_is_not_missing()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(50));
        Assert.Empty(m.Evaluate(T0.AddSeconds(4), Cpu(null)).MissingSources);
    }

    [Fact]
    public void Null_for_6s_is_missing()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(50));
        Assert.Contains(TempSource.Cpu, m.Evaluate(T0.AddSeconds(6), Cpu(null)).MissingSources);
    }

    [Fact]
    public void Missing_source_recovers_when_value_returns()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(50));
        m.Evaluate(T0.AddSeconds(6), Cpu(null));
        Assert.Empty(m.Evaluate(T0.AddSeconds(7), Cpu(50)).MissingSources);
    }

    [Fact]
    public void NaN_counts_as_missing()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, Cpu(50));
        Assert.Contains(TempSource.Cpu, m.Evaluate(T0.AddSeconds(6), Cpu(double.NaN)).MissingSources);
    }

    [Fact]
    public void Source_never_seen_is_missing_after_5s_from_first_ask()
    {
        var m = new SafetyMonitor();
        Assert.Empty(m.Evaluate(T0, Cpu(null)).MissingSources);
        Assert.Empty(m.Evaluate(T0.AddSeconds(5), Cpu(null)).MissingSources);
        Assert.Contains(TempSource.Cpu, m.Evaluate(T0.AddSeconds(5.1), Cpu(null)).MissingSources);
    }

    [Fact]
    public void Only_requested_sources_are_considered()
    {
        var m = new SafetyMonitor();
        m.Evaluate(T0, new Dictionary<TempSource, double?> { [TempSource.Cpu] = 50, [TempSource.Gpu] = null });
        var r = m.Evaluate(T0.AddSeconds(6), Cpu(50));
        Assert.Empty(r.MissingSources);
    }
}
