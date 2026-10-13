using System.Text.Json;

namespace Ember.Lighting;

/// <summary>
/// Ready-made fills for painting single LEDs: one colour, a gradient at any angle, a rainbow. Each returns the paint list
/// for one device ("#RRGGBB" for every LED). Pure, so it is tested without hardware.
/// </summary>
public static class PaintTools
{
    public const int MaxLeds = 1024;

    public static IReadOnlyList<string?> Fill(int count, Color colour) =>
        Enumerable.Repeat<string?>(colour.ToHex(), Math.Clamp(count, 0, MaxLeds)).ToList();

    /// <summary>
    /// A gradient from <paramref name="from"/> to <paramref name="to"/> over the device. The angle is in degrees: 0 runs left to
    /// right, 90 top to bottom. A device that is one row (or one column) of LEDs simply runs from its first LED to its last.
    /// </summary>
    public static IReadOnlyList<string?> Gradient(int count, int columns, Color from, Color to, double angleDegrees) =>
        Along(count, columns, angleDegrees).Select(t => (string?)Color.Lerp(from, to, t).ToHex()).ToList();

    /// <summary>The colours of the rainbow, from red to violet, along the same direction rules as <see cref="Gradient"/>.</summary>
    public static IReadOnlyList<string?> Rainbow(int count, int columns, double angleDegrees) =>
        Along(count, columns, angleDegrees).Select(t => (string?)FromHue(t * 300).ToHex()).ToList();

    /// <summary>For every LED, how far along the chosen direction it is: 0 at the start, 1 at the end.</summary>
    static double[] Along(int count, int columns, double angleDegrees)
    {
        count = Math.Clamp(count, 0, MaxLeds);
        var angle = angleDegrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        var along = new double[count];
        for (var i = 0; i < count; i++)
        {
            var (x, y) = Where(i, count, columns);
            along[i] = x * cos + y * sin;
        }
        if (count == 0) return along;
        var (lo, hi) = (along.Min(), along.Max());
        for (var i = 0; i < count; i++) along[i] = hi - lo < 1e-9 ? 0 : (along[i] - lo) / (hi - lo);
        return along;
    }
    /// <summary>Position of an LED as a point in the unit square, in the same layout the compositor uses.</summary>
    static (double X, double Y) Where(int index, int count, int columns)
    {
        if (count <= 1) return (0.5, 0.5);
        if (columns > 1 && count % columns == 0)
        {
            var rows = count / columns;
            return ((index % columns + 0.5) / columns, (index / columns + 0.5) / rows);
        }
        return ((index + 0.5) / count, 0.5);
    }

    static Color FromHue(double degrees)
    {
        var h = (degrees % 360 + 360) % 360 / 60;
        var x = 1 - Math.Abs(h % 2 - 1);
        return (int)h switch
        {
            0 => new Color(1, x, 0), 1 => new Color(x, 1, 0), 2 => new Color(0, 1, x),
            3 => new Color(0, x, 1), 4 => new Color(x, 0, 1), _ => new Color(1, 0, x),
        };
    }

    /// <summary>The same picture on a device with a different number of LEDs (nearest LED wins).</summary>
    public static IReadOnlyList<string?> Resample(IReadOnlyList<string?> paint, int count)
    {
        count = Math.Clamp(count, 0, MaxLeds);
        if (paint.Count == 0 || count == 0) return new string?[count];
        if (paint.Count == count) return paint.ToList();
        var list = new string?[count];
        for (var i = 0; i < count; i++) list[i] = paint[Math.Clamp((int)((i + 0.5) / count * paint.Count), 0, paint.Count - 1)];
        return list;
    }

    /// <summary>Flips the picture left to right (rows are flipped one by one on a grid).</summary>
    public static IReadOnlyList<string?> Mirror(IReadOnlyList<string?> paint, int columns)
    {
        var cols = columns > 1 && paint.Count % columns == 0 ? columns : Math.Max(1, paint.Count);
        var list = new string?[paint.Count];
        for (var i = 0; i < paint.Count; i++) list[i] = paint[i / cols * cols + (cols - 1 - i % cols)];
        return list;
    }
}

/// <summary>
/// The file that Export paint writes and Import paint reads: JSON with the colours in LED order. Reading is strict about size
/// and content, because the file may come from anywhere.
/// </summary>
public static class PaintFile
{
    public const string Format = "ember-paint";
    public const int MaxBytes = 262_144;

    public static string Export(IReadOnlyList<string?> paint, int columns, string? deviceName = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["format"] = Format, ["version"] = 1, ["device"] = deviceName, ["leds"] = paint.Count, ["columns"] = columns, ["paint"] = paint,
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>
    /// Reads a paint file: the JSON written by <see cref="Export"/>, or a plain list with one colour per line ("#RRGGBB", or a
    /// blank line for an LED left to the effects). Null if it is anything else. Never throws.
    /// </summary>
    public static IReadOnlyList<string?>? TryImport(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxBytes) return null;
        try
        {
            var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
            if (trimmed.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (!root.TryGetProperty("format", out var f) || f.GetString() != Format) return null;
                if (!root.TryGetProperty("paint", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
                return Validate(arr.EnumerateArray().Take(PaintTools.MaxLeds + 1).Select(e => e.ValueKind == JsonValueKind.Null ? null : e.ValueKind == JsonValueKind.String ? e.GetString() : "?"));
            }
            return Validate(trimmed.Split('\n').Take(PaintTools.MaxLeds + 1).Select(l => l.Trim()).Select(l => l.Length == 0 ? null : l));
        }
        catch (JsonException) { return null; }
    }

    static IReadOnlyList<string?>? Validate(IEnumerable<string?> items)
    {
        var list = new List<string?>();
        foreach (var item in items)
        {
            if (item is null) { list.Add(null); continue; }
            if (Color.TryParseHex(item) is not { } c) return null;
            list.Add(c.ToHex());
        }
        return list.Count is > 0 and <= PaintTools.MaxLeds && list.Any(x => x is not null) ? list : null;
    }
}
