namespace Ember.Core;

public sealed record FanRow(string Name, double Rpm);

public sealed record DashboardSummary(double? CpuTemp, double? GpuTemp, double? MotherboardTemp, double? AverageFanRpm, IReadOnlyList<FanRow> Fans)
{
    public static DashboardSummary From(Snapshot s)
    {
        IEnumerable<SensorReading> Temps(HardwareKind h) =>
            s.Readings.Where(r => r.Hardware == h && r.Kind == SensorKind.Temperature);
        static double? Max(IEnumerable<SensorReading> r) => r.Select(x => (double?)x.Value).Max();

        var cpu = Temps(HardwareKind.Cpu).ToList();
        var package = cpu.Where(r => r.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)).ToList();
        var fans = s.Readings.Where(r => r.Kind == SensorKind.FanRpm)
            .OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => new FanRow(r.Name, r.Value)).ToList();

        return new DashboardSummary(
            Max(package.Count > 0 ? package : cpu),
            Max(Temps(HardwareKind.Gpu)),
            Max(Temps(HardwareKind.Motherboard)),
            fans.Any(f => f.Rpm > 0) ? fans.Where(f => f.Rpm > 0).Average(f => f.Rpm) : null, // unplugged or stopped fans read 0 and would drag the average down
            fans);
    }
}
