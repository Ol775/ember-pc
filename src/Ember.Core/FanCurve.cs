namespace Ember.Core;

public sealed record CurvePoint(double TempC, double Percent);

/// <summary>2 to 10 points, strictly increasing temperature, non-decreasing percent 0 to 100, linear between points, clamped outside.</summary>
public sealed class FanCurve
{
    public IReadOnlyList<CurvePoint> Points { get; }

    public FanCurve(IReadOnlyList<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count is < 2 or > 10) throw new ArgumentException("A curve needs 2 to 10 points.", nameof(points));
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!double.IsFinite(p.TempC)) throw new ArgumentException("Temperature must be finite.", nameof(points));
            if (!double.IsFinite(p.Percent) || p.Percent is < 0 or > 100) throw new ArgumentException("Percent must be 0 to 100.", nameof(points));
            if (i > 0 && p.TempC <= points[i - 1].TempC) throw new ArgumentException("Temperatures must increase.", nameof(points));
            if (i > 0 && p.Percent < points[i - 1].Percent) throw new ArgumentException("Percent must not decrease.", nameof(points));
        }
        Points = points.ToArray();
    }

    public double Evaluate(double tempC)
    {
        if (!double.IsFinite(tempC)) throw new ArgumentException("Temperature must be finite.", nameof(tempC));
        if (tempC <= Points[0].TempC) return Points[0].Percent;
        for (var i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1];
            var b = Points[i];
            if (tempC <= b.TempC) return a.Percent + (b.Percent - a.Percent) * (tempC - a.TempC) / (b.TempC - a.TempC);
        }
        return Points[^1].Percent;
    }
}

public static class ModePresets
{
    static readonly FanCurve Quiet = Make(20, 20, 25, 35, 55, 80, 100);
    static readonly FanCurve Normal = Make(25, 30, 40, 55, 70, 90, 100);
    static readonly FanCurve Performance = Make(30, 40, 55, 70, 85, 100, 100);
    static readonly FanCurve Extreme = Make(40, 55, 70, 85, 100, 100, 100);

    // Percent at 30, 40, ... 90 °C.
    static FanCurve Make(params double[] percents) =>
        new(percents.Select((p, i) => new CurvePoint(30 + 10 * i, p)).ToList());

    public static FanCurve Get(FanMode mode) => mode switch
    {
        FanMode.Quiet => Quiet,
        FanMode.Normal => Normal,
        FanMode.Performance => Performance,
        FanMode.Extreme => Extreme,
        _ => throw new ArgumentException($"No preset for {mode}.", nameof(mode)),
    };
}
