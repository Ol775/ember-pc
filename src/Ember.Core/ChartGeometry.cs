namespace Ember.Core;

public static class ChartGeometry
{
    public static IReadOnlyList<(double X, double Y)> Scale(
        IReadOnlyList<(DateTimeOffset Time, double Value)> series, DateTimeOffset windowEnd, TimeSpan window,
        double width, double height, double minValue, double maxValue)
    {
        var start = windowEnd - window;
        var range = maxValue - minValue;
        var result = new List<(double, double)>();
        foreach (var (time, value) in series)
        {
            if (time < start || time > windowEnd) continue;
            var x = (time - start) / window * width;
            var y = range == 0 ? height : (maxValue - value) / range * height;
            result.Add((x, Math.Clamp(y, 0, height)));
        }
        return result;
    }
}
