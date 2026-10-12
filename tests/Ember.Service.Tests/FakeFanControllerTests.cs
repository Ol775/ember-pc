namespace Ember.Service.Tests;

public class FakeFanControllerTests
{
    private static FanHeader H(string id) => new(id, id, true, true, false);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(double.NaN)]
    public void Rejects_out_of_range_and_NaN(double p) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeFanController(H("a")).SetPercent("a", p));

    [Fact]
    public void Unknown_header_throws() =>
        Assert.Throws<InvalidOperationException>(() => new FakeFanController(H("a")).SetPercent("b", 50));

    [Fact]
    public void Records_calls_in_order()
    {
        var f = new FakeFanController(H("a"), H("b"));
        f.SetPercent("a", 40);
        f.ReleaseToAuto("b");
        Assert.Equal(new (string, string, double?)[] { ("set", "a", 40), ("release", "b", null) }, f.Calls);
    }
}
