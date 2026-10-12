using Ember.Core;
using LibreHardwareMonitor.Hardware;

namespace Ember.Service.Tests;

public class SensorMapperTests
{
    [Fact]
    public void Map_cpu_package_temperature()
    {
        var r = SensorMapper.Map(HardwareType.Cpu, SensorType.Temperature, "/intelcpu/0/temperature/0", "CPU Package", 55.5f);
        Assert.Equal(new SensorReading("/intelcpu/0/temperature/0", "CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 55.5), r);
    }

    [Fact]
    public void Map_nvidia_load()
    {
        var r = SensorMapper.Map(HardwareType.GpuNvidia, SensorType.Load, "/gpu-nvidia/0/load/0", "GPU Core", 40f);
        Assert.Equal(HardwareKind.Gpu, r!.Hardware);
        Assert.Equal(SensorKind.Load, r.Kind);
    }

    [Fact]
    public void Map_superio_fan_to_motherboard_rpm()
    {
        var r = SensorMapper.Map(HardwareType.SuperIO, SensorType.Fan, "/lpc/it8688e/0/fan/0", "Fan #1", 1200f);
        Assert.Equal(HardwareKind.Motherboard, r!.Hardware);
        Assert.Equal(SensorKind.FanRpm, r.Kind);
        Assert.Equal(1200, r.Value);
    }

    [Fact]
    public void Map_control_to_fan_percent()
    {
        var r = SensorMapper.Map(HardwareType.SuperIO, SensorType.Control, "/lpc/it8688e/0/control/0", "Fan Control #1", 60f);
        Assert.Equal(SensorKind.FanPercent, r!.Kind);
    }

    [Fact]
    public void Unsupported_sensor_type_returns_null() =>
        Assert.Null(SensorMapper.Map(HardwareType.Cpu, SensorType.Voltage, "/cpu/0/voltage/0", "Vcore", 1.2f));

    [Fact]
    public void Null_value_returns_null() =>
        Assert.Null(SensorMapper.Map(HardwareType.Cpu, SensorType.Temperature, "/cpu/0/temperature/0", "CPU Package", null));

    [Fact]
    public void Source_never_touches_controls()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Ember.Service", "LhmSensorSource.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Ember.Service", "LhmSensorSource.cs"));
        foreach (var banned in new[] { "IControl", "SetSoftware", "SetDefault" })
            Assert.DoesNotContain(banned, text);
    }
}
