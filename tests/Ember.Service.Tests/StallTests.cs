using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public sealed class StallTests : IDisposable
{
    const string Sys1 = "/lpc/nct6687d/0/control/2", Fan1 = "/lpc/nct6687d/0/fan/2";
    static readonly FanControlIdentity Id = new("MS-7C56", "0.9.6");

    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-stall-" + Guid.NewGuid().ToString("N"));
    readonly ManualClock _clock = new();
    readonly FakeFanController _fans = new(new FanHeader(Sys1, "System Fan #1", true, true, false));
    readonly FanControlLoop _loop;

    public StallTests()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(Path.Combine(_dir, "settings.json"));
        store.Save(EmberSettings.Default with { Headers = [new HeaderRecord(Sys1, true, "Case", "MS-7C56", "0.9.6")] });
        _loop = new FanControlLoop(new SnapshotHub(), _fans, store, () => [], _clock, NullLogger<FanControlLoop>.Instance, Id);
        _loop.Handle(new SetMode(FanMode.SuperQuiet));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    Snapshot Snap(double rpm) => new(DateTimeOffset.UnixEpoch,
    [
        new SensorReading("cpu", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 45),
        new SensorReading(Fan1, "System Fan #1", HardwareKind.Motherboard, SensorKind.FanRpm, rpm),
    ], false, DriverStatus.Ok);

    double? LastSet() => _fans.Calls.LastOrDefault(c => c.Op == "set" && c.Id == Sys1).Value;

    void Tick(double rpm, double seconds = 1) { _clock.Advance(seconds); _loop.OnSnapshot(Snap(rpm)); }

    [Fact]
    public void A_fan_that_stops_turning_is_spun_up_at_100_then_returns_to_the_curve()
    {
        Tick(700);
        var curve = LastSet();
        Assert.True(curve < 100);
        for (var i = 0; i < 6; i++) Tick(0); // stalled, but not for long enough yet
        Assert.Equal(curve, LastSet());
        Tick(0, 2); // 8 s at 0 RPM
        Assert.Equal(100, LastSet());
        Tick(0, 5); // kick over
        Assert.Equal(curve + 5, LastSet()); // back on the curve, with a slightly higher minimum for this fan
    }

    [Fact]
    public void A_fan_that_never_turned_is_never_kicked()
    {
        for (var i = 0; i < 30; i++) Tick(0);
        Assert.True(LastSet() < 100);
    }

    [Fact]
    public void A_fan_that_does_not_recover_is_kicked_only_a_few_times()
    {
        Tick(700);
        var kicks = 0;
        for (var i = 0; i < 120; i++)
        {
            Tick(0);
            if (LastSet() == 100) { kicks++; }
        }
        Assert.InRange(kicks, 1, 15); // 3 kicks of about 4 s
    }

    [Fact]
    public void A_fan_that_comes_back_resets_the_count()
    {
        Tick(700);
        for (var i = 0; i < 9; i++) Tick(0);
        Assert.Equal(100, LastSet());
        Tick(650, 5);
        Assert.True(LastSet() < 100);
    }

    [Fact]
    public void After_a_stall_the_fan_keeps_a_higher_minimum_than_before()
    {
        Tick(700);
        var before = LastSet();
        for (var i = 0; i < 9; i++) Tick(0);
        Assert.Equal(100, LastSet());
        Tick(700, 5);
        Assert.Equal(before + 5, LastSet());
    }
}
