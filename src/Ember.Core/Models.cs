namespace Ember.Core;

public enum HardwareKind { Cpu, Gpu, Motherboard, Memory, Storage, Other }

public enum SensorKind { Temperature, FanRpm, FanPercent, Load, Clock }

public sealed record SensorReading(string Id, string Name, HardwareKind Hardware, SensorKind Kind, double Value);

public sealed record Snapshot(DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, bool Stale);
