namespace Ember.Service.Tests;

public class HeaderTestRunnerTests
{
    const string Id = "/lpc/nct6687d/0/control/2";

    static FanHeader Header(bool rpm = true, string name = "System Fan #1") => new(Id, name, true, rpm, false);

    /// <summary>A fan whose RPM is 1000 under automatic control and 1000 * percent / 60 while Ember holds it.</summary>
    sealed class SimFan
    {
        public FakeFanController Fans { get; }
        public double Auto = 1000, AutoPercent = 60;
        public bool Responds = true;
        public SimFan(FanHeader h) => Fans = new FakeFanController(h);

        double? Held
        {
            get
            {
                var last = Fans.Calls.LastOrDefault(c => c.Id == Id);
                return last.Op == "set" ? last.Value : null;
            }
        }

        public double? Rpm(string _) => Held is { } p && Responds ? Auto * p / AutoPercent : Auto;
        public double? Percent(string _) => Held ?? AutoPercent;
    }

    static HeaderTestRunner Runner(SimFan s, Func<bool>? safety = null, Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(s.Fans, s.Rpm, s.Percent, delay ?? ((_, _) => Task.CompletedTask), safety);

    [Fact]
    public async Task Passes_when_rpm_follows_command_and_returns()
    {
        var s = new SimFan(Header());
        var r = await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.True(r.Passed, r.Message);
        Assert.Equal(("set", Id, (double?)40), s.Fans.Calls[0]);
        Assert.Equal("release", s.Fans.Calls[^1].Op);
    }

    [Fact]
    public async Task Fails_when_rpm_does_not_move()
    {
        var s = new SimFan(Header()) { Responds = false };
        var r = await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.False(r.Passed);
        Assert.Equal("release", s.Fans.Calls[^1].Op);
    }

    [Fact]
    public async Task Fails_without_rpm_sensor_and_writes_nothing()
    {
        var s = new SimFan(Header(rpm: false));
        var r = await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.False(r.Passed);
        Assert.Empty(s.Fans.Calls);
    }

    [Fact]
    public async Task Fails_when_no_fan_is_spinning()
    {
        var s = new SimFan(Header()) { Auto = 0 };
        Assert.False((await Runner(s).RunAsync(Id, CancellationToken.None)).Passed);
        Assert.Empty(s.Fans.Calls);
    }

    [Fact]
    public async Task Always_releases_after_failure()
    {
        var s = new SimFan(Header()) { Responds = false };
        await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.Equal(1, s.Fans.Calls.Count(c => c.Op == "release"));
    }

    [Fact]
    public async Task Releases_on_cancellation()
    {
        var s = new SimFan(Header());
        using var cts = new CancellationTokenSource();
        var runner = Runner(s, delay: (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(Id, cts.Token));
        Assert.Equal("release", s.Fans.Calls[^1].Op);
    }

    [Fact]
    public async Task Raises_instead_of_lowering_when_near_floor()
    {
        var s = new SimFan(Header()) { AutoPercent = 30 }; // 30 - 20 < floor 20
        await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.Equal(50, s.Fans.Calls[0].Value);
    }

    [Fact]
    public async Task Cpu_fan_floor_is_30_and_it_ends_at_100()
    {
        var s = new SimFan(Header(name: "CPU Fan")) { AutoPercent = 40 };
        var r = await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.Equal(60, s.Fans.Calls[0].Value); // 40 - 20 = 20 < floor 30, so raise
        Assert.Equal(new[] { ("set", Id, (double?)100), ("release", Id, (double?)null) }, s.Fans.Calls.Skip(1).ToArray());
        Assert.Equal(40, r.OriginalPercent);
    }

    [Fact]
    public async Task System_fan_is_restored_to_its_original_percent_before_release()
    {
        var s = new SimFan(Header());
        var r = await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.Equal(new[] { ("set", Id, (double?)40), ("set", Id, (double?)60), ("release", Id, (double?)null) }, s.Fans.Calls.ToArray());
        Assert.Equal(60, r.OriginalPercent);
    }

    [Fact]
    public async Task Refuses_second_concurrent_test()
    {
        var s = new SimFan(Header());
        var gate = new TaskCompletionSource();
        var runner = Runner(s, delay: (_, _) => gate.Task);
        var first = runner.RunAsync(Id, CancellationToken.None);
        Assert.True(runner.IsBusy);
        var second = await runner.RunAsync(Id, CancellationToken.None);
        Assert.False(second.Passed);
        gate.SetResult();
        await first;
        Assert.False(runner.IsBusy);
    }

    [Fact]
    public async Task Aborts_and_releases_when_safety_trips_mid_test()
    {
        var s = new SimFan(Header());
        var trip = false;
        var runner = Runner(s, safety: () => trip, delay: (_, _) => { trip = true; return Task.CompletedTask; });
        var r = await runner.RunAsync(Id, CancellationToken.None);
        Assert.False(r.Passed);
        Assert.Contains("safety", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("release", s.Fans.Calls[^1].Op);
    }

    [Fact]
    public async Task Refuses_during_safety_override()
    {
        var s = new SimFan(Header());
        var r = await Runner(s, safety: () => true).RunAsync(Id, CancellationToken.None);
        Assert.False(r.Passed);
        Assert.Empty(s.Fans.Calls);
    }

    [Fact]
    public async Task Unknown_header_fails_without_throwing()
    {
        var s = new SimFan(Header());
        Assert.False((await Runner(s).RunAsync("/lpc/x/0/control/9", CancellationToken.None)).Passed);
    }
}

public class GpuHeaderTestTests
{
    const string Gpu = "/gpu-nvidia/0/control/1";

    sealed class Card
    {
        public FakeFanController Fans { get; } = new(new FanHeader(Gpu, "GPU Fan", true, true, false));
        public double IdleRpm = 0;
        public bool Responds = true;

        double? Held { get { var last = Fans.Calls.LastOrDefault(c => c.Id == Gpu); return last.Op == "set" ? last.Value : null; } }
        public double? Rpm(string _) => Held is { } p && Responds ? 18.0 * p : IdleRpm;     // 1440 RPM at 80%
        public double? Percent(string _) => Held ?? 0;
    }

    static HeaderTestRunner Runner(Card c) => new(c.Fans, c.Rpm, c.Percent, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task A_card_that_sits_still_at_idle_can_pass_and_is_asked_for_a_steady_80_percent()
    {
        var c = new Card();
        var r = await Runner(c).RunAsync(Gpu, CancellationToken.None);
        Assert.True(r.Passed, r.Message);
        Assert.Equal(("set", Gpu, (double?)80), c.Fans.Calls[0]);
        Assert.Equal("release", c.Fans.Calls[^1].Op);          // handed back, even though the fan was never "spinning" to start with
    }

    [Fact]
    public async Task A_card_fan_that_does_not_turn_fails_and_is_still_handed_back()
    {
        var c = new Card { Responds = false };
        var r = await Runner(c).RunAsync(Gpu, CancellationToken.None);
        Assert.False(r.Passed);
        Assert.Equal("release", c.Fans.Calls[^1].Op);
    }

    [Fact]
    public async Task The_card_test_never_goes_below_80_percent_while_it_runs()
    {
        var c = new Card();
        await Runner(c).RunAsync(Gpu, CancellationToken.None);
        Assert.All(c.Fans.Calls.Where(x => x.Op == "set" && x.Value is { } v && v < 80), x => Assert.True(x.Value >= 30));   // only the safe release speed
        Assert.DoesNotContain(c.Fans.Calls, x => x.Op == "set" && x.Value is > 40 and < 60);       // never the card's stop-and-start zone
    }
}
