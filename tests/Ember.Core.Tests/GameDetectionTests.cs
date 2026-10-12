namespace Ember.Core.Tests;

public class GameDetectionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);
    private static readonly ForegroundInfo Game = new("game.exe", true);

    /// <summary>Feeds one reading per second from 0 to <paramref name="seconds"/> and returns every event.</summary>
    private static List<(double T, GameEvent E)> Run(GameDetection d, double seconds, Func<double, (ForegroundInfo, double?)> input)
    {
        var events = new List<(double, GameEvent)>();
        for (double t = 0; t <= seconds; t++)
        {
            var (fg, load) = input(t);
            if (d.Update(At(t), fg, load) is { } e) events.Add((t, e));
        }
        return events;
    }

    [Fact]
    public void Starts_after_10s_fullscreen_and_load()
    {
        var ev = Run(new GameDetection([]), 12, _ => (Game, 90));
        Assert.Equal(10, ev[0].T);
        Assert.Equal(new GameEvent.Started("game.exe"), ev[0].E);
    }

    [Fact]
    public void No_start_below_50_percent_load() =>
        Assert.Empty(Run(new GameDetection([]), 30, _ => (Game, 49.9)));

    [Fact]
    public void No_start_when_not_fullscreen() =>
        Assert.Empty(Run(new GameDetection([]), 30, _ => (new ForegroundInfo("game.exe", false), 90)));

    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("vlc.exe")]
    [InlineData("MSEDGE.EXE")]
    [InlineData("explorer.exe")]
    public void Browser_and_video_player_excluded(string exe) =>
        Assert.Empty(Run(new GameDetection([]), 30, _ => (new ForegroundInfo(exe, true), 90)));

    [Fact]
    public void NotGame_list_excludes_program() =>
        Assert.Empty(Run(new GameDetection(["Game.EXE"]), 30, _ => (Game, 90)));

    [Fact]
    public void Null_gpu_load_prevents_start() =>
        Assert.Empty(Run(new GameDetection([]), 30, _ => (Game, null)));

    [Fact]
    public void Load_dip_restarts_the_10s_wait()
    {
        var ev = Run(new GameDetection([]), 20, t => (Game, t == 5 ? 10 : 90));
        Assert.Equal(16, ev[0].T); // eligible again from t=6, then 10 s
    }

    [Fact]
    public void Ends_after_5s_not_foreground()
    {
        var d = new GameDetection([]);
        var ev = Run(d, 30, t => t < 15 ? (Game, 90) : (new ForegroundInfo("desktop.exe", false), 10));
        var ended = ev.Single(e => e.E is GameEvent.Ended);
        Assert.Equal(20, ended.T); // first seen away at t=15, 5 s later
        Assert.Null(d.ActiveGame);
    }

    [Fact]
    public void Alt_tab_for_3s_does_not_end_game()
    {
        var ev = Run(new GameDetection([]), 40, t => t is >= 20 and < 23 ? (new ForegroundInfo("chat.exe", false), 30) : (Game, 90));
        Assert.DoesNotContain(ev, e => e.E is GameEvent.Ended);
    }

    [Fact]
    public void Heartbeat_every_10s_while_active()
    {
        var ev = Run(new GameDetection([]), 41, _ => (Game, 90));
        Assert.Equal(new double[] { 20, 30, 40 }, ev.Where(e => e.E is GameEvent.Heartbeat).Select(e => e.T));
    }

    [Fact]
    public void Heartbeat_continues_during_a_short_alt_tab()
    {
        var ev = Run(new GameDetection([]), 31, t => t is >= 18 and < 22 ? (new ForegroundInfo("chat.exe", false), 30) : (Game, 90));
        Assert.Contains(ev, e => e.T == 20 && e.E is GameEvent.Heartbeat);
    }

    [Fact]
    public void Changing_foreground_game_to_another_starts_new_game()
    {
        var other = new ForegroundInfo("other.exe", true);
        var ev = Run(new GameDetection([]), 40, t => t < 15 ? (Game, 90) : (other, 90));
        Assert.Contains(ev, e => e.E.Equals(new GameEvent.Started("other.exe")) && e.T == 25);
    }

    [Fact]
    public void Marking_the_running_game_as_not_a_game_ends_it()
    {
        var d = new GameDetection([]);
        Run(d, 12, _ => (Game, 90));
        d.SetNotGames(["game.exe"]);
        Assert.IsType<GameEvent.Ended>(d.Update(At(13), Game, 90));
        Assert.Null(d.Update(At(14), Game, 90));
    }

    [Fact]
    public void Same_game_does_not_start_twice()
    {
        var ev = Run(new GameDetection([]), 60, _ => (Game, 90));
        Assert.Single(ev, e => e.E is GameEvent.Started);
    }
}
