using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

/// <summary>Game detection and GPU fan control sit behind a user switch (Experimental) that is off until the user turns it on.</summary>
public sealed class ExperimentalTests : IDisposable
{
    const string Gpu = "/gpu-nvidia/0/control/1", Sys1 = "/lpc/nct6687d/0/control/2";
    static readonly FanControlIdentity Id = new("MS-7C56", "0.9.6");

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-exp-" + Guid.NewGuid().ToString("N"));
    readonly FakeFanController _fans;
    readonly ManualClock _clock = new();

    public ExperimentalTests()
    {
        Directory.CreateDirectory(_dir);
        _fans = new FakeFanController(new FanHeader(Sys1, "System Fan #1", true, true, false), new FanHeader(Gpu, "GPU Fan", true, true, false));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    string Path_ => Path.Combine(_dir, "settings.json");

    FanControlLoop Loop(EmberSettings s)
    {
        var store = new SettingsStore(Path_);
        store.Save(s);
        return new FanControlLoop(new SnapshotHub(), _fans, store, () => [], _clock, NullLogger<FanControlLoop>.Instance, Id);
    }

    static EmberSettings WithGpu(bool experimental) => EmberSettings.Default with
    {
        Experimental = experimental,
        GpuControlEnabled = true,
        Headers = [new HeaderRecord(Gpu, true, "Case", "MS-7C56", "0.9.6", 50)],
    };

    [Fact]
    public void Off_by_default_in_a_new_settings_file()
    {
        Assert.False(EmberSettings.Default.Experimental);
        Assert.False(Loop(EmberSettings.Default).Status.Experimental);
    }

    [Fact]
    public void Game_messages_are_refused_while_experimental_is_off()
    {
        var loop = Loop(EmberSettings.Default);
        foreach (ControlMessage m in new ControlMessage[] { new GameStarted("game.exe"), new GameEnded(), new DetectorHeartbeat(), new SetNotGame("x.exe", true), new SetGameMode(FanMode.Quiet) })
        {
            var r = loop.Handle(m);
            Assert.False(r.Ok);
            Assert.Contains("Experimental", r.Error);
        }
        Assert.Null(loop.Status.GameExe);
    }

    [Fact]
    public void Turning_the_switch_on_allows_games_and_turning_it_off_ends_the_game()
    {
        var loop = Loop(EmberSettings.Default);
        Assert.True(loop.Handle(new SetExperimental(true)).Ok);
        Assert.True(loop.Status.Experimental);
        Assert.True(loop.Handle(new GameStarted("game.exe")).Ok);
        Assert.Equal("game.exe", loop.Status.GameExe);
        Assert.True(loop.Handle(new SetExperimental(false)).Ok);
        Assert.Null(loop.Status.GameExe);
        Assert.False(loop.Handle(new GameStarted("game.exe")).Ok);
    }

    [Fact]
    public void The_switch_is_saved()
    {
        var loop = Loop(EmberSettings.Default);
        loop.Handle(new SetExperimental(true));
        Assert.True(new SettingsStore(Path_).Load().Experimental);
        loop.Handle(new SetExperimental(false));
        Assert.False(new SettingsStore(Path_).Load().Experimental);
    }

    [Fact]
    public void Gpu_fan_cannot_be_allowed_or_driven_while_experimental_is_off()
    {
        var loop = Loop(WithGpu(experimental: false));
        var r = loop.Handle(new SetGpuControl(true));
        Assert.False(r.Ok);
        Assert.Contains("Experimental", r.Error);
        Assert.False(loop.Handle(new ConfirmHeader(Gpu, true)).Ok);
        loop.OnSnapshot(new Snapshot(DateTimeOffset.UtcNow, [new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 50)], false));
        Assert.DoesNotContain(_fans.Calls, c => c.Id == Gpu);
        Assert.False(loop.Status.GpuControl);
    }

    [Fact]
    public void Turning_experimental_off_hands_the_gpu_fan_back_and_stops_driving_it()
    {
        var loop = Loop(WithGpu(experimental: true));
        var snap = new Snapshot(DateTimeOffset.UtcNow, [new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 50)], false);
        loop.OnSnapshot(snap);
        Assert.Contains(_fans.Calls, c => c.Op == "set" && c.Id == Gpu);
        loop.Handle(new SetExperimental(false));
        Assert.Contains(_fans.Calls, c => c.Op == "release" && c.Id == Gpu);
        var before = _fans.Calls.Count(c => c.Id == Gpu);
        loop.OnSnapshot(snap);
        Assert.Equal(before, _fans.Calls.Count(c => c.Id == Gpu));
        Assert.False(loop.Status.GpuControl);
    }

    [Fact]
    public void Set_experimental_travels_over_both_pipes()
    {
        Assert.True(ControlProtocol.TryDecode(ControlProtocol.Encode(new SetExperimental(true)), out var m));
        Assert.Equal(new SetExperimental(true), m);
        var status = new FanControlStatus(FanMode.Normal, ModeSource.User, false, null, [], [], null, null, false, null, true);
        Assert.True(Protocol.TryDecode(Protocol.Encode(new Snapshot(DateTimeOffset.UnixEpoch, [], false, DriverStatus.Ok, status)), out var back));
        Assert.True(back!.Control!.Experimental);
    }
}
