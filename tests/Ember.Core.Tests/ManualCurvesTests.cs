using System.Text.Json;

namespace Ember.Core.Tests;

public class ManualCurvesTests
{
    static CurvePoint[] P(params (double t, double p)[] pts) => pts.Select(x => new CurvePoint(x.t, x.p)).ToArray();

    [Fact]
    public void Default_curves_are_valid_and_equal_the_normal_preset()
    {
        Assert.Null(ManualCurveRules.Check(ManualCurves.Default.Cpu));
        Assert.Null(ManualCurveRules.Check(ManualCurves.Default.Case));
        Assert.Equal(ModePresets.Get(FanMode.Normal).Points, ManualCurves.Default.Cpu);
    }

    [Theory]
    [InlineData(30, 20, 90, 100, true)]
    [InlineData(30, 20, 80, 59, false)]   // under 60% at 80 °C
    [InlineData(30, 20, 85, 79, false)]   // under 80% at 85 °C (and 80 °C is interpolated from it)
    public void Hot_end_rules(double t1, double p1, double t2, double p2, bool ok)
    {
        var points = P((t1, p1), (t2, p2));
        Assert.Equal(ok, ManualCurveRules.Check(points) is null);
    }

    [Fact]
    public void Rejects_shape_and_range_errors()
    {
        Assert.NotNull(ManualCurveRules.Check(P((30, 20))));                          // one point
        Assert.NotNull(ManualCurveRules.Check(P((40, 20), (30, 100))));               // temperatures go down
        Assert.NotNull(ManualCurveRules.Check(P((30, 50), (60, 40), (90, 100))));     // percent goes down
        Assert.NotNull(ManualCurveRules.Check(P((10, 20), (90, 100))));               // below 20 °C
        Assert.NotNull(ManualCurveRules.Check(P((30, 20), (110, 100))));              // above 100 °C
        Assert.NotNull(ManualCurveRules.Check(P((30, -5), (90, 100))));
        Assert.NotNull(ManualCurveRules.Check(P((30, 20), (double.NaN, 100))));
        Assert.NotNull(ManualCurveRules.Check(null));
    }

    [Fact]
    public void Set_curve_round_trips_on_the_control_pipe()
    {
        var m = new SetCurve(CurveGroup.Case, P((30, 20), (60, 50), (90, 100)));
        Assert.True(ControlProtocol.TryDecode(ControlProtocol.Encode(m), out var back));
        var b = Assert.IsType<SetCurve>(back);
        Assert.Equal(CurveGroup.Case, b.Group);
        Assert.Equal(m.Points, b.Points);
    }

    [Theory]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Fan\",\"points\":[{\"t\":30,\"p\":20},{\"t\":90,\"p\":100}]}")]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Cpu\"}")]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Cpu\",\"points\":[{\"t\":30,\"p\":20}]}")]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Cpu\",\"points\":[null,{\"t\":90,\"p\":100}]}")]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Cpu\",\"points\":[{\"t\":30},{\"t\":90,\"p\":100}]}")]
    [InlineData("{\"v\":1,\"op\":\"setCurve\",\"group\":\"Cpu\",\"points\":[{\"t\":\"x\",\"p\":1},{\"t\":90,\"p\":100}]}")]
    public void Malformed_set_curve_is_rejected(string line) => Assert.False(ControlProtocol.TryDecode(line, out _));

    [Fact]
    public void More_than_ten_points_are_rejected()
    {
        var pts = Enumerable.Range(0, 11).Select(i => new CurvePoint(20 + i * 7, 10 + i * 9)).ToList();
        Assert.False(ControlProtocol.TryDecode(ControlProtocol.Encode(new SetCurve(CurveGroup.Cpu, pts)), out _));
    }

    [Fact]
    public void Manual_mode_can_be_picked()
    {
        Assert.True(ControlProtocol.TryDecode(ControlProtocol.Encode(new SetMode(FanMode.Manual)), out var m));
        Assert.Equal(new SetMode(FanMode.Manual), m);
    }

    [Fact]
    public void Manual_has_no_preset() => Assert.Throws<ArgumentException>(() => ModePresets.Get(FanMode.Manual));

    [Fact]
    public void Status_carries_curves_over_the_read_pipe()
    {
        var curves = new ManualCurves(P((30, 20), (70, 60), (85, 90)), P((25, 20), (90, 100)));
        var status = new FanControlStatus(FanMode.Manual, ModeSource.User, false, null, [], [], null, null, false, curves);
        var snap = new Snapshot(DateTimeOffset.UnixEpoch, [], false, DriverStatus.Ok, status);
        Assert.True(Protocol.TryDecode(Protocol.Encode(snap), out var back));
        Assert.Equal(curves, back!.Control!.Curves);
        Assert.Equal(FanMode.Manual, back.Control.Mode);
    }

    [Fact]
    public void Status_without_curves_decodes_as_defaults_and_bad_curves_are_rejected()
    {
        var plain = new Snapshot(DateTimeOffset.UnixEpoch, [], false, DriverStatus.Ok,
            new FanControlStatus(FanMode.Normal, ModeSource.User, false, null, [], [], null));
        var line = Protocol.Encode(plain);
        Assert.True(Protocol.TryDecode(line.Replace("\"mc\":{", "\"zz\":{"), out var old));
        Assert.Equal(ManualCurves.Default, old!.Control!.Curves);
        // a curve that is too slow when hot must not be drawn
        var slow = new ManualCurves(P((30, 20), (90, 50)), ManualCurves.Default.Case);
        var bad = new Snapshot(DateTimeOffset.UnixEpoch, [], false, DriverStatus.Ok,
            new FanControlStatus(FanMode.Normal, ModeSource.User, false, null, [], [], null, null, false, slow));
        Assert.False(Protocol.TryDecode(Protocol.Encode(bad), out _));
    }

    [Fact]
    public void Settings_keep_curves_and_fall_back_when_they_are_bad()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-mc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new SettingsStore(path);
            var mine = new ManualCurves(P((30, 25), (80, 70), (90, 100)), ManualCurves.Default.Case);
            store.Save(EmberSettings.Default with { NormalMode = FanMode.Manual, Curves = mine });
            var loaded = store.Load();
            Assert.Equal(FanMode.Manual, loaded.NormalMode);
            Assert.Equal(mine, loaded.Curves);

            var json = File.ReadAllText(path).Replace("\"Percent\": 70", "\"Percent\": 5");
            File.WriteAllText(path, json);
            var damaged = store.Load();
            Assert.Equal(FanMode.Manual, damaged.NormalMode);          // the rest survives
            Assert.Equal(ManualCurves.Default, damaged.Curves);        // only the curves reset
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void View_lists_manual_as_selectable()
    {
        var status = new FanControlStatus(FanMode.Manual, ModeSource.User, false, null, [], [], null);
        var view = FansViewState.Render(status, true);
        var item = view.Modes.Single(m => m.Mode == FanMode.Manual);
        Assert.True(item.Enabled);
        Assert.True(item.Selected);
        Assert.Equal(FanMode.Manual, view.Mode);
    }
}

public class PresetTests
{
    public static IEnumerable<object[]> Presets() =>
        new[] { FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme }.Select(m => new object[] { m });

    [Theory]
    [MemberData(nameof(Presets))]
    public void Every_preset_can_be_copied_into_manual(FanMode mode) =>
        Assert.Null(ManualCurveRules.Check(ModePresets.Get(mode).Points));

    [Fact]
    public void Super_quiet_is_never_louder_than_quiet_and_sits_on_the_floor_when_cool()
    {
        var sq = ModePresets.Get(FanMode.SuperQuiet);
        var q = ModePresets.Get(FanMode.Quiet);
        for (var t = 20; t <= 100; t += 2) Assert.True(sq.Evaluate(t) <= q.Evaluate(t) + 1e-9, $"at {t} C");
        Assert.Equal(ReleaseRules.SystemFloor, sq.Evaluate(45));
        Assert.Equal(100, sq.Evaluate(90));
    }

    [Fact]
    public void Super_quiet_can_be_picked_and_has_a_readable_name()
    {
        Assert.True(ControlProtocol.TryDecode(ControlProtocol.Encode(new SetMode(FanMode.SuperQuiet)), out var m));
        Assert.Equal(new SetMode(FanMode.SuperQuiet), m);
        Assert.Equal("Super quiet", FansViewState.Label(FanMode.SuperQuiet));
    }
}
