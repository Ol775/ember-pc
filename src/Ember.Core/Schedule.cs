using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// A time window with something to apply while it lasts: a fan mode, a lighting scene, or lights off. It may run past midnight
/// (22:00 to 07:00): the part after midnight belongs to the day the window started.
/// </summary>
public sealed record ScheduleRule(string Name, TimeOnly Start, TimeOnly End, int Days = ScheduleRule.EveryDay, FanMode? Mode = null,
    string? Scene = null, bool LightsOff = false, bool Enabled = true)
{
    public const int EveryDay = 0b1111111; // bit 0 is Monday, bit 6 is Sunday
    public const int MaxRules = 12;

    public static int Bit(DayOfWeek d) => 1 << (((int)d + 6) % 7);

    /// <summary>True when this moment is inside the window. A window whose start equals its end never applies.</summary>
    public bool Covers(DateTime now)
    {
        if (!Enabled || Start == End) return false;
        var time = TimeOnly.FromDateTime(now);
        if (Start < End) return (Days & Bit(now.DayOfWeek)) != 0 && time >= Start && time < End;
        if (time >= Start) return (Days & Bit(now.DayOfWeek)) != 0;       // the evening part
        return time < End && (Days & Bit(now.AddDays(-1).DayOfWeek)) != 0; // the small hours belong to yesterday's window
    }

    public bool ChangesAnything => Mode is not null || Scene is not null || LightsOff;
}

public sealed record ScheduleSet(bool Enabled, IReadOnlyList<ScheduleRule> Rules)
{
    public static ScheduleSet Default { get; } = new(false, []);

    /// <summary>The first enabled rule that covers this moment, so a rule higher in the list wins an overlap.</summary>
    public ScheduleRule? ActiveAt(DateTime now) => Enabled ? Rules.FirstOrDefault(r => r.ChangesAnything && r.Covers(now)) : null;
}

/// <summary>What the schedule needs to know about the PC right now.</summary>
public readonly record struct ScheduleView(FanMode Mode, string Scene, bool LightsOn);

/// <summary>What the schedule asks for. Null means leave it alone.</summary>
public readonly record struct ScheduleActions(FanMode? Mode, string? Scene, bool? LightsOn)
{
    public bool Any => Mode is not null || Scene is not null || LightsOn is not null;
}

/// <summary>
/// Applies a schedule only at the moment a window starts or ends, so the person can still change the mode or scene by hand in between.
/// When a window ends, each thing it changed goes back to what it was before, but only if nobody has changed it since.
/// </summary>
public sealed class ScheduleRunner
{
    ScheduleRule? _active;
    (FanMode Mode, string Scene, bool LightsOn)? _before; // what the PC had when the window started
    FanMode? _setMode;
    string? _setScene;
    bool _setLightsOff;

    public string? ActiveName => _active?.Name;

    public ScheduleActions Step(ScheduleSet set, DateTime now, ScheduleView view)
    {
        var rule = set.ActiveAt(now);
        if (Equals(rule, _active)) return default;

        FanMode? mode = null; string? scene = null; bool? lights = null;
        var after = view; // what the PC will look like once the restore below has happened

        if (_before is { } before)
        {
            if (_setMode is { } m && view.Mode == m) { mode = before.Mode; after = after with { Mode = before.Mode }; }
            if (_setScene is { } s && view.Scene == s) { scene = before.Scene; after = after with { Scene = before.Scene }; }
            if (_setLightsOff && !view.LightsOn) { lights = before.LightsOn; after = after with { LightsOn = before.LightsOn }; }
        }
        (_active, _before, _setMode, _setScene, _setLightsOff) = (null, null, null, null, false);

        if (rule is not null)
        {
            _active = rule;
            _before = (after.Mode, after.Scene, after.LightsOn);
            if (rule.Mode is { } rm && rm != after.Mode) { mode = rm; _setMode = rm; }
            if (rule.Scene is { } rs && rs != after.Scene) { scene = rs; _setScene = rs; }
            if (rule.LightsOff && after.LightsOn) { lights = false; _setLightsOff = true; }
        }
        return new ScheduleActions(mode, scene, lights);
    }
}

public sealed class ScheduleStore(string path)
{
    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "schedule.json");

    public ScheduleSet Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 200_000) return ScheduleSet.Default;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var enabled = root.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;
            var rules = new List<ScheduleRule>();
            if (root.TryGetProperty("Rules", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var r in list.EnumerateArray().Take(ScheduleRule.MaxRules))
                    if (Rule(r) is { } rule) rules.Add(rule);
            return new ScheduleSet(enabled, rules);
        }
        catch (Exception) { return ScheduleSet.Default; }
    }

    static ScheduleRule? Rule(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        string? Text(string key) => r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (!TimeOnly.TryParseExact(Text("Start"), "HH:mm", out var start) || !TimeOnly.TryParseExact(Text("End"), "HH:mm", out var end)) return null;
        FanMode? mode = Enum.TryParse<FanMode>(Text("Mode"), out var m) && Enum.IsDefined(m) && m is not (FanMode.Ai or FanMode.Manual) ? m : null;
        var days = r.TryGetProperty("Days", out var d) && d.TryGetInt32(out var bits) ? bits & ScheduleRule.EveryDay : ScheduleRule.EveryDay;
        var scene = Text("Scene");
        return new ScheduleRule((Text("Name") ?? "Schedule").Trim() is { Length: > 0 and <= 60 } name ? name : "Schedule", start, end, days, mode,
            scene is { Length: > 0 and <= 60 } ? scene : null,
            r.TryGetProperty("LightsOff", out var l) && l.ValueKind == JsonValueKind.True,
            !r.TryGetProperty("Enabled", out var en) || en.ValueKind != JsonValueKind.False);
    }

    public bool TrySave(ScheduleSet set)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            var rules = set.Rules.Take(ScheduleRule.MaxRules).Select(r => new
            {
                r.Name, Start = r.Start.ToString("HH:mm"), End = r.End.ToString("HH:mm"), r.Days,
                Mode = r.Mode?.ToString(), r.Scene, r.LightsOff, r.Enabled,
            });
            File.WriteAllText(temp, JsonSerializer.Serialize(new { set.Enabled, Rules = rules }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}
