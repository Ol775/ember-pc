using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
}

public sealed class FanControlLoopTests : IDisposable
{
    const string Sys1 = "/lpc/nct6687d/0/control/2", Sys2 = "/lpc/nct6687d/0/control/3";
    const string Gpu = "/gpu-nvidia/0/control/1";
    const string CpuFan = "/lpc/nct6687d/0/control/0", Pump = "/lpc/nct6687d/0/control/1";
    static readonly FanControlIdentity Id = new("MS-7C56", "0.9.6");

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-loop-" + Guid.NewGuid().ToString("N"));
    readonly ManualClock _clock = new();
    readonly FakeFanController _fans;
    readonly SnapshotHub _hub = new();
    IReadOnlyList<string> _processes = [];

    public FanControlLoopTests()
    {
        Directory.CreateDirectory(_dir);
        _fans = new FakeFanController(
            new FanHeader(CpuFan, "CPU Fan", true, true, false),
            new FanHeader(Pump, "Pump Fan", true, true, false),
            new FanHeader(Sys1, "System Fan #1", true, true, false),
            new FanHeader(Sys2, "System Fan #2", true, true, true),
            new FanHeader(Gpu, "GPU Fan", true, true, false));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    string SettingsPath => Path.Combine(_dir, "settings.json");

    static HeaderRecord Rec(string id, bool enabled = true, string board = "MS-7C56", string version = "0.9.6") =>
        new(id, enabled, "Case", board, version);

    FanControlLoop Loop(params HeaderRecord[] headers) => Loop(EmberSettings.Default with { Headers = headers });

    FanControlLoop Loop(EmberSettings settings)
    {
        var store = new SettingsStore(SettingsPath);
        store.Save(settings);
        return new FanControlLoop(_hub, _fans, store, () => _processes, _clock, NullLogger<FanControlLoop>.Instance, Id);
    }

    static Snapshot Snap(double? cpu = 40, double? gpu = 40, double? board = 40, bool stale = false) => new(
        new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
        new[]
        {
            cpu is { } c ? new SensorReading("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, c) : null,
            gpu is { } g ? new SensorReading("gpu", "GPU Core", HardwareKind.Gpu, SensorKind.Temperature, g) : null,
            board is { } b ? new SensorReading("mb", "System", HardwareKind.Motherboard, SensorKind.Temperature, b) : null,
        }.Where(r => r is not null).Select(r => r!).ToList(), stale, DriverStatus.Ok);

    double? LastSet(string id) => _fans.Calls.LastOrDefault(c => c.Op == "set" && c.Id == id).Value;

    [Fact]
    public void Drives_only_enabled_tested_headers()
    {
        var loop = Loop(Rec(Sys1), Rec(Sys2, enabled: false), Rec(CpuFan, board: "OTHER"), Rec(Pump, version: "0.0.1"));
        loop.OnSnapshot(Snap());
        Assert.Equal(new[] { Sys1 }, _fans.Calls.Select(c => c.Id).Distinct());
    }

    [Fact]
    public void Uses_curve_for_effective_mode()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50, 30, 30)); // Normal curve at 50 °C = 40
        Assert.Equal(40, LastSet(Sys1));
        Assert.Equal(ModeSource.User, loop.Status.Source);
    }

    [Fact]
    public void Game_start_switches_curve_and_game_end_restores()
    {
        var loop = Loop(Rec(Sys1));
        loop.Handle(new GameStarted("game.exe"));
        loop.OnSnapshot(Snap(50)); // Performance at 50 = 55
        Assert.Equal(55, LastSet(Sys1));
        Assert.Equal("game.exe", loop.Status.GameExe);
        loop.Handle(new GameEnded());
        loop.OnSnapshot(Snap(50));
        Assert.Equal(40, LastSet(Sys1));
    }

    [Fact]
    public void Manual_pick_during_game_holds_until_game_ends()
    {
        var loop = Loop(Rec(Sys1));
        loop.Handle(new GameStarted("game.exe"));
        loop.Handle(new SetMode(FanMode.Extreme));
        loop.OnSnapshot(Snap(50)); // Extreme at 50 = 70
        Assert.Equal(70, LastSet(Sys1));
        Assert.Equal(ModeSource.GameOverride, loop.Status.Source);
        loop.Handle(new GameEnded());
        loop.OnSnapshot(Snap(50));
        Assert.Equal(40, LastSet(Sys1));
        Assert.Equal(FanMode.Normal, new SettingsStore(SettingsPath).Load().NormalMode); // override was not persisted
    }

    [Fact]
    public void Floor_applied_20_and_60_for_pump()
    {
        var loop = Loop(Rec(Sys1), Rec(Pump));
        loop.Handle(new SetMode(FanMode.Quiet));
        loop.OnSnapshot(Snap(30, 30, 30)); // Quiet at 30 = 20
        Assert.Equal(20, LastSet(Sys1));
        Assert.Equal(60, LastSet(Pump));
    }

    [Fact]
    public void Over_90C_forces_100_on_all_headers_and_releases_below_85()
    {
        var loop = Loop(Rec(Sys1), Rec(CpuFan));
        loop.OnSnapshot(Snap(40, 91, 40));
        Assert.Equal(100, LastSet(Sys1));
        Assert.Equal(100, LastSet(CpuFan));
        Assert.True(loop.Status.SafetyOverride);
        loop.OnSnapshot(Snap(40, 86, 40));
        Assert.Equal(100, LastSet(Sys1));
        loop.OnSnapshot(Snap(40, 60, 40));
        Assert.False(loop.Status.SafetyOverride);
        Assert.True(LastSet(Sys1) < 100);
    }

    [Fact]
    public void All_sources_missing_for_6s_forces_100_for_that_group()
    {
        var loop = Loop(Rec(Sys1), Rec(CpuFan));
        loop.OnSnapshot(Snap(40, 40, 40));
        _clock.Advance(6);
        loop.OnSnapshot(Snap(null, null, null));
        Assert.Equal(100, LastSet(Sys1));
        Assert.Equal(100, LastSet(CpuFan));
    }

    [Fact]
    public void Case_group_goes_to_100_when_any_source_it_uses_goes_blind()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(40, 40, 40));
        _clock.Advance(6);
        loop.OnSnapshot(Snap(50, null, null)); // GPU and motherboard sensors vanished: the case fans must not trust CPU alone
        Assert.Equal(100, LastSet(Sys1));
    }

    [Fact]
    public void No_gpu_machine_still_controls_case_fans()
    {
        var loop = Loop(Rec(Sys1));
        for (var i = 0; i < 10; i++)
        {
            _clock.Advance(1);
            loop.OnSnapshot(Snap(50, null, 45));
        }
        Assert.Equal(40, LastSet(Sys1));
    }

    [Fact]
    public async Task Stop_releases_every_written_header()
    {
        var loop = Loop(Rec(Sys1), Rec(CpuFan));
        await loop.StartAsync(CancellationToken.None);
        _hub.Publish(Snap());
        await loop.StopAsync(CancellationToken.None);
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
        Assert.Contains(("release", CpuFan, (double?)null), _fans.Calls);
    }

    [Fact]
    public void Unmanaged_manual_header_is_not_touched()
    {
        var loop = Loop(Rec(Sys1)); // Sys2 is InSoftwareMode but not enabled
        loop.OnSnapshot(Snap());
        Assert.DoesNotContain(_fans.Calls, c => c.Id == Sys2);
    }

    [Fact]
    public void Competitor_running_stops_writes_and_sets_status()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap());
        var writes = _fans.Calls.Count(c => c.Op == "set");
        _processes = new[] { "FanControl" };
        _clock.Advance(6);
        loop.OnSnapshot(Snap());
        Assert.Equal(new[] { "Fan Control" }, loop.Status.Competitors);
        Assert.Equal(writes, _fans.Calls.Count(c => c.Op == "set"));
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
        _processes = [];
        _clock.Advance(6);
        loop.OnSnapshot(Snap());
        Assert.Empty(loop.Status.Competitors);
        Assert.True(_fans.Calls.Count(c => c.Op == "set") > writes);
    }

    [Fact]
    public void Corrupt_settings_drive_nothing()
    {
        File.WriteAllText(SettingsPath, "{ not json");
        var loop = new FanControlLoop(_hub, _fans, new SettingsStore(SettingsPath), () => _processes, _clock,
            NullLogger<FanControlLoop>.Instance, Id);
        loop.OnSnapshot(Snap());
        Assert.Empty(_fans.Calls);
    }

    [Fact]
    public void Game_mode_expires_after_30s_without_heartbeat()
    {
        var loop = Loop(Rec(Sys1));
        loop.Handle(new GameStarted("game.exe"));
        _clock.Advance(31);
        loop.OnSnapshot(Snap());
        Assert.Null(loop.Status.GameExe);
        Assert.Equal(FanMode.Normal, loop.Status.Mode);
    }

    [Fact]
    public void Confirm_refused_without_passing_test()
    {
        var loop = Loop(Rec(Sys1, enabled: false, board: "OTHER"));
        var reply = loop.Handle(new ConfirmHeader(Sys1, true));
        Assert.False(reply.Ok);
        Assert.False(loop.Handle(new ConfirmHeader(CpuFan, true)).Ok); // no record at all
        Assert.True(loop.Handle(new ConfirmHeader(Sys1, false)).Ok);
    }

    [Fact]
    public void Confirm_accepted_with_passing_test_and_persisted()
    {
        var loop = Loop(Rec(Sys1, enabled: false));
        Assert.True(loop.Handle(new ConfirmHeader(Sys1, true)).Ok);
        Assert.True(new SettingsStore(SettingsPath).Load().Headers.Single().Enabled);
        loop.OnSnapshot(Snap());
        Assert.NotNull(LastSet(Sys1));
    }

    [Fact]
    public void Confirm_refused_while_competitor_runs()
    {
        _processes = new[] { "iCUE" };
        var loop = Loop(Rec(Sys1, enabled: false));
        loop.OnSnapshot(Snap());
        var reply = loop.Handle(new ConfirmHeader(Sys1, true));
        Assert.False(reply.Ok);
        Assert.Contains("iCUE", reply.Error);
    }

    [Fact]
    public void Settings_saved_after_mode_change()
    {
        var loop = Loop();
        loop.Handle(new SetMode(FanMode.Quiet));
        loop.Handle(new SetGameMode(FanMode.Extreme));
        loop.Handle(new SetNotGame("Chrome.exe", true));
        var s = new SettingsStore(SettingsPath).Load();
        Assert.Equal(FanMode.Quiet, s.NormalMode);
        Assert.Equal(FanMode.Extreme, s.GameMode);
        Assert.Equal(new[] { "chrome.exe" }, s.NotGames);
    }

    [Fact]
    public void Status_lists_not_games()
    {
        var loop = Loop();
        loop.Handle(new SetNotGame("Chrome.exe", true));
        Assert.Equal(new[] { "chrome.exe" }, loop.Status.NotGames);
        loop.Handle(new SetNotGame("chrome.exe", false));
        Assert.Empty(loop.Status.NotGames);
    }

    [Fact]
    public void Not_a_game_is_ignored_when_it_reports_a_game()
    {
        var loop = Loop(EmberSettings.Default with { NotGames = new[] { "chrome.exe" } });
        loop.Handle(new GameStarted("Chrome.exe"));
        Assert.Null(loop.Status.GameExe);
    }

    [Fact]
    public void Competitor_appears_in_status_within_5s()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap());
        _processes = new[] { "iCUE" };
        _clock.Advance(4);
        loop.OnSnapshot(Snap());
        Assert.Empty(loop.Status.Competitors);
        _clock.Advance(1.5);
        loop.OnSnapshot(Snap());
        Assert.Equal(new[] { "iCUE" }, loop.Status.Competitors);
    }

    [Fact]
    public void Process_scan_failure_leaves_previous_result()
    {
        var fail = false;
        var store = new SettingsStore(SettingsPath);
        store.Save(EmberSettings.Default);
        var loop = new FanControlLoop(_hub, _fans, store, () => fail ? throw new InvalidOperationException("denied") : new[] { "iCUE" },
            _clock, NullLogger<FanControlLoop>.Instance, Id);
        loop.OnSnapshot(Snap());
        fail = true;
        _clock.Advance(6);
        loop.OnSnapshot(Snap());
        Assert.Equal(new[] { "iCUE" }, loop.Status.Competitors);
    }

    [Fact]
    public async Task Header_test_via_control_message_records_pass_then_confirm_works()
    {
        var loop = Loop();
        double? Held() { var l = _fans.Calls.LastOrDefault(c => c.Id == Sys1); return l.Op == "set" ? l.Value : null; }
        loop.Runner = new HeaderTestRunner(_fans, _ => Held() is { } p ? 1000 * p / 60 : 1000, _ => Held() ?? 60,
            (_, _) => Task.CompletedTask);
        Assert.True(loop.Handle(new RunHeaderTest(Sys1)).Ok);
        for (var i = 0; i < 200 && !loop.Status.Headers.Any(h => h.Id == Sys1 && h.Tested); i++) await Task.Delay(10);
        Assert.Contains(loop.Status.Headers, h => h.Id == Sys1 && h.Tested && h.State == HeaderState.Controllable);
        Assert.StartsWith("Passed", loop.Status.TestMessage);
        Assert.True(loop.Handle(new ConfirmHeader(Sys1, true)).Ok);
    }

    [Fact]
    public void Header_test_refused_without_runner_or_when_enabled_or_with_competitor()
    {
        var loop = Loop(Rec(Sys1));
        Assert.False(loop.Handle(new RunHeaderTest(Sys1)).Ok); // no runner
        loop.Runner = new HeaderTestRunner(_fans, _ => 1000, _ => 60, (_, _) => Task.CompletedTask);
        Assert.False(loop.Handle(new RunHeaderTest(Sys1)).Ok); // enabled
        loop.Handle(new ConfirmHeader(Sys1, false));
        _processes = new[] { "FanControl" };
        loop.OnSnapshot(Snap());
        Assert.False(loop.Handle(new RunHeaderTest(Sys1)).Ok); // competitor
    }

    [Fact]
    public void Gpu_header_ignored_when_gpu_control_disabled()
    {
        var loop = Loop(Rec(Gpu));
        loop.OnSnapshot(Snap());
        Assert.DoesNotContain(_fans.Calls, c => c.Id == Gpu);
        Assert.Equal(HeaderState.Unknown, loop.Status.Headers.Single(h => h.Id == Gpu).State);
        Assert.False(loop.Handle(new ConfirmHeader(Gpu, true)).Ok);
    }

    [Fact]
    public void Gpu_floor_is_30_and_turning_it_off_releases_it()
    {
        var loop = Loop(EmberSettings.Default with { GpuControlEnabled = true, Headers = new[] { Rec(Gpu) } });
        loop.Handle(new SetMode(FanMode.Quiet));
        loop.OnSnapshot(Snap(30, 30, 30)); // Quiet at 30 = 20, floor 30
        Assert.Equal(30, LastSet(Gpu));
        Assert.True(loop.Status.GpuControl);
        loop.Handle(new SetGpuControl(false));
        Assert.Contains(("release", Gpu, (double?)null), _fans.Calls);
        Assert.False(new SettingsStore(SettingsPath).Load().GpuControlEnabled);
    }

    [Fact]
    public void Failed_gpu_test_means_no_writes()
    {
        var loop = Loop(EmberSettings.Default with { GpuControlEnabled = true });
        loop.RecordTestResult(Gpu, false, "no response");
        loop.OnSnapshot(Snap());
        Assert.DoesNotContain(_fans.Calls, c => c.Id == Gpu);
        Assert.Equal(HeaderState.Failed, loop.Status.Headers.Single(h => h.Id == Gpu).State);
    }

    [Fact]
    public void Write_failure_falls_back_to_100_and_marks_header_failed()
    {
        var loop = Loop(Rec(Sys1));
        _fans.FailWrite = (_, p) => p < 100;
        loop.OnSnapshot(Snap(50));
        Assert.Equal(100, LastSet(Sys1));
        Assert.Equal(HeaderState.Failed, loop.Status.Headers.Single(h => h.Id == Sys1).State);
    }

    [Fact]
    public void Write_failure_with_no_fallback_possible_releases_the_header()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50));
        _fans.FailWrite = (_, _) => true;
        loop.OnSnapshot(Snap(60));
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
    }

    [Fact]
    public void Enumerate_failure_releases_everything_written()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50));
        _fans.EnumerateThrows = true;
        loop.OnSnapshot(Snap(50));
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
    }

    [Fact]
    public void Failed_release_is_retried_on_the_next_step()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50));
        _processes = new[] { "FanControl" };
        _fans.FailRelease = true;
        _clock.Advance(6);
        loop.OnSnapshot(Snap(50));
        Assert.DoesNotContain(_fans.Calls, c => c.Op == "release");
        _fans.FailRelease = false;
        loop.OnSnapshot(Snap(50));
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
    }

    [Fact]
    public void Failed_release_after_disable_is_retried()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50));
        _fans.FailRelease = true;
        loop.Handle(new ConfirmHeader(Sys1, false));
        Assert.DoesNotContain(_fans.Calls, c => c.Op == "release");
        _fans.FailRelease = false;
        loop.OnSnapshot(Snap(50));
        Assert.Contains(("release", Sys1, (double?)null), _fans.Calls);
    }

    [Fact]
    public async Task Snapshot_after_stop_does_not_write()
    {
        var loop = Loop(Rec(Sys1));
        await loop.StartAsync(CancellationToken.None);
        loop.OnSnapshot(Snap(50));
        await loop.StopAsync(CancellationToken.None);
        var calls = _fans.Calls.Count;
        loop.OnSnapshot(Snap(50));
        Assert.Equal(calls, _fans.Calls.Count);
        Assert.False(loop.Handle(new SetMode(FanMode.Quiet)).Ok);
    }

    [Fact]
    public void NotGames_list_is_capped()
    {
        var loop = Loop();
        ControlReply last = new(true, null);
        for (var i = 0; i < 250; i++) last = loop.Handle(new SetNotGame($"game{i}.exe", true));
        Assert.False(last.Ok);
        Assert.True(new SettingsStore(SettingsPath).Load().NotGames.Count <= FanControlLoop.MaxNotGames);
        Assert.True(loop.Handle(new SetNotGame("game0.exe", false)).Ok); // removing still works
    }

    [Fact]
    public async Task Second_test_request_is_refused_while_the_first_is_starting()
    {
        var loop = Loop();
        var gate = new TaskCompletionSource();
        loop.Runner = new HeaderTestRunner(_fans, _ => 1000, _ => 60, (_, _) => gate.Task);
        Assert.True(loop.Handle(new RunHeaderTest(Sys1)).Ok);
        Assert.False(loop.Handle(new RunHeaderTest(Sys2)).Ok);
        gate.SetResult();
        for (var i = 0; i < 300 && loop.Status.TestMessage == "Testing2026"; i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Confirm_refused_while_that_header_is_being_tested()
    {
        var loop = Loop(Rec(Sys1, enabled: false));
        var gate = new TaskCompletionSource();
        loop.Runner = new HeaderTestRunner(_fans, _ => 1000, _ => 60, (_, _) => gate.Task);
        Assert.True(loop.Handle(new RunHeaderTest(Sys1)).Ok);
        Assert.False(loop.Handle(new ConfirmHeader(Sys1, true)).Ok);
        gate.SetResult();
        for (var i = 0; i < 300 && loop.Status.TestMessage == "Testing2026"; i++) await Task.Delay(10);
    }

    [Fact]
    public void Unmapped_header_name_cannot_be_enabled_or_tested()
    {
        const string odd = "/lpc/other/0/control/9";
        var fans = new FakeFanController(new FanHeader(odd, "Fan #1", true, true, false));
        var store = new SettingsStore(SettingsPath);
        store.Save(EmberSettings.Default with { Headers = new[] { Rec(odd, enabled: false) } });
        var loop = new FanControlLoop(_hub, fans, store, () => _processes, _clock, NullLogger<FanControlLoop>.Instance, Id);
        loop.OnSnapshot(Snap());
        Assert.Equal(HeaderState.Unknown, loop.Status.Headers.Single().State);
        Assert.False(loop.Handle(new ConfirmHeader(odd, true)).Ok);
    }

    [Fact]
    public void Ai_mode_is_rejected()
    {
        var loop = Loop();
        Assert.False(loop.Handle(new SetMode(FanMode.Ai)).Ok);
    }

    [Fact]
    public void Stale_snapshot_holds_value_then_goes_to_100_after_5s()
    {
        var loop = Loop(Rec(Sys1));
        loop.OnSnapshot(Snap(50));
        var before = LastSet(Sys1);
        _clock.Advance(2);
        loop.OnSnapshot(Snap(50, stale: true));
        Assert.Equal(before, LastSet(Sys1));
        _clock.Advance(6);
        loop.OnSnapshot(Snap(50, stale: true));
        Assert.Equal(100, LastSet(Sys1));
    }
}
