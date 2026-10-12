namespace Ember.Core.Tests;

public class ChartGeometryTests
{
    private static readonly DateTimeOffset End = new(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
    private static readonly TimeSpan Win = TimeSpan.FromSeconds(60);

    private static IReadOnlyList<(double X, double Y)> Run(params (DateTimeOffset, double)[] s) =>
        ChartGeometry.Scale(s, End, Win, 200, 100, 0, 100);

    [Fact]
    public void Maps_corners()
    {
        var p = Run((End - Win, 0), (End, 100));
        Assert.Equal((0d, 100d), p[0]);
        Assert.Equal((200d, 0d), p[1]);
    }

    [Fact]
    public void Clamps_out_of_range_values()
    {
        var p = Run((End, 150), (End, -20));
        Assert.Equal(0, p[0].Y);
        Assert.Equal(100, p[1].Y);
    }

    [Fact]
    public void Drops_points_before_window() =>
        Assert.Single(Run((End - Win - TimeSpan.FromSeconds(1), 50), (End, 50)));
}
