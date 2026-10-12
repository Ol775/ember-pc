using Ember.Core;
using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

internal static class SensorMapper
{
    public static SensorReading? Map(HardwareType hw, SensorType type, string id, string name, float? value)
    {
        if (value is null) return null; // not ready: never report 0
        SensorKind? kind = type switch
        {
            SensorType.Temperature => SensorKind.Temperature,
            SensorType.Fan => SensorKind.FanRpm,
            SensorType.Control => SensorKind.FanPercent,
            SensorType.Load => SensorKind.Load,
            SensorType.Clock => SensorKind.Clock,
            _ => null,
        };
        if (kind is null) return null;
        var hardware = hw switch
        {
            HardwareType.Cpu => HardwareKind.Cpu,
            HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareKind.Gpu,
            HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Motherboard,
            HardwareType.Memory => HardwareKind.Memory,
            HardwareType.Storage => HardwareKind.Storage,
            _ => HardwareKind.Other,
        };
        return new SensorReading(id, name, hardware, kind.Value, value.Value);
    }
}
