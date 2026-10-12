namespace Ember.Service.Tests;

public class FanReleaseTests
{
    const string A = "/lpc/nct6687d/0/control/2", B = "/lpc/nct6687d/0/control/3";
    private static FanHeader H(string id) => new(id, id, true, true, false);

    [Fact]
    public void Releases_every_controllable_header_and_leaves_clean_ones_alone()
    {
        var fans = new FakeFanController(H(A), H(B), new FanHeader("/lpc/x/0/control/9", "RO", false, false, false));
        FanRelease.ReleaseAll(fans, _ => { });
        Assert.Equal(new[] { ("release", A, (double?)null), ("release", B, (double?)null) }, fans.Calls);
    }

    [Fact]
    public void A_header_still_in_software_mode_after_release_is_forced_to_100()
    {
        var fans = new FakeFanController(H(A), H(B));
        fans.Stuck.Add(A);
        FanRelease.ReleaseAll(fans, _ => { });
        Assert.Contains(("set", A, (double?)100), fans.Calls);
        Assert.DoesNotContain(fans.Calls, c => c.Op == "set" && c.Id == B);
    }

    [Fact]
    public void A_failing_release_does_not_stop_the_others_and_is_forced_to_100()
    {
        var fans = new FakeFanController(H(A), H(B)) { FailRelease = true };
        var log = new List<string>();
        FanRelease.ReleaseAll(fans, log.Add);
        Assert.Contains(("set", A, (double?)100), fans.Calls);
        Assert.Contains(("set", B, (double?)100), fans.Calls);
        Assert.NotEmpty(log);
    }
}
