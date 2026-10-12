namespace Ember.Core.Tests;

public class WatchdogMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);

    [Fact]
    public void None_while_snapshots_flow()
    {
        var m = new WatchdogMonitor();
        for (var i = 0; i < 30; i++)
        {
            m.OnSnapshot(At(i));
            Assert.Equal(WatchdogAction.None, m.Decide(At(i + 0.5), true));
        }
    }

    [Fact]
    public void Release_at_once_when_service_not_running()
    {
        var m = new WatchdogMonitor();
        Assert.Equal(WatchdogAction.Release, m.Decide(T0, false));
    }

    [Fact]
    public void Pipe_down_alone_does_not_release_while_service_runs()
    {
        var m = new WatchdogMonitor();
        m.OnSnapshot(T0);
        m.OnDisconnected(At(1));
        Assert.Equal(WatchdogAction.None, m.Decide(At(8), true));
    }

    [Fact]
    public void Stop_then_release_when_service_hung_over_10s()
    {
        var m = new WatchdogMonitor();
        m.OnSnapshot(T0);
        Assert.Equal(WatchdogAction.None, m.Decide(At(10), true));
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(10.5), true));
    }

    [Fact]
    public void Repeats_release_every_5s_while_down()
    {
        var m = new WatchdogMonitor();
        Assert.Equal(WatchdogAction.Release, m.Decide(T0, false));
        Assert.Equal(WatchdogAction.None, m.Decide(At(4), false));
        Assert.Equal(WatchdogAction.Release, m.Decide(At(5), false));
        Assert.Equal(WatchdogAction.None, m.Decide(At(9), false));
        Assert.Equal(WatchdogAction.Release, m.Decide(At(10), false));
    }

    [Fact]
    public void Returns_to_none_after_snapshots_resume()
    {
        var m = new WatchdogMonitor();
        m.Decide(T0, false);
        m.OnSnapshot(At(6));
        Assert.Equal(WatchdogAction.None, m.Decide(At(7), true));
    }

    [Fact]
    public void Release_again_immediately_after_a_second_outage()
    {
        var m = new WatchdogMonitor();
        m.Decide(T0, false);
        m.OnSnapshot(At(6));
        Assert.Equal(WatchdogAction.Release, m.Decide(At(7), false));
    }

    [Fact]
    public void Boot_with_service_running_and_no_snapshot_yet_waits_60s()
    {
        var m = new WatchdogMonitor();
        m.OnDisconnected(T0);
        Assert.Equal(WatchdogAction.None, m.Decide(At(3), true));
        Assert.Equal(WatchdogAction.None, m.Decide(At(30), true));
        Assert.Equal(WatchdogAction.None, m.Decide(At(60), true));
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(61), true));
    }

    [Fact]
    public void Restarted_service_gets_a_fresh_grace_period()
    {
        var m = new WatchdogMonitor();
        m.OnSnapshot(T0);
        Assert.Equal(WatchdogAction.Release, m.Decide(At(1), false)); // killed
        Assert.Equal(WatchdogAction.None, m.Decide(At(6), true));    // restarted by Windows, still opening the hardware
        Assert.Equal(WatchdogAction.None, m.Decide(At(20), true));   // the old snapshot time must not count
        m.OnSnapshot(At(25));
        Assert.Equal(WatchdogAction.None, m.Decide(At(30), true));
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(36), true)); // hung again: 10 s rule
    }

    [Fact]
    public void Hung_service_is_not_stopped_repeatedly_within_5s()
    {
        var m = new WatchdogMonitor();
        m.OnSnapshot(T0);
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(11), true));
        Assert.Equal(WatchdogAction.None, m.Decide(At(12), true));
    }

    [Fact]
    public void Service_the_watchdog_restarted_itself_gets_a_fresh_grace_period()
    {
        var m = new WatchdogMonitor();
        m.OnSnapshot(T0);
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(11), true));
        m.OnServiceRestarted(At(25)); // the worker stopped and started the service inside that one step
        Assert.Equal(WatchdogAction.None, m.Decide(At(26), true));
        Assert.Equal(WatchdogAction.None, m.Decide(At(60), true));
        Assert.Equal(WatchdogAction.StopServiceThenRelease, m.Decide(At(86), true));
    }
}
