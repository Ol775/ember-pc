using System.Globalization;
using System.Text;

namespace Ember.Core;

/// <summary>One line of the specs; Alert marks a value the person should look at (a drive that is failing).</summary>
public sealed record SpecItem(string Label, string Value, bool Alert = false);

public sealed record SpecGroup(string Title, IReadOnlyList<SpecItem> Items);

/// <summary>The PC's specs as shown on the Specs page and copied as plain text (no PC name or user name is ever included).</summary>
public static class SpecSheet
{
    /// <summary>Binary sizes as people read them on a box: 16 GB of memory, 1.86 TB of disk.</summary>
    public static string Bytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var v = (double)bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        var text = i < 3 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return text + " " + units[i];
    }

    public static string ToText(IEnumerable<SpecGroup> groups)
    {
        var sb = new StringBuilder();
        foreach (var g in groups.Where(g => g.Items.Count > 0))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine(g.Title);
            var width = g.Items.Max(i => i.Label.Length);
            foreach (var i in g.Items) sb.AppendLine($"  {i.Label.PadRight(width)}  {i.Value}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>One physical drive as the Specs page and the dashboard show it. Values a drive does not report stay null.</summary>
public sealed record DriveSpec(string Name, string Kind, bool External, string Volumes, ulong TotalBytes, ulong FreeBytes,
    string Health, bool HealthOk, int? TempC, long? PowerOnHours, int? WearPercent)
{
    public double UsedFraction => TotalBytes == 0 ? 0 : Math.Clamp(1 - (double)FreeBytes / TotalBytes, 0, 1);

    /// <summary>Under 5 percent free.</summary>
    public bool AlmostFull => TotalBytes > 0 && FreeBytes < TotalBytes / 20;

    /// <summary>Years of running time, such as "5.3 y", or "" when the drive does not say.</summary>
    public string Age => PowerOnHours is { } h and > 0 ? (h / 8766.0).ToString("0.0", CultureInfo.InvariantCulture) + " y" : "";

    /// <summary>The plain-text line used when the specs are copied.</summary>
    public string ToLine()
    {
        var parts = new List<string> { Health, Kind, SpecSheet.Bytes(TotalBytes) };
        if (TempC is { } t) parts.Add($"{t} °C");
        if (WearPercent is { } w) parts.Add($"{w}% worn");
        if (PowerOnHours is { } h and > 0) parts.Add($"{h:N0} h on");
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }
}

/// <summary>Everything the Specs page shows: the text groups (also what is copied) and the drives in full.</summary>
public sealed record SpecReport(IReadOnlyList<SpecGroup> Groups, IReadOnlyList<DriveSpec> Drives)
{
    public static readonly SpecReport Empty = new([], []);

    public SpecGroup? Group(string title) => Groups.FirstOrDefault(g => g.Title == title);

    public string? Value(string group, string label) => Group(group)?.Items.FirstOrDefault(i => i.Label == label)?.Value;

    public int HealthyDrives => Drives.Count(d => d.HealthOk);
    public int NearlyFullDrives => Drives.Count(d => d.AlmostFull);
}
