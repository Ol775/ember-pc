using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public sealed class ManualModeTests : IDisposable
{
    const string Sys1 = "/lpc/nct6687d/0/control/2", CpuFan = "/lpc/nct6687d/0/control/0";
    static readonly FanControlIdentity Id = new("MS-7C56", "0.9.6");

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-manual-" + Guid.NewGuid().ToString("N"));
    readonly ManualClock _clock = new();
    readonly FakeFanController _fans;
    readonly SnapshotHub _hub = new();

    public ManualModeTests()
    {
        Directory.CreateDirectory(_dir);
        _fans = new FakeFanController(
            new FanHeader(CpuFan, "CPU Fan", true, true, false),
            new FanHeader(Sys1, "System Fan #1", true, true, false));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    string SettingsPath => Path.Combine(_dir, "settings.json");

    FanControlLoop Loop(EmberSettings? settings = null)
    {
        var store = new SettingsStore(SettingsPath);
        store.Save((settings ?? EmberSettings.Default) with
        {
            Headers = [new HeaderRecord(Sys1, true, "Case", "MS-7C56", "0.9.6"), new HeaderRecord(CpuFan, true, "CPU/Pump", "MS-7C56", "0.9.6")],
        });
        return new FanControlLoop(_hub, _fans, store, () => [], _clock, NullLogger<FanControlLoop>.Instance, Id);
    }

    static Snapshot Snap(double cpu) => new(DateTimeOffset.UnixEpoch,
        [new SensorReading("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, cpu)], false, DriverStatus.Ok);

    double? LastSet(string id) => _fans.Calls.LastOrDefault(c => c.Op == "set" && c.Id == id).Value;

    static CurvePoint[] P(params (double t, double p)[] pts) => pts.Select(x => new CurvePoint(x.t, x.p)).ToArray();

    [Fact]
    public void Manual_mode_follows_the_users_curve_per_group()
    {
        var loop = Loop();
        Assert.True(loop.Handle(new SetCurve(CurveGroup.Case, P((30, 40), (60, 60), (85, 90)))).Ok);
        Assert.True(loop.Handle(new SetCurve(CurveGroup.Cpu, P((30, 50), (60, 70), (85, 100)))).Ok);
        Assert.True(loop.Handle(new SetMode(FanMode.Manual)).Ok);
        loop.OnSnapshot(Snap(45));
        Assert.Equal(50, LastSet(Sys1)); // halfway from (30,40) to (60,60)
        Assert.Equal(60, LastSet(CpuFan)); // halfway from (30,50) to (60,70)
        Assert.Equal(FanMode.Manual, loop.Status.Mode);
    }

    [Fact]
    public void Editing_the_curve_applies_at_once_while_in_manual_mode()
    {
        var loop = Loop();
        loop.Handle(new SetMode(FanMode.Manual));
        loop.OnSnapshot(Snap(45)); // default curve = Normal
        var before = LastSet(Sys1);
        loop.Handle(new SetCurve(CurveGroup.Case, P((30, 80), (60, 85), (85, 100))));
        loop.OnSnapshot(Snap(45));
        Assert.NotEqual(before, LastSet(Sys1));
        Assert.True(LastSet(Sys1) > 80);
    }

    [Fact]
    public void A_curve_that_is_too_slow_when_hot_is_refused_and_changes_nothing()
    {
        var loop = Loop();
        var reply = loop.Handle(new SetCurve(CurveGroup.Case, P((30, 20), (90, 50))));
        Assert.False(reply.Ok);
        Assert.Contains("by 80", reply.Error);
        Assert.Equal(ManualCurves.Default, loop.Status.Curves);
        Assert.Equal(ManualCurves.Default, new SettingsStore(SettingsPath).Load().Curves);
    }

    [Fact]
    public void Output_never_goes_below_the_header_floor_and_cpu_guard_still_applies()
    {
        var loop = Loop();
        loop.Handle(new SetCurve(CurveGroup.Case, P((30, 0), (85, 100))));
        loop.Handle(new SetCurve(CurveGroup.Cpu, P((30, 0), (85, 100))));
        loop.Handle(new SetMode(FanMode.Manual));
        loop.OnSnapshot(Snap(30));
        Assert.Equal(ReleaseRules.SystemFloor, LastSet(Sys1));
        Assert.Equal(ReleaseRules.CpuFloor, LastSet(CpuFan));
        loop.OnSnapshot(Snap(76)); // the user's curve gives about 83%; the guard demands 85% from 75 degrees
        Assert.True(LastSet(CpuFan) >= 85);
    }

    [Fact]
    public void Over_90C_still_forces_100_in_manual_mode()
    {
        var loop = Loop();
        loop.Handle(new SetMode(FanMode.Manual));
        loop.OnSnapshot(Snap(91));
        Assert.Equal(100, LastSet(Sys1));
        Assert.True(loop.Status.SafetyOverride);
    }

    [Fact]
    public void Curves_and_mode_survive_a_restart()
    {
        var loop = Loop();
        loop.Handle(new SetCurve(CurveGroup.Cpu, P((30, 35), (70, 70), (85, 100))));
        loop.Handle(new SetMode(FanMode.Manual));
        var again = new FanControlLoop(_hub, _fans, new SettingsStore(SettingsPath), () => [], _clock, NullLogger<FanControlLoop>.Instance, Id);
        Assert.Equal(FanMode.Manual, again.Status.Mode);
        Assert.Equal(P((30, 35), (70, 70), (85, 100)), again.Status.Curves.Cpu);
    }
}