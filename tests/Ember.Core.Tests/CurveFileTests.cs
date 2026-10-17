using Ember.Core;
using Xunit;

namespace Ember.Core.Tests;

public class CurveFileTests
{
    [Fact]
    public void Curves_round_trip()
    {
        var text = CurveFile.Export(ManualCurves.Default);
        var back = CurveFile.TryImport(text, out var problem)!;
        Assert.Null(problem);
        Assert.True(back.Equals(ManualCurves.Default));
    }

    [Fact]
    public void A_curve_that_lets_the_fans_idle_when_hot_is_refused()
    {
        var lazy = new ManualCurves([new(30, 20), new(60, 20), new(100, 20)], ManualCurves.Default.Case);
        Assert.Null(CurveFile.TryImport(CurveFile.Export(lazy), out var problem));
        Assert.Contains("80", problem);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Kind\":\"other\"}")]
    [InlineData("{\"Kind\":\"ember-fan-curves\",\"Cpu\":[[30,20]],\"Case\":[[30,20],[90,100]]}")]
    [InlineData("{\"Kind\":\"ember-fan-curves\",\"Cpu\":[[30,\"x\"],[90,100]],\"Case\":[[30,20],[90,100]]}")]
    public void Anything_else_is_refused(string text)
    {
        Assert.Null(CurveFile.TryImport(text, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_huge_file_is_refused()
    {
        Assert.Null(CurveFile.TryImport(new string(' ', CurveFile.MaxBytes + 1), out _));
    }
}
