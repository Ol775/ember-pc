namespace Ember.Lighting;

/// <summary>Fills that run across the whole canvas, so several devices side by side share one gradient or rainbow.</summary>
public static class CanvasPaint
{
    /// <summary>
    /// One paint list per enabled device that has LEDs. Every LED is placed where it sits on the canvas, and its colour comes
    /// from <paramref name="ramp"/> (0 to 1) according to how far along the direction <paramref name="angleDegrees"/> it is
    /// (0 left to right, 90 top to bottom) over all the LEDs together.
    /// </summary>
    public static Dictionary<string, IReadOnlyList<string?>> Across(IReadOnlyList<DevicePlacement> placements, IReadOnlyDictionary<string, int> ledCounts,
        IReadOnlyDictionary<string, int> columns, double angleDegrees, Func<double, Color> ramp)
    {
        var angle = angleDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        var along = new Dictionary<string, double[]>();
        var (lo, hi) = (double.MaxValue, double.MinValue);
        foreach (var p in placements)
        {
            if (!p.Enabled || !ledCounts.TryGetValue(p.DeviceId, out var count) || count <= 0 || along.ContainsKey(p.DeviceId)) continue;
            count = Math.Min(count, PaintTools.MaxLeds);
            var cols = columns.TryGetValue(p.DeviceId, out var c) ? c : 0;
            var values = new double[count];
            for (var i = 0; i < count; i++)
            {
                var (x, y) = p.LedPosition(i, count, cols);
                values[i] = x * cos + y * sin;
                lo = Math.Min(lo, values[i]); hi = Math.Max(hi, values[i]);
            }
            along[p.DeviceId] = values;
        }
        var result = new Dictionary<string, IReadOnlyList<string?>>();
        foreach (var (id, values) in along)
            result[id] = values.Select(v => (string?)ramp(hi - lo < 1e-9 ? 0 : (v - lo) / (hi - lo)).ToHex()).ToList();
        return result;
    }

    public static Dictionary<string, IReadOnlyList<string?>> Gradient(IReadOnlyList<DevicePlacement> placements, IReadOnlyDictionary<string, int> ledCounts,
        IReadOnlyDictionary<string, int> columns, Color from, Color to, double angleDegrees) =>
        Across(placements, ledCounts, columns, angleDegrees, t => Color.Lerp(from, to, t));

    public static Dictionary<string, IReadOnlyList<string?>> Rainbow(IReadOnlyList<DevicePlacement> placements, IReadOnlyDictionary<string, int> ledCounts,
        IReadOnlyDictionary<string, int> columns, double angleDegrees) =>
        Across(placements, ledCounts, columns, angleDegrees, PaintTools.RainbowAt);
}
