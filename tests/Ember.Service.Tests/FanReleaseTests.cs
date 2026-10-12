namespace Ember.Service.Tests;

public class FanReleaseTests
{
    const string A = "/lpc/nct6687d/0/control/2", B = "/lpc/nct6687d/0/control/3";
    private static FanHeader H(string id) => new(id, id, true, true, false);

    [Fact]
    public void Writes_the_safe_speed_then_releases_each_header_in_the_list_only()
    {
        var fans = new FakeFanController(H(A), H(B));
        FanRelease.ReleaseAll(fans, new Dictionary<string, double> { [A] = 60 }, _ => { });
        Assert.Equal(new[] { ("set", A, (double?)60), ("release", A, (double?)null) }, fans.Calls);
    }

    [Fact]
    public void A_failing_release_falls_back_to_100_and_does_not_stop_the_others()
    {
        var fans = new FakeFanController(H(A), H(B)) { FailRelease = true };
        var log = new List<string>();
        FanRelease.ReleaseAll(fans, new Dictionary<string, double> { [A] = 60, [B] = 70 }, log.Add);
        Assert.Contains(("set", A, (double?)100), fans.Calls);
        Assert.Contains(("set", B, (double?)100), fans.Calls);
        Assert.NotEmpty(log);
    }

    [Fact]
    public void A_failing_write_falls_back_to_100()
    {
        var fans = new FakeFanController(H(A)) { FailWrite = (_, p) => p < 100 };
        FanRelease.ReleaseAll(fans, new Dictionary<string, double> { [A] = 60 }, _ => { });
        Assert.Contains(("set", A, (double?)100), fans.Calls);
    }

    [Fact]
    public void Nothing_to_release_touches_nothing()
    {
        var fans = new FakeFanController(H(A));
        FanRelease.ReleaseAll(fans, new Dictionary<string, double>(), _ => { });
        Assert.Empty(fans.Calls);
    }
}
