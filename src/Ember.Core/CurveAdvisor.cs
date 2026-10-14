namespace Ember.Core;

/// <summary>A curve suggested for each of the two manual curve groups, with plain-language notes about why.</summary>
public sealed record CurveSuggestion(IReadOnlyList<CurvePoint> Cpu, IReadOnlyList<CurvePoint> Case, IReadOnlyList<string> Notes);

/// <summary>The answer to "suggest a curve": either a suggestion, or why there is not one yet.</summary>
public sealed record AdvisorResult(CurveSuggestion? Suggestion, string Message)
{
    public bool Ready => Suggestion is not null;
}

/// <summary>
/// Suggests manual fan curves from how hot this PC usually runs and how much the owner minds noise. It is a plain, explainable
/// calculation (no network, no model): the fans hold a low floor speed through the temperatures the PC normally sits at, and
/// only start to climb above them, with a quieter or a cooler tilt as chosen. Every suggestion satisfies <see cref="ManualCurveRules"/>
/// by construction (at least 60% at 80 °C, 80% at 85 °C, full speed at 90 °C), and the service checks it again before it applies it.
/// </summary>
public static class CurveAdvisor
{
    /// <summary>Hours of watching needed before a suggestion is made.</summary>
    public const double MinHours = 2;

    public const double DefaultPreference = 0.35;

    /// <param name="preference">0 for as quiet as possible, 1 for as cool as possible.</param>
    public static AdvisorResult Suggest(ThermalProfile profile, double preference)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var p = double.IsFinite(preference) ? Math.Clamp(preference, 0, 1) : DefaultPreference;
        if (profile.Hours < MinHours)
        {
            var left = MinHours - profile.Hours;
            return new AdvisorResult(null, $"Still learning: {profile.Hours:0.#} hours so far. Keep Ember running for about {Math.Ceiling(left * 2) / 2:0.#} more hours, and a few days is better.");
        }

        var cpu = Build(profile.Cpu, p, 0, ReleaseRules.CpuFloor);
        var kase = Build(profile.Case.Total > 0 ? profile.Case : profile.Cpu, p, -2, ReleaseRules.SystemFloor);
        var notes = new List<string>();
        if (profile.Cpu.Percentile(0.1) is { } lo && profile.Cpu.Percentile(0.9) is { } hi && profile.Cpu.Highest is { } top)
            notes.Add($"Your CPU usually runs between {lo:0} and {hi:0} °C, and the highest seen was {top:0} °C.");
        notes.Add($"CPU fan: {Describe(cpu)}.");
        notes.Add($"Case fans: {Describe(kase)}.");
        notes.Add(p < 0.34 ? "Tilted towards quiet: the fans stay low for longer and climb later."
            : p > 0.66 ? "Tilted towards cool: the fans start climbing sooner and run faster."
            : "Balanced between quiet and cool.");
        notes.Add("The safety rules stay in charge: full speed at 90 °C, and the service checks the curve again before it uses it.");
        return new AdvisorResult(new CurveSuggestion(cpu, kase, notes), "Here is a curve based on your readings.");
    }

    /// <param name="minFloor">The slowest the service lets this fan run: a suggestion below it would be shown as slower than it really is.</param>
    static IReadOnlyList<CurvePoint> Build(TemperatureHistogram hist, double p, double shiftC, double minFloor)
    {
        var floor = Math.Max(minFloor, Math.Round(Lerp(20, 35, p)));
        var typical = hist.Percentile(0.75) ?? 45;
        var knee = Math.Clamp(Math.Round(typical + Lerp(6, 1, p) + shiftC), 40, 65);   // the floor holds up to here
        var at80 = Math.Round(Lerp(65, 85, p));
        var at85 = Math.Round(Lerp(85, 100, p));
        var midT = Math.Round(knee + (80 - knee) * 0.5);
        var midP = Math.Round(floor + (at80 - floor) * Lerp(0.25, 0.5, p));
        return
        [
            new CurvePoint(30, floor), new CurvePoint(knee, floor), new CurvePoint(midT, midP),
            new CurvePoint(80, at80), new CurvePoint(85, at85), new CurvePoint(90, 100),
        ];
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>"20% up to 52 °C, 46% at 66 °C, 70% at 80 °C, 100% at 90 °C": the points that matter, in words.</summary>
    public static string Describe(IReadOnlyList<CurvePoint> points)
    {
        var parts = new List<string>();
        for (var i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            if (i > 0 && points[i - 1].Percent == pt.Percent) continue;   // already said by "up to"
            parts.Add(i == 0 && i + 1 < points.Count && points[i + 1].Percent == pt.Percent
                ? $"{pt.Percent:0}% up to {points[i + 1].TempC:0} °C"
                : $"{pt.Percent:0}% at {pt.TempC:0} °C");
        }
        return string.Join(", ", parts);
    }
}
