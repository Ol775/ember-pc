namespace Ember.Core;

/// <summary>
/// What the PC's own sensors say it draws, in watts. The board itself has no power sensor on most PCs, so <see cref="BoardW"/> is
/// usually null; the part of the draw nobody measures is worked out on the Power page from a smart plug instead.
/// </summary>
public sealed record PowerSummary(double? CpuW, double? GpuW, double? BoardW)
{
    /// <summary>The sum of whatever the sensors measured, or null when they measured nothing.</summary>
    public double? MeasuredW => CpuW is null && GpuW is null && BoardW is null ? null : (CpuW ?? 0) + (GpuW ?? 0) + (BoardW ?? 0);

    public static PowerSummary From(Snapshot s)
    {
        static double? Pick(IEnumerable<SensorReading> r, params string[] preferred)
        {
            var list = r.ToList();
            if (list.Count == 0) return null;
            foreach (var p in preferred)
                if (list.FirstOrDefault(x => x.Name.Contains(p, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit.Value;
            return list.Max(x => x.Value);
        }
        IEnumerable<SensorReading> Power(HardwareKind h) => s.Readings.Where(r => r.Hardware == h && r.Kind == SensorKind.Power && r.Value >= 0);
        var board = Power(HardwareKind.Motherboard).ToList();
        return new PowerSummary(Pick(Power(HardwareKind.Cpu), "Package"), Pick(Power(HardwareKind.Gpu), "Package", "PPT", "Total"),
            board.Count == 0 ? null : board.Sum(b => b.Value));
    }
}

/// <summary>One power figure over time: the latest value, the peak, the average and the energy used since Ember started it, plus recent points for a chart.</summary>
public sealed class PowerTrack
{
    public const int MaxPoints = 600;
    const double LongestGapSeconds = 10; // a longer silence is a gap in the data, not a steady draw

    readonly List<double> _recent = [];
    DateTimeOffset? _last;
    double _lastW, _wattSeconds, _seconds;

    public double? Now { get; private set; }
    public double Peak { get; private set; }
    public double? Average => _seconds > 0 ? _wattSeconds / _seconds : null;
    public double Kwh => _wattSeconds / 3_600_000;
    public IReadOnlyList<double> Recent => _recent;

    public void Add(DateTimeOffset at, double? watts)
    {
        Now = watts;
        if (watts is not { } w || !double.IsFinite(w) || w < 0) { Now = null; _last = null; return; }
        if (_last is { } prev && (at - prev).TotalSeconds is > 0 and var dt && dt <= LongestGapSeconds)
        {
            _wattSeconds += (w + _lastW) / 2 * dt;
            _seconds += dt;
        }
        _last = at;
        _lastW = w;
        Peak = Math.Max(Peak, w);
        _recent.Add(w);
        if (_recent.Count > MaxPoints) _recent.RemoveAt(0);
    }
}
