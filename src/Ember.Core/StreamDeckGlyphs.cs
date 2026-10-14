using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// The small pictures on the Stream Deck keys and in its action list. Each is a few stroked paths in a 24 by 24 box, kept in
/// StreamDeckGlyphs.json so the key faces (SVG) and scripts\make-streamdeck-icons.ps1 (PNG) draw exactly the same shapes.
/// </summary>
public static class StreamDeckGlyphs
{
    static readonly Lazy<IReadOnlyDictionary<string, string[]>> All = new(Load);

    static IReadOnlyDictionary<string, string[]> Load()
    {
        using var stream = typeof(StreamDeckGlyphs).Assembly.GetManifestResourceStream("Ember.Core.StreamDeckGlyphs.json")
            ?? throw new InvalidOperationException("StreamDeckGlyphs.json is not embedded.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? [];
    }

    public static IReadOnlyCollection<string> Names => All.Value.Keys.ToList();

    public static IReadOnlyList<string> Paths(string name) => All.Value.TryGetValue(name, out var p) ? p : [];

    /// <summary>The glyph as an SVG group (24 by 24 box scaled by <paramref name="scale"/> and moved to <paramref name="x"/>, <paramref name="y"/>), or "" for an unknown name.</summary>
    public static string Svg(string name, string stroke, double x, double y, double scale)
    {
        var paths = Paths(name);
        if (paths.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append(FormattableString.Invariant($"<g transform=\"translate({x} {y}) scale({scale})\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"));
        foreach (var p in paths) sb.Append("<path d=\"").Append(p).Append("\"/>");
        sb.Append("</g>");
        return sb.ToString();
    }
}
