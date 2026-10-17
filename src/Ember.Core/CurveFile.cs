using System.Text.Json;

namespace Ember.Core;

/// <summary>The two manual fan curves as a file, to share or keep. An imported file is checked against the same rules as a curve drawn by hand.</summary>
public static class CurveFile
{
    public const int MaxBytes = 32768;
    const string Kind = "ember-fan-curves";

    public static string Export(ManualCurves curves) => JsonSerializer.Serialize(new
    {
        Kind, Version = 1,
        Cpu = curves.Cpu.Select(p => new[] { p.TempC, p.Percent }),
        Case = curves.Case.Select(p => new[] { p.TempC, p.Percent }),
    }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>The curves in a file, or null with the reason when it is anything else or breaks a rule (a curve that lets the fans idle when hot).</summary>
    public static ManualCurves? TryImport(string text, out string? problem)
    {
        problem = "that is not an Ember fan curve file";
        try
        {
            if (text.Length > MaxBytes) { problem = "that file is too large to be a fan curve file"; return null; }
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Kind", out var kind) || kind.GetString() != Kind) return null;
            var cpu = Points(root, "Cpu");
            var @case = Points(root, "Case");
            if (cpu is null || @case is null) return null;
            if (ManualCurveRules.Check(cpu) is { } why) { problem = "the CPU curve " + why; return null; }
            if (ManualCurveRules.Check(@case) is { } why2) { problem = "the case fan curve " + why2; return null; }
            problem = null;
            return new ManualCurves(cpu, @case);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    static List<CurvePoint>? Points(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is < 2 or > 20) return null;
        var points = new List<CurvePoint>();
        foreach (var p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2) return null;
            var (t, pct) = (p[0].GetDouble(), p[1].GetDouble());
            if (!double.IsFinite(t) || !double.IsFinite(pct)) return null;
            points.Add(new CurvePoint(t, pct));
        }
        return points;
    }
}
