namespace Ember.Core.Tests;

public class ModeResolverTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static ModeResolver R() => new(FanMode.Normal, FanMode.Performance);

    [Fact]
    public void Starts_in_normal_mode()
    {
        var r = R();
        Assert.Equal(FanMode.Normal, r.Effective);
        Assert.Equal(ModeSource.User, r.Source);
    }

    [Fact]
    public void Game_start_switches_to_game_mode()
    {
        var r = R();
        r.GameStarted("game.exe", T0);
        Assert.Equal(FanMode.Performance, r.Effective);
        Assert.Equal(ModeSource.Game, r.Source);
        Assert.Equal("game.exe", r.GameExe);
    }

    [Fact]
    public void Game_end_returns_to_normal()
    {
        var r = R();
        r.GameStarted("game.exe", T0);
        r.GameEnded();
        Assert.Equal(FanMode.Normal, r.Effective);
        Assert.Null(r.GameExe);
    }

    [Fact]
    public void Pick_without_game_changes_normal_mode()
    {
        var r = R();
        Assert.True(r.PickMode(FanMode.Quiet));
        Assert.Equal(FanMode.Quiet, r.Effective);
        r.GameStarted("g.exe", T0);
        r.GameEnded();
        Assert.Equal(FanMode.Quiet, r.Effective);
    }

    [Fact]
    public void Pick_during_game_holds_until_game_ends()
    {
        var r = R();
        r.GameStarted("g.exe", T0);
        Assert.True(r.PickMode(FanMode.Extreme));
        Assert.Equal(FanMode.Extreme, r.Effective);
        Assert.Equal(ModeSource.GameOverride, r.Source);
    }

    [Fact]
    public void Game_end_discards_override_and_keeps_normal()
    {
        var r = R();
        r.GameStarted("g.exe", T0);
        r.PickMode(FanMode.Extreme);
        r.GameEnded();
        Assert.Equal(FanMode.Normal, r.Effective);
        Assert.Equal(ModeSource.User, r.Source);
    }

    [Fact]
    public void Second_game_clears_previous_override()
    {
        var r = R();
        r.GameStarted("a.exe", T0);
        r.PickMode(FanMode.Extreme);
        r.GameStarted("b.exe", T0.AddMinutes(1));
        Assert.Equal(FanMode.Performance, r.Effective);
        Assert.Equal("b.exe", r.GameExe);
    }

    [Fact]
    public void Ai_pick_rejected()
    {
        var r = R();
        Assert.False(r.PickMode(FanMode.Ai));
        Assert.Equal(FanMode.Normal, r.Effective);
    }

    [Fact]
    public void Expire_after_30s_without_heartbeat()
    {
        var r = R();
        r.GameStarted("g.exe", T0);
        Assert.False(r.Expire(T0.AddSeconds(30)));
        Assert.True(r.Expire(T0.AddSeconds(30.1)));
        Assert.Equal(FanMode.Normal, r.Effective);
    }

    [Fact]
    public void Heartbeat_resets_timeout()
    {
        var r = R();
        r.GameStarted("g.exe", T0);
        r.Heartbeat(T0.AddSeconds(25));
        Assert.False(r.Expire(T0.AddSeconds(50)));
        Assert.True(r.Expire(T0.AddSeconds(56)));
    }

    [Fact]
    public void Heartbeat_without_game_does_nothing()
    {
        var r = R();
        r.Heartbeat(T0);
        Assert.False(r.Expire(T0.AddMinutes(5)));
    }

    [Fact]
    public void GameStarted_with_empty_exe_throws() => Assert.Throws<ArgumentException>(() => R().GameStarted(" ", T0));

    [Fact]
    public void SetGameMode_changes_running_game()
    {
        var r = R();
        r.GameStarted("g.exe", T0);
        r.SetGameMode(FanMode.Extreme);
        Assert.Equal(FanMode.Extreme, r.Effective);
    }
}
