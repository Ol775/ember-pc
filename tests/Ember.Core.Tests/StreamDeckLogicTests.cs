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
