using System.Globalization;

namespace Ember.Core;

/// <summary>The four live values on the Stream Deck readout keys. A null is "no reading" and shows as a dash.</summary>
public sealed record Readouts(double? CpuTemp, double? CpuFanRpm, double? GpuTemp, double? GpuFanRpm, double? GpuFanPercent = null)
{
    public static readonly Readouts None = new(null, null, null, null);

    /// <summary>
    /// Temperatures are the same ones the dashboard shows. The CPU fan is the motherboard fan reading named "CPU Fan"
    /// (the CPU header), the GPU fan is the graphics card's own fan. A stale snapshot gives no readings at all.
    /// A fan that has stopped reads 0, which is a real value and not the same as no reading.
    /// </summary>
    /// <summary>Like <see cref="From(Snapshot)"/>, but a snapshot older than <see cref="DashboardState.StaleAfter"/> (the service stopped sending) gives no readings.</summary>
    public static Readouts From(Snapshot s, DateTimeOffset now) => StreamDeckLogic.IsFresh(s, now) ? From(s) : None;

    public static Readouts From(Snapshot s)
    {
        if (s.Stale) return None;
        var sum = DashboardSummary.From(s);
        static double? Highest(IEnumerable<SensorReading> r) => r.Select(x => (double?)x.Value).Max();
        var rpm = s.Readings.Where(r => r.Kind == SensorKind.FanRpm).ToList();
        return new Readouts(
            sum.CpuTemp,
            Highest(rpm.Where(r => r.Hardware != HardwareKind.Gpu && r.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))),
            sum.GpuTemp,
            Highest(rpm.Where(r => r.Hardware == HardwareKind.Gpu)),
            Highest(s.Readings.Where(r => r.Kind == SensorKind.FanPercent && r.Hardware == HardwareKind.Gpu)));
    }
}

public static class StreamDeckLogic
{
    /// <summary>The modes the tray menu offers, in its order. The cycle key steps through exactly these.</summary>
    public static readonly FanMode[] CycleOrder = [FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme];

    /// <summary>The mode after <paramref name="current"/>, wrapping round. From a mode that is not on the list (Manual, AI, unknown) it goes to Normal.</summary>
    public static FanMode Next(FanMode? current)
    {
        var i = current is { } m ? Array.IndexOf(CycleOrder, m) : -1;
        return i < 0 ? FanMode.Normal : CycleOrder[(i + 1) % CycleOrder.Length];
    }

    /// <summary>The mode before <paramref name="current"/>, wrapping round. From a mode that is not on the list it goes to Normal, like <see cref="Next"/>.</summary>
    public static FanMode Previous(FanMode? current)
    {
        var i = current is { } m ? Array.IndexOf(CycleOrder, m) : -1;
        return i < 0 ? FanMode.Normal : CycleOrder[(i + CycleOrder.Length - 1) % CycleOrder.Length];
    }

    /// <summary>The scene after <paramref name="current"/>, wrapping round; the first scene when the current one is not in the list; null when there are none.</summary>
    public static string? NextScene(string current, IReadOnlyList<string> scenes)
    {
        if (scenes.Count == 0) return null;
        var i = scenes.ToList().IndexOf(current);
        return i < 0 ? scenes[0] : scenes[(i + 1) % scenes.Count];
    }

    /// <summary>How long a key must be held before it counts as a long press (and before Brightness starts repeating).</summary>
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>How often a held Brightness key repeats once the hold delay has passed.</summary>
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(200);

    public static bool IsLongPress(TimeSpan held) => held >= HoldDelay;

    /// <summary>False for no snapshot, or one older than <see cref="DashboardState.StaleAfter"/> (a hung service keeps the pipe open but stops sending).</summary>
    public static bool IsFresh(Snapshot? s, DateTimeOffset now) => s is not null && now - s.Timestamp <= DashboardState.StaleAfter;

    public static string ModeLabel(FanMode m) => FansViewState.Label(m);

    public static string FormatTemp(double? celsius) => celsius is { } c ? c.ToString("F0", CultureInfo.InvariantCulture) + "°" : "-";

    public static string FormatRpm(double? rpm) => rpm is { } r ? r.ToString("N0", CultureInfo.InvariantCulture) : "-";
}
