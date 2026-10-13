using System.Globalization;

namespace Ember.Core;

public enum ThemeMode { Dark, Light, System }

public enum AccentChoice { Orange, Blue, Green, Purple, Pink }

/// <summary>The colours of one theme, by the resource key the app uses for each (WindowBrush, PanelBrush, ...).</summary>
public static class ThemePalette
{
    static string Base(AccentChoice a) => a switch
    {
        AccentChoice.Blue => "#3b8ef0",
        AccentChoice.Green => "#2fb86a",
        AccentChoice.Purple => "#8b6cf0",
        AccentChoice.Pink => "#ee4f9b",
        _ => "#ff7a2f",
    };

    public static IReadOnlyDictionary<string, string> For(bool dark, AccentChoice accent)
    {
        var a = Base(accent);
        var window = dark ? "#141110" : "#f4f1ee";
        var map = new Dictionary<string, string>
        {
            ["WindowBrush"] = window,
            ["SidebarBrush"] = dark ? "#181412" : "#ebe6e1",
            ["PanelBrush"] = dark ? "#1d1916" : "#ffffff",
            ["PanelBorderBrush"] = dark ? "#2e2824" : "#ddd5ce",
            ["TextBrush"] = dark ? "#f3ece6" : "#1d1916",
            ["MutedBrush"] = dark ? "#9c9189" : "#5f554e",
            ["AccentBrush"] = dark ? a : Mix(a, "#000000", 0.12),
            ["AccentSoftBrush"] = Mix(window, a, dark ? 0.16 : 0.14),
            ["AccentTextBrush"] = dark ? Mix(a, "#ffffff", 0.35) : Mix(a, "#000000", 0.5),
            ["GpuBrush"] = accent == AccentChoice.Blue ? "#ff7a2f" : dark ? "#4ea8de" : "#2b7fb8",
            ["BannerBrush"] = Mix(window, a, dark ? 0.14 : 0.12),
            ["TrackBrush"] = dark ? "#241f1c" : "#e9e3dd",
            ["SegmentOnBrush"] = dark ? "#4b413a" : "#ffffff",
            ["SegmentHoverBrush"] = dark ? "#322b27" : "#ddd5ce",
            ["ButtonBrush"] = dark ? "#352e29" : "#e6dfd8",
            ["ButtonHoverBrush"] = dark ? "#463c35" : "#dbd2ca",
            ["ButtonPressedBrush"] = dark ? "#574a41" : "#cfc4ba",
            ["ThumbBrush"] = dark ? "#4b413a" : "#cdc3b9",
            ["ThumbHoverBrush"] = dark ? "#6a5b50" : "#b3a79c",
        };
        return map;
    }

    /// <summary>WCAG contrast ratio of two #rrggbb colours (1 to 21).</summary>
    public static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        static double Lin(double c) { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
    }

    static (int R, int G, int B) Parse(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    /// <summary>The colour t of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    static string Mix(string from, string to, double t)
    {
        var (f, o) = (Parse(from), Parse(to));
        int C(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return $"#{C(f.R, o.R):x2}{C(f.G, o.G):x2}{C(f.B, o.B):x2}";
    }
}
