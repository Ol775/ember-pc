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
