using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public sealed class FeatureGatingTests : IDisposable
{
    const string Sys1 = "/lpc/nct6687d/0/control/2", Gpu = "/gpu-nvidia/0/control/1";
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-gate-" + Guid.NewGuid().ToString("N"));
    readonly FakeFanController _fans = new(new FanHeader(Sys1, "System Fan #1", true, true, false), new FanHeader(Gpu, "GPU Fan", true, true, false));

    public FeatureGatingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    FanControlLoop Alpha(EmberSettings? settings = null)
    {
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(settings ?? EmberSettings.Default);
        return new FanControlLoop(new SnapshotHub(), _fans, store, () => [], TimeProvider.System,
            NullLogger<FanControlLoop>.Instance, new FanControlIdentity("B", "1"), new BuildFeatures(false, false));
    }

    [Fact]
    public void Production_has_game_detection_but_only_after_experimental_features_are_switched_on()
    {
        var store = new SettingsStore(Path.Combine(_dir, "prod.json"));
        store.Save(EmberSettings.Default);
        var loop = new FanControlLoop(new SnapshotHub(), _fans, store, () => [], TimeProvider.System,
            NullLogger<FanControlLoop>.Instance, new FanControlIdentity("B", "1"), BuildFeatures.Production);
        var off = loop.Handle(new GameStarted("game.exe"));
        Assert.False(off.Ok);
        Assert.Contains("Experimental", off.Error);
        Assert.False(loop.Handle(new SetGpuControl(true)).Ok);   // GPU fan control stays out of production
    }

    [Theory]
    [InlineData("game")]
    [InlineData("end")]
    [InlineData("beat")]
    [InlineData("gamemode")]
    [InlineData("notgame")]
    public void Game_detection_messages_are_refused_in_the_alpha_build(string which)
    {
        ControlMessage m = which switch
        {
            "game" => new GameStarted("game.exe"),
            "end" => new GameEnded(),
            "beat" => new DetectorHeartbeat(),
            "gamemode" => new SetGameMode(FanMode.Extreme),
            _ => new SetNotGame("chrome.exe", true),
        };
        var loop = Alpha();
        var reply = loop.Handle(m);
        Assert.False(reply.Ok);
        Assert.Contains("beta", reply.Error);
        Assert.Null(loop.Status.GameExe);
    }

    [Fact]
    public void Choosing_a_mode_still_works_in_the_alpha_build() => Assert.True(Alpha().Handle(new SetMode(FanMode.Quiet)).Ok);

    [Fact]
    public void Gpu_fan_is_refused_and_never_driven_in_the_alpha_build()
    {
        var loop = Alpha(EmberSettings.Default with
        {
            GpuControlEnabled = true,
            Headers = new[] { new HeaderRecord(Gpu, true, "Case", "B", "1", 50) },
        });
        Assert.False(loop.Handle(new SetGpuControl(true)).Ok);
        Assert.False(loop.Handle(new ConfirmHeader(Gpu, true)).Ok);
        Assert.False(loop.Handle(new RunHeaderTest(Gpu)).Ok);
        loop.OnSnapshot(new Snapshot(DateTimeOffset.UtcNow, [new("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 50)], false));
        Assert.DoesNotContain(_fans.Calls, c => c.Id == Gpu);
        Assert.Equal(HeaderState.Unknown, loop.Status.Headers.Single(h => h.Id == Gpu).State);
        Assert.False(loop.Status.GpuControl);
    }
}
