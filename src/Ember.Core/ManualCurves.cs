namespace Ember.Core;

/// <summary>The two curves a user can edit: the CPU fan, and the case fans (which follow the hottest of CPU, GPU and board).</summary>
public enum CurveGroup { Cpu, Case }

/// <summary>The user's own curves for Manual mode. Both are always valid (see <see cref="ManualCurveRules"/>).</summary>
public sealed record ManualCurves(IReadOnlyList<CurvePoint> Cpu, IReadOnlyList<CurvePoint> Case)
{
    public static readonly ManualCurves Default = new(ModePresets.Get(FanMode.Normal).Points, ModePresets.Get(FanMode.Normal).Points);

    public IReadOnlyList<CurvePoint> For(CurveGroup group) => group == CurveGroup.Cpu ? Cpu : Case;

    public ManualCurves With(CurveGroup group, IReadOnlyList<CurvePoint> points) =>
        group == CurveGroup.Cpu ? this with { Cpu = points } : this with { Case = points };

    public bool Equals(ManualCurves? other) =>
        other is not null && Cpu.SequenceEqual(other.Cpu) && Case.SequenceEqual(other.Case);

    public override int GetHashCode() => HashCode.Combine(Cpu.Count, Case.Count);
}

/// <summary>
/// What a hand-made curve must satisfy. Besides the shape rules of <see cref="FanCurve"/> it must stay between 20 and
/// 100 °C and may not leave the fans slow when it is hot: at least 60% at 80 °C and 80% at 85 °C. (The 90 °C rule and
/// the CPU guard still apply on top.)
/// </summary>
public static class ManualCurveRules
{
    public const double MinTempC = 20, MaxTempC = 100, PercentAt80 = 60, PercentAt85 = 80;

    /// <summary>Null when the points are acceptable, otherwise a plain-language reason.</summary>
    public static string? Check(IReadOnlyList<CurvePoint>? points)
    {
        if (points is null) return "a curve needs points";
        FanCurve curve;
        try { curve = new FanCurve(points); }
        catch (ArgumentException ex) { return ex.Message.Split('(')[0].Trim(); }
        if (points[0].TempC < MinTempC || points[^1].TempC > MaxTempC) return $"temperatures must be between {MinTempC:0} and {MaxTempC:0} °C";
        if (curve.Evaluate(80) < PercentAt80 || curve.Evaluate(85) < PercentAt85)
            return $"the curve must reach at least {PercentAt80:0}% by 80 °C and {PercentAt85:0}% by 85 °C";
        return null;
    }
}
