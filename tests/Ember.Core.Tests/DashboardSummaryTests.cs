namespace Ember.Core.Tests;

public class DashboardSummaryTests
{
    private static SensorReading R(string name, HardwareKind h, SensorKind k, double v) =>
        new($"{h}/{name}", name, h, k, v);

    private static DashboardSummary S(params SensorReading[] r) =>
        DashboardSummary.From(new Snapshot(DateTimeOffset.UnixEpoch, r, false));

    [Fact]
    public void Loads_clock_and_board_temperatures_are_picked_by_name()
    {
        var x = S(R("CPU Total", HardwareKind.Cpu, SensorKind.Load, 12.4), R("Core #1", HardwareKind.Cpu, SensorKind.Load, 90),
            R("Core #1", HardwareKind.Cpu, SensorKind.Clock, 4600), R("Core #2", HardwareKind.Cpu, SensorKind.Clock, 4200),
            R("GPU Core", HardwareKind.Gpu, SensorKind.Load, 34),
            R("VRM MOS", HardwareKind.Motherboard, SensorKind.Temperature, 44), R("Chipset", HardwareKind.Motherboard, SensorKind.Temperature, 41));
        Assert.Equal(12.4, x.CpuLoad);
        Assert.Equal(4600, x.CpuClockMhz);
        Assert.Equal(34, x.GpuLoad);
        Assert.Equal(44, x.VrmTemp);
        Assert.Equal(41, x.ChipsetTemp);
    }

    [Fact]
    public void Missing_extras_are_null() => Assert.Null(S().CpuLoad);

    [Fact]
    public void Cpu_prefers_package_sensor() =>
        Assert.Equal(50, S(R("Core #1", HardwareKind.Cpu, SensorKind.Temperature, 80),
            R("CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 50)).CpuTemp);

    [Fact]
    public void Cpu_falls_back_to_hottest_core() =>
        Assert.Equal(80, S(R("Core #1", HardwareKind.Cpu, SensorKind.Temperature, 60),
            R("Core #2", HardwareKind.Cpu, SensorKind.Temperature, 80)).CpuTemp);

    [Fact]
    public void Two_gpus_use_hottest() =>
        Assert.Equal(72, S(R("GPU A", HardwareKind.Gpu, SensorKind.Temperature, 61),
            R("GPU B", HardwareKind.Gpu, SensorKind.Temperature, 72)).GpuTemp);

    [Fact]
    public void No_gpu_gives_null()
    {
        var s = S(R("CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 50));
        Assert.Null(s.GpuTemp);
        Assert.Null(s.MotherboardTemp);
    }

    [Fact]
    public void No_fans_gives_null_average_and_empty_list()
    {
        var s = S(R("CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 50));
        Assert.Null(s.AverageFanRpm);
        Assert.Empty(s.Fans);
    }

    [Fact]
    public void Fan_average_over_all_fans() =>
        Assert.Equal(800, S(R("Fan 1", HardwareKind.Motherboard, SensorKind.FanRpm, 900),
            R("Fan 2", HardwareKind.Motherboard, SensorKind.FanRpm, 700)).AverageFanRpm);

    [Fact]
    public void Fan_average_ignores_fans_reading_zero() =>
        Assert.Equal(800, S(R("Fan 1", HardwareKind.Motherboard, SensorKind.FanRpm, 900),
            R("Fan 2", HardwareKind.Motherboard, SensorKind.FanRpm, 700),
            R("Fan 3", HardwareKind.Motherboard, SensorKind.FanRpm, 0)).AverageFanRpm);

    [Fact]
    public void Fans_sorted_by_name() =>
        Assert.Equal(new[] { "A", "B" }, S(R("B", HardwareKind.Motherboard, SensorKind.FanRpm, 1),
            R("A", HardwareKind.Motherboard, SensorKind.FanRpm, 2)).Fans.Select(f => f.Name));
}
