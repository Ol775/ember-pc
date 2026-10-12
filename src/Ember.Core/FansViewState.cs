namespace Ember.Core;

public sealed record ModeItem(FanMode Mode, string Label, bool Enabled, bool Selected)
{
    // WPF uses ToString() as the accessible name of a list item.
    public override string ToString() => Label;
}

public sealed record HeaderRow(string Id, string Name, string StateText, bool CanEnable, bool CanDisable, bool CanTest,
    string? DisabledReason)
{
    public override string ToString() => $"{Name}, {StateText}";
}

public sealed record FansView(
    bool ServiceAvailable, string? Message, IReadOnlyList<ModeItem> Modes, string SourceText,
    string? CompetitorWarning, string? SafetyNote, IReadOnlyList<HeaderRow> Headers, string? TestMessage,
    string? DetectedGame = null, IReadOnlyList<string>? NotGames = null, bool GpuControl = false,
    IReadOnlyList<string>? CompetitorNames = null, FanMode Mode = FanMode.Normal, ManualCurves? ManualCurves = null)
{
    /// <summary>The user's Manual-mode curves (the Normal preset until they change them).</summary>
    public ManualCurves Curves => ManualCurves ?? Ember.Core.ManualCurves.Default;

    /// <summary>Display names of the detected fan-control programs (for the close button).</summary>
    public IReadOnlyList<string> Competitors => CompetitorNames ?? Array.Empty<string>();
}

/// <summary>Turns the service's fan control status into what the Fans page shows. Pure, so it is tested without WPF.</summary>
public static class FansViewState
{
    static readonly FanMode[] Order = [FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme, FanMode.Manual, FanMode.Ai];

    public static FansView Render(FanControlStatus? status, bool connected, BuildFeatures? features = null)
    {
        features ??= BuildFeatures.Current;
        if (!connected || status is null)
        {
            return new FansView(false, "Service not running: fan control is unavailable.",
                Order.Select(m => new ModeItem(m, Label(m), false, false)).ToList(), "", null, null, [], null);
        }

        var modes = Order.Select(m => new ModeItem(m, Label(m), m != FanMode.Ai, m == status.Mode)).ToList();
        var names = Join(status.Competitors);
        var competing = status.Competitors.Count > 0;
        var headers = status.Headers.Select(h => Row(h, competing, names, features.GpuFan)).ToList();

        return new FansView(
            true, null, modes,
            status.Source switch
            {
                ModeSource.Game => $"Game mode: {status.GameExe}",
                ModeSource.GameOverride => "Your pick for this game",
                _ => "Chosen by you",
            },
            competing ? $"{names} is running and may also control your fans. Close it before enabling fan control." : null,
            status.SafetyOverride
                ? "Safety override: all controlled fans are at 100% because a temperature is too high or a sensor is missing."
                : null,
            headers, status.TestMessage, status.GameExe, status.NotGames, status.GpuControl, status.Competitors,
            status.Mode, status.Curves);
    }

    static HeaderRow Row(HeaderStatus h, bool competing, string names, bool gpuInBuild)
    {
        var text = h.State switch
        {
            HeaderState.ReadOnly => "Read-only (cannot be controlled)",
            HeaderState.Controllable => h.Tested ? "Tested, not enabled" : "Not tested",
            HeaderState.Enabled => "Enabled (Ember controls this fan)",
            HeaderState.Failed => "Test failed",
            HeaderState.Unknown when h.Id.StartsWith("/gpu-", StringComparison.Ordinal) && !gpuInBuild => "GPU fan control arrives in the beta build",
            HeaderState.Unknown when h.Id.StartsWith("/gpu-", StringComparison.Ordinal) => "GPU fan control is off (allow it above)",
            HeaderState.Unknown => "Left to the board (Ember does not control this header)",
            _ => "Unknown",
        };
        var canTest = !competing && h.State is HeaderState.Controllable or HeaderState.Failed;
        var canEnable = !competing && h.State == HeaderState.Controllable && h.Tested;
        string? reason = null;
        if (h.State != HeaderState.Enabled)
        {
            reason = competing ? $"Close {names} first"
                : h.State is HeaderState.ReadOnly or HeaderState.Unknown ? "This header cannot be controlled"
                : !canEnable ? "Run the test first"
                : null;
        }
        return new HeaderRow(h.Id, h.Name, text, canEnable, h.State == HeaderState.Enabled, canTest, reason);
    }

    public static string Label(FanMode m) => m switch { FanMode.Ai => "AI (coming later)", FanMode.SuperQuiet => "Super quiet", _ => m.ToString() };

    static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
