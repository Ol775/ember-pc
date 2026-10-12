namespace Ember.Core.Tests;

public class FanCurveTests
{
    private static FanCurve C(params (double t, double p)[] pts) =>
        new(pts.Select(x => new CurvePoint(x.t, x.p)).ToList());

    [Fact]
    public void Interpolates_linearly() => Assert.Equal(35, ModePresets.Get(FanMode.Normal).Evaluate(45), 6);

    [Fact]
    public void Clamps_below_first_and_above_last()
    {
        var q = ModePresets.Get(FanMode.Quiet);
        Assert.Equal(20, q.Evaluate(10));
        Assert.Equal(100, q.Evaluate(95));
    }

    [Fact]
    public void Rejects_fewer_than_2_or_more_than_10_points()
    {
        Assert.Throws<ArgumentException>(() => C((30, 20)));
        Assert.Throws<ArgumentException>(() => C(Enumerable.Range(0, 11).Select(i => (30.0 + i, 20.0 + i)).ToArray()));
        _ = C(Enumerable.Range(0, 10).Select(i => (30.0 + i, 20.0 + i)).ToArray());
    }

    [Fact]
    public void Rejects_non_increasing_temperature()
    {
        Assert.Throws<ArgumentException>(() => C((30, 20), (30, 30)));
        Assert.Throws<ArgumentException>(() => C((40, 20), (30, 30)));
    }

    [Fact]
    public void Rejects_decreasing_percent() => Assert.Throws<ArgumentException>(() => C((30, 40), (40, 30)));

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void Rejects_percent_outside_0_100_and_NaN(double p) => Assert.Throws<ArgumentException>(() => C((30, 20), (40, p)));

    [Fact]
    public void Rejects_non_finite_temperature() => Assert.Throws<ArgumentException>(() => C((double.NaN, 20), (40, 30)));

    public static IEnumerable<object[]> Table() => new[]
    {
        new object[] { FanMode.Quiet, new double[] { 20, 20, 25, 35, 55, 80, 100 } },
        new object[] { FanMode.Normal, new double[] { 25, 30, 40, 55, 70, 90, 100 } },
        new object[] { FanMode.Performance, new double[] { 30, 40, 55, 70, 85, 100, 100 } },
        new object[] { FanMode.Extreme, new double[] { 40, 55, 70, 85, 100, 100, 100 } },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Presets_match_spec_table(FanMode mode, double[] percents)
    {
        var curve = ModePresets.Get(mode);
        for (var i = 0; i < 7; i++) Assert.Equal(percents[i], curve.Evaluate(30 + 10 * i), 6);
    }

    [Fact]
    public void Ai_has_no_preset() => Assert.Throws<ArgumentException>(() => ModePresets.Get(FanMode.Ai));
}
