namespace Ember.Core.Tests;

public class StreamDeckLogicTests
{
    static SensorReading R(string name, HardwareKind h, SensorKind k, double v) => new($"{h}/{name}/{k}", name, h, k, v);

    static Snapshot Snap(bool stale, params SensorReading[] r) => new(DateTimeOffset.UnixEpoch, r, stale);

    // Names as the owner's PC reports them (checked on the live status pipe).
    static readonly SensorReading[] Live =
    [
        R("CPU Fan", HardwareKind.Motherboard, SensorKind.FanPercent, 30),
        R("CPU Fan", HardwareKind.Motherboard, SensorKind.FanRpm, 511),
        R("System Fan #1", HardwareKind.Motherboard, SensorKind.FanRpm, 793),
        R("Pump Fan", HardwareKind.Motherboard, SensorKind.FanRpm, 1351),
        R("Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 55.9),
        R("CCD1 (Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 60),
        R("GPU Core", HardwareKind.Gpu, SensorKind.Temperature, 51),
        R("GPU Hot Spot", HardwareKind.Gpu, SensorKind.Temperature, 61.6),
        R("GPU Fan", HardwareKind.Gpu, SensorKind.FanRpm, 1180),
        R("GPU Fan", HardwareKind.Gpu, SensorKind.FanPercent, 39),
    ];

    [Fact]
    public void Picks_cpu_fan_by_name_and_gpu_fan_by_hardware()
    {
        var x = Readouts.From(Snap(false, Live));
        Assert.Equal(511, x.CpuFanRpm);
        Assert.Equal(1180, x.GpuFanRpm);
        Assert.Equal(60, x.CpuTemp);
        Assert.Equal(61.6, x.GpuTemp);
    }

    [Fact]
    public void Stale_snapshot_gives_all_null()
    {
        var x = Readouts.From(Snap(true, Live));
        Assert.Null(x.CpuTemp);
        Assert.Null(x.CpuFanRpm);
        Assert.Null(x.GpuTemp);
        Assert.Null(x.GpuFanRpm);
    }

    [Fact]
    public void Missing_gpu_gives_null_gpu_values()
    {
        var x = Readouts.From(Snap(false, Live.Where(r => r.Hardware != HardwareKind.Gpu).ToArray()));
        Assert.Null(x.GpuTemp);
        Assert.Null(x.GpuFanRpm);
        Assert.Equal(511, x.CpuFanRpm);
    }

    [Fact]
    public void A_stopped_fan_reads_zero_not_missing()
    {
        var x = Readouts.From(Snap(false, R("GPU Fan", HardwareKind.Gpu, SensorKind.FanRpm, 0)));
        Assert.Equal(0, x.GpuFanRpm);
    }

    [Fact]
    public void A_system_fan_is_not_mistaken_for_the_cpu_fan()
    {
        Assert.Null(Readouts.From(Snap(false, R("System Fan #1", HardwareKind.Motherboard, SensorKind.FanRpm, 800))).CpuFanRpm);
    }

    [Theory]
    [InlineData(FanMode.SuperQuiet, FanMode.Quiet)]
    [InlineData(FanMode.Quiet, FanMode.Normal)]
    [InlineData(FanMode.Normal, FanMode.Performance)]
    [InlineData(FanMode.Performance, FanMode.Extreme)]
    [InlineData(FanMode.Extreme, FanMode.SuperQuiet)]
    public void Next_steps_through_the_tray_order_and_wraps(FanMode from, FanMode to) =>
        Assert.Equal(to, StreamDeckLogic.Next(from));

    [Theory]
    [InlineData(null)]
    [InlineData(FanMode.Manual)]
    [InlineData(FanMode.Ai)]
    public void Next_from_an_unlisted_mode_goes_to_Normal(FanMode? from) =>
        Assert.Equal(FanMode.Normal, StreamDeckLogic.Next(from));

    [Fact]
    public void Cycle_order_is_exactly_the_modes_the_tray_offers() =>
        Assert.Equal([FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme], StreamDeckLogic.CycleOrder);

    [Fact]
    public void Mode_labels_match_the_apps()
    {
        Assert.Equal("Super quiet", StreamDeckLogic.ModeLabel(FanMode.SuperQuiet));
        Assert.Equal("Performance", StreamDeckLogic.ModeLabel(FanMode.Performance));
    }

    [Fact]
    public void Formats_null_as_a_dash_and_values_plainly()
    {
        Assert.Equal("-", StreamDeckLogic.FormatTemp(null));
        Assert.Equal("-", StreamDeckLogic.FormatRpm(null));
        Assert.Equal("56°", StreamDeckLogic.FormatTemp(55.9));
        Assert.Equal("1,240", StreamDeckLogic.FormatRpm(1240));
        Assert.Equal("0", StreamDeckLogic.FormatRpm(0));
    }
}

public class StreamDeckPressTests
{
    [Theory]
    [InlineData(FanMode.Quiet, FanMode.SuperQuiet)]
    [InlineData(FanMode.Normal, FanMode.Quiet)]
    [InlineData(FanMode.Extreme, FanMode.Performance)]
    [InlineData(FanMode.SuperQuiet, FanMode.Extreme)]
    public void Previous_steps_back_through_the_tray_order_and_wraps(FanMode from, FanMode to) =>
        Assert.Equal(to, StreamDeckLogic.Previous(from));

    [Theory]
    [InlineData(null)]
    [InlineData(FanMode.Manual)]
    [InlineData(FanMode.Ai)]
    public void Previous_from_an_unlisted_mode_goes_to_Normal(FanMode? from) =>
        Assert.Equal(FanMode.Normal, StreamDeckLogic.Previous(from));

    [Fact]
    public void Next_then_previous_returns_to_the_start()
    {
        foreach (var m in StreamDeckLogic.CycleOrder) Assert.Equal(m, StreamDeckLogic.Previous(StreamDeckLogic.Next(m)));
    }

    [Theory]
    [InlineData("A", "B")]
    [InlineData("B", "C")]
    [InlineData("C", "A")]
    public void Next_scene_steps_through_the_list_and_wraps(string current, string expected) =>
        Assert.Equal(expected, StreamDeckLogic.NextScene(current, ["A", "B", "C"]));

    [Fact]
    public void Next_scene_from_an_unknown_scene_goes_to_the_first() =>
        Assert.Equal("A", StreamDeckLogic.NextScene("Gone", ["A", "B"]));

    [Fact]
    public void Next_scene_with_one_scene_stays_on_it() =>
        Assert.Equal("A", StreamDeckLogic.NextScene("A", ["A"]));

    [Fact]
    public void Next_scene_with_no_scenes_is_null() =>
        Assert.Null(StreamDeckLogic.NextScene("A", []));

    [Theory]
    [InlineData(0, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(2000, true)]
    public void A_press_held_for_half_a_second_is_a_long_press(int ms, bool expected) =>
        Assert.Equal(expected, StreamDeckLogic.IsLongPress(TimeSpan.FromMilliseconds(ms)));

    [Fact]
    public void Brightness_repeats_after_the_hold_delay_at_a_steady_rate()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), StreamDeckLogic.HoldDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(200), StreamDeckLogic.RepeatInterval);
    }
}

public class StreamDeckFreshnessTests
{
    static Snapshot At(DateTimeOffset t) => new(t, [new SensorReading("a", "Core (Tctl/Tdie)", HardwareKind.Cpu, SensorKind.Temperature, 60)], false);
    static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_snapshot_from_a_moment_ago_is_used() =>
        Assert.Equal(60, Readouts.From(At(Now.AddSeconds(-2)), Now).CpuTemp);

    [Fact]
    public void A_snapshot_older_than_the_stale_limit_gives_no_readings() =>
        Assert.Null(Readouts.From(At(Now - DashboardState.StaleAfter - TimeSpan.FromSeconds(1)), Now).CpuTemp);

    [Fact]
    public void A_snapshot_exactly_at_the_limit_is_still_used() =>
        Assert.Equal(60, Readouts.From(At(Now - DashboardState.StaleAfter), Now).CpuTemp);

    [Fact]
    public void Freshness_check_matches_for_the_mode()
    {
        Assert.True(StreamDeckLogic.IsFresh(At(Now.AddSeconds(-1)), Now));
        Assert.False(StreamDeckLogic.IsFresh(At(Now.AddSeconds(-30)), Now));
        Assert.False(StreamDeckLogic.IsFresh(null, Now));
    }
}
