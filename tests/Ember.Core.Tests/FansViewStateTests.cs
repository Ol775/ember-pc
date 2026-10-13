namespace Ember.Core.Tests;

public class FansViewStateTests
{
    private static FanControlStatus Status(ModeSource source = ModeSource.User, string? game = null, bool safety = false,
        IReadOnlyList<HeaderStatus>? headers = null, IReadOnlyList<string>? competitors = null, FanMode mode = FanMode.Normal) =>
        new(mode, source, safety, game, headers ?? Array.Empty<HeaderStatus>(), competitors ?? Array.Empty<string>(), null);

    private static HeaderStatus H(HeaderState state, bool tested = false) => new("/lpc/nct6687d/0/control/2", "System Fan #1", state, tested);

    [Fact]
    public void Ai_item_disabled_and_other_modes_enabled()
    {
        var v = FansViewState.Render(Status(), true);
        Assert.Equal(7, v.Modes.Count);
        Assert.All(v.Modes.Where(m => m.Mode != FanMode.Ai), m => Assert.True(m.Enabled));
        var ai = v.Modes.Single(m => m.Mode == FanMode.Ai);
        Assert.False(ai.Enabled);
        Assert.Equal("AI (see AI assist below)", ai.Label);
    }

    [Fact]
    public void Selected_mode_follows_status()
    {
        var v = FansViewState.Render(Status(mode: FanMode.Quiet), true);
        Assert.Equal(FanMode.Quiet, v.Modes.Single(m => m.Selected).Mode);
    }

    [Theory]
    [InlineData(ModeSource.User, null, "Chosen by you")]
    [InlineData(ModeSource.Game, "game.exe", "Game mode: game.exe")]
    [InlineData(ModeSource.GameOverride, "game.exe", "Your pick for this game")]
    public void Source_text_for_each_ModeSource(ModeSource source, string? game, string expected) =>
        Assert.Equal(expected, FansViewState.Render(Status(source, game), true).SourceText);

    [Fact]
    public void Competitor_warning_names_apps_and_disables_enable_and_test()
    {
        var v = FansViewState.Render(Status(headers: new[] { H(HeaderState.Controllable, true) },
            competitors: new[] { "Fan Control", "iCUE" }), true);
        Assert.Contains("Fan Control and iCUE", v.CompetitorWarning);
        var row = Assert.Single(v.Headers);
        Assert.False(row.CanEnable);
        Assert.False(row.CanTest);
        Assert.Contains("Close Fan Control and iCUE", row.DisabledReason);
    }

    [Fact]
    public void Single_competitor_is_named_alone()
    {
        var v = FansViewState.Render(Status(competitors: new[] { "NZXT CAM" }), true);
        Assert.StartsWith("NZXT CAM is running", v.CompetitorWarning);
    }

    [Fact]
    public void Unavailable_service_hides_controls_with_message()
    {
        var v = FansViewState.Render(null, false);
        Assert.False(v.ServiceAvailable);
        Assert.Contains("Service not running", v.Message);
        Assert.All(v.Modes, m => Assert.False(m.Enabled));
        Assert.Empty(v.Headers);
    }

    [Fact]
    public void Readonly_header_cannot_be_enabled_or_tested()
    {
        var row = Assert.Single(FansViewState.Render(Status(headers: new[] { H(HeaderState.ReadOnly) }), true).Headers);
        Assert.False(row.CanEnable);
        Assert.False(row.CanTest);
        Assert.Contains("cannot be controlled", row.StateText);
    }

    [Fact]
    public void Untested_header_can_be_tested_but_not_enabled()
    {
        var row = Assert.Single(FansViewState.Render(Status(headers: new[] { H(HeaderState.Controllable) }), true).Headers);
        Assert.True(row.CanTest);
        Assert.False(row.CanEnable);
        Assert.Equal("Not tested", row.StateText);
        Assert.Contains("test first", row.DisabledReason);
    }

    [Fact]
    public void Tested_header_can_be_enabled_and_enabled_header_can_be_disabled()
    {
        var tested = Assert.Single(FansViewState.Render(Status(headers: new[] { H(HeaderState.Controllable, true) }), true).Headers);
        Assert.True(tested.CanEnable);
        var on = Assert.Single(FansViewState.Render(Status(headers: new[] { H(HeaderState.Enabled, true) }), true).Headers);
        Assert.True(on.CanDisable);
        Assert.False(on.CanEnable);
        Assert.False(on.CanTest);
    }

    [Fact]
    public void Unmapped_or_pump_header_is_left_to_the_board()
    {
        var row = Assert.Single(FansViewState.Render(Status(headers: new[] { new HeaderStatus("/lpc/nct6687d/0/control/1", "Pump Fan", HeaderState.Unknown, false) }), true).Headers);
        Assert.Contains("Left to the board", row.StateText);
        Assert.False(row.CanEnable);
        Assert.False(row.CanTest);
    }

    [Fact]
    public void Failed_header_can_be_retested()
    {
        var row = Assert.Single(FansViewState.Render(Status(headers: new[] { H(HeaderState.Failed) }), true).Headers);
        Assert.True(row.CanTest);
        Assert.Equal("Test failed", row.StateText);
    }

    [Fact]
    public void Safety_override_note_shown() =>
        Assert.Contains("100%", FansViewState.Render(Status(safety: true), true).SafetyNote);

    [Fact]
    public void No_safety_note_normally() => Assert.Null(FansViewState.Render(Status(), true).SafetyNote);
}

public class FansViewGamesTests
{
    [Fact]
    public void Detected_game_and_not_game_list_are_exposed()
    {
        var status = new FanControlStatus(FanMode.Normal, ModeSource.Game, false, "game.exe", Array.Empty<HeaderStatus>(),
            Array.Empty<string>(), null, new[] { "chrome.exe" });
        var v = FansViewState.Render(status, true, BuildFeatures.All);
        Assert.Equal("game.exe", v.DetectedGame);
        Assert.Equal(new[] { "chrome.exe" }, v.NotGames);
    }

    [Fact]
    public void No_game_gives_null_detected_and_empty_list()
    {
        var v = FansViewState.Render(new FanControlStatus(FanMode.Normal, ModeSource.User, false, null,
            Array.Empty<HeaderStatus>(), Array.Empty<string>(), null), true);
        Assert.Null(v.DetectedGame);
        Assert.Empty(v.NotGames!);
    }
}

public class FansViewGpuTests
{
    [Fact]
    public void Gpu_row_says_beta_when_the_build_lacks_it()
    {
        var status = new FanControlStatus(FanMode.Normal, ModeSource.User, false, null,
            new[] { new HeaderStatus("/gpu-nvidia/0/control/1", "GPU Fan", HeaderState.Unknown, false) }, Array.Empty<string>(), null);
        Assert.Contains("beta build", Assert.Single(FansViewState.Render(status, true, BuildFeatures.Production).Headers).StateText);
        Assert.Contains("Experimental features", Assert.Single(FansViewState.Render(status, true, BuildFeatures.All).Headers).StateText);
    }

    [Fact]
    public void Gpu_off_row_text_and_status_flag()
    {
        var status = new FanControlStatus(FanMode.Normal, ModeSource.User, false, null,
            new[] { new HeaderStatus("/gpu-nvidia/0/control/1", "GPU Fan", HeaderState.Unknown, false) }, Array.Empty<string>(), null, null, false, null, Experimental: true);
        var v = FansViewState.Render(status, true, BuildFeatures.All);
        Assert.Contains("GPU fan control is off", Assert.Single(v.Headers).StateText);
        Assert.False(v.GpuControl);
    }
}

public class FansViewCompetitorTests
{
    [Fact]
    public void Competitor_names_are_exposed_for_the_close_button()
    {
        var status = new FanControlStatus(FanMode.Normal, ModeSource.User, false, null, Array.Empty<HeaderStatus>(),
            new[] { "NZXT CAM", "iCUE" }, null);
        Assert.Equal(new[] { "NZXT CAM", "iCUE" }, FansViewState.Render(status, true).Competitors);
        Assert.Empty(FansViewState.Render(null, false).Competitors);
    }
}
