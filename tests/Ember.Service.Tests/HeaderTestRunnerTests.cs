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
    public async Task Pump_floor_is_60()
    {
        var s = new SimFan(Header(name: "Pump Fan")) { AutoPercent = 70 };
        await Runner(s).RunAsync(Id, CancellationToken.None);
        Assert.Equal(90, s.Fans.Calls[0].Value); // 70 - 20 = 50 < 60, so raise
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
