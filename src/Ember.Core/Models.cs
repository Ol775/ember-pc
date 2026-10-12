namespace Ember.Core;

public enum HardwareKind { Cpu, Gpu, Motherboard, Memory, Storage, Other }

public enum SensorKind { Temperature, FanRpm, FanPercent, Load, Clock }

public sealed record SensorReading(string Id, string Name, HardwareKind Hardware, SensorKind Kind, double Value);

/// <summary>Whether the kernel driver LibreHardwareMonitor needs for motherboard sensors (PawnIO) is installed.</summary>
public enum DriverStatus { Unknown, Ok, Missing }

public sealed record Snapshot(DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, bool Stale,
    DriverStatus Driver = DriverStatus.Unknown);
