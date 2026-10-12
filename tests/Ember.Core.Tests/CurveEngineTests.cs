namespace Ember.Core.Tests;

public class CurveEngineTests
{
    private static readonly FanCurve Normal = ModePresets.Get(FanMode.Normal);

    [Fact]
    public void First_sample_returns_curve_value() => Assert.Equal(40, new CurveEngine(5, 2).Step(50, Normal), 6);

    [Fact]
    public void Moving_average_over_window()
    {
        var e = new CurveEngine(3, 0);
        e.Step(40, Normal);
        e.Step(50, Normal);
        // average of 40, 50, 60 = 50 -> 40%
        Assert.Equal(40, e.Step(60, Normal), 6);
        // window slides: 50, 60, 70 -> 60 -> 55%
        Assert.Equal(55, e.Step(70, Normal), 6);
    }

    [Fact]
    public void Rises_immediately()
    {
        var e = new CurveEngine(1, 2);
        Assert.Equal(40, e.Step(50, Normal), 6);
        Assert.Equal(55, e.Step(60, Normal), 6);
    }

    [Fact]
    public void Does_not_fall_until_2C_below_peak()
    {
        var e = new CurveEngine(1, 2);
        var peak = e.Step(60, Normal);
        Assert.Equal(peak, e.Step(59, Normal));
        Assert.Equal(peak, e.Step(58.5, Normal));
        Assert.True(e.Step(57.9, Normal) < peak);
    }

    [Fact]
    public void Reset_clears_history_and_hysteresis()
    {
        var e = new CurveEngine(3, 2);
        e.Step(80, Normal);
        e.Reset();
        Assert.Equal(40, e.Step(50, Normal), 6);
    }

    [Fact]
    public void Switching_curve_applies_new_curve_immediately()
    {
        var e = new CurveEngine(1, 2);
        e.Step(60, ModePresets.Get(FanMode.Extreme));
        Assert.Equal(55, e.Step(60, Normal), 6);
    }

    [Fact]
    public void Non_finite_temperature_throws_ArgumentException()
    {
        var e = new CurveEngine();
        Assert.Throws<ArgumentException>(() => e.Step(double.NaN, Normal));
        Assert.Throws<ArgumentException>(() => e.Step(double.PositiveInfinity, Normal));
    }
}
