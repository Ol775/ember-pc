using System.Globalization;

namespace Ember.Core;

public enum ThemeMode { Dark, Light, System, Oled }

public enum AccentChoice { Orange, Blue, Green, Purple, Pink, Red, Teal, Gold, Custom }

/// <summary>The tint of the surfaces: the warm brown-black Ember started with, plain grey, or a cool blue-grey.</summary>
public enum SurfaceTone { Warm, Neutral, Cool }

/// <summary>How strongly the accent colour shows in highlights: the selected menu item, banners and soft fills.</summary>
public enum HighlightStrength { Subtle, Normal, Vivid }

/// <summary>Everything that decides the colours of the app. <c>Oled</c> only has an effect on a dark theme.</summary>
public sealed record Look(bool Dark, AccentChoice Accent = AccentChoice.Orange, bool Oled = false, SurfaceTone Tone = SurfaceTone.Warm,
    HighlightStrength Highlight = HighlightStrength.Normal, string? CustomAccent = null);

/// <summary>The colours of one theme, by the resource key the app uses for each (WindowBrush, PanelBrush, ...).</summary>
public static class ThemePalette
{
    public const string DefaultCustomAccent = "#ff7a2f";

    static string Base(AccentChoice a, string? custom) => a switch
    {
        AccentChoice.Blue => "#3b8ef0",
        AccentChoice.Green => "#2fb86a",
        AccentChoice.Purple => "#8b6cf0",
        AccentChoice.Pink => "#ee4f9b",
        AccentChoice.Red => "#f0483e",
        AccentChoice.Teal => "#14b8a6",
        AccentChoice.Gold => "#e8b422",
        AccentChoice.Custom => IsHexColour(custom) ? custom!.ToLowerInvariant() : DefaultCustomAccent,
        _ => "#ff7a2f",
    };

    /// <summary>"#rrggbb" and nothing else.</summary>
    public static bool IsHexColour(string? text) =>
        text is { Length: 7 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The colour an accent choice stands for, for drawing its swatch.</summary>
    public static string AccentColour(AccentChoice accent, string? custom = null) => Base(accent, custom);

    // The surfaces of one tone, for a dark or a light theme.
    sealed record Surfaces(string Window, string Sidebar, string Panel, string Border, string Text, string Muted, string Track,
        string SegOn, string SegHover, string Button, string ButtonHover, string ButtonPressed, string Thumb, string ThumbHover);

    static Surfaces Of(bool dark, SurfaceTone tone) => (dark, tone) switch
    {
        (true, SurfaceTone.Neutral) => new("#121212", "#161616", "#1c1c1c", "#2c2c2c", "#ececec", "#9a9a9a", "#222222", "#464646", "#2d2d2d", "#333333", "#404040", "#4e4e4e", "#464646", "#666666"),
        (true, SurfaceTone.Cool) => new("#0f1218", "#131720", "#181d27", "#262d3a", "#e8edf5", "#8f9aab", "#1b212c", "#3a4455", "#262e3b", "#2b3443", "#394456", "#485469", "#3a4455", "#55627a"),
        (true, _) => new("#141110", "#181412", "#1d1916", "#2e2824", "#f3ece6", "#9c9189", "#241f1c", "#4b413a", "#322b27", "#352e29", "#463c35", "#574a41", "#4b413a", "#6a5b50"),
        (false, SurfaceTone.Neutral) => new("#f3f3f3", "#e9e9e9", "#ffffff", "#d9d9d9", "#1a1a1a", "#595959", "#e5e5e5", "#ffffff", "#d9d9d9", "#e3e3e3", "#d6d6d6", "#c9c9c9", "#c4c4c4", "#a8a8a8"),
        (false, SurfaceTone.Cool) => new("#eff2f7", "#e5e9f0", "#ffffff", "#d3dae6", "#151a24", "#536075", "#e1e6ef", "#ffffff", "#d3dae6", "#dde3ed", "#d0d8e5", "#c2ccdc", "#bfc9d9", "#a0adc2"),
        _ => new("#f4f1ee", "#ebe6e1", "#ffffff", "#ddd5ce", "#1d1916", "#5f554e", "#e9e3dd", "#ffffff", "#ddd5ce", "#e6dfd8", "#dbd2ca", "#cfc4ba", "#cdc3b9", "#b3a79c"),
    };

    /// <summary>Pure black behind everything, for OLED screens: the dark surfaces pulled towards black, the text left alone.</summary>
    static Surfaces Oled(Surfaces s) => s with
    {
        Window = "#000000",
        Sidebar = Mix(s.Sidebar, "#000000", 0.6),
        Panel = Mix(s.Panel, "#000000", 0.5),
        Border = Mix(s.Border, "#000000", 0.2),
        Track = Mix(s.Track, "#000000", 0.5),
        SegHover = Mix(s.SegHover, "#000000", 0.4),
    };

    public static IReadOnlyDictionary<string, string> For(bool dark, AccentChoice accent) => For(new Look(dark, accent));

    public static IReadOnlyDictionary<string, string> For(Look look)
    {
        var (dark, accent) = (look.Dark, look.Accent);
        var s = Of(dark, look.Tone);
        if (dark && look.Oled) s = Oled(s);
        var a = Base(accent, look.CustomAccent);
        var window = s.Window;
        // How much of the accent goes into the soft fills.
        var strength = look.Highlight switch { HighlightStrength.Subtle => 0.6, HighlightStrength.Vivid => 1.5, _ => 1.0 };

        // The accent must stand out on a panel (large text and lines); a custom colour is nudged until it does.
        var accentBrush = dark ? a : Mix(a, "#000000", 0.12);
        accentBrush = Nudge(accentBrush, s.Panel, 3, dark ? "#ffffff" : "#000000");

        var soft = Mix(window, accentBrush, (dark ? 0.16 : 0.14) * strength);
        var accentText = dark ? Mix(a, "#ffffff", 0.35) : Mix(a, "#000000", 0.5);
        accentText = Nudge(accentText, soft, 4.5, dark ? "#ffffff" : "#000000");
        var banner = Mix(window, accentBrush, (dark ? 0.14 : 0.12) * strength);

        var gpu = Partner(accentBrush, dark, s.Panel);

        return new Dictionary<string, string>
        {
            ["WindowBrush"] = window,
            ["SidebarBrush"] = s.Sidebar,
            ["PanelBrush"] = s.Panel,
            ["PanelBorderBrush"] = s.Border,
            ["TextBrush"] = s.Text,
            ["MutedBrush"] = s.Muted,
            ["AccentBrush"] = accentBrush,
            ["AccentSoftBrush"] = soft,
            ["AccentTextBrush"] = accentText,
            ["GpuBrush"] = gpu,
            ["BannerBrush"] = banner,
            ["TrackBrush"] = s.Track,
            ["SegmentOnBrush"] = s.SegOn,
            ["SegmentHoverBrush"] = s.SegHover,
            ["ButtonBrush"] = s.Button,
            ["ButtonHoverBrush"] = s.ButtonHover,
            ["ButtonPressedBrush"] = s.ButtonPressed,
            ["ThumbBrush"] = s.Thumb,
            ["ThumbHoverBrush"] = s.ThumbHover,
        };
    }

    /// <summary>
    /// The colour of the GPU line: a partner to the accent, taken from the opposite side of the colour wheel (so an orange accent
    /// keeps its blue GPU line) with a saturation and lightness that suit the theme. When that lands on orange (a blue accent would
    /// give Ember's own orange) the next hue around the wheel is used instead, and the result is moved until it reads on a panel.
    /// </summary>
    static string Partner(string accent, bool dark, string panel)
    {
        var (h, _, _) = ToHsl(accent);
        var (saturation, lightness) = dark ? (0.70, 0.60) : (0.62, 0.42);
        string best = FromHsl(h + 180, saturation, lightness);
        foreach (var turn in new[] { 180.0, 120, 240, 150, 210 })
        {
            var hue = (h + turn) % 360;
            if (hue is >= 10 and <= 50) continue; // that would read as orange, the brand colour
            var candidate = FromHsl(hue, saturation, lightness);
            if (Distance(candidate, accent) < 90) continue;
            best = candidate;
            break;
        }
        return Nudge(best, panel, 3, dark ? "#ffffff" : "#000000");
    }

    static (double H, double S, double L) ToHsl(string hex)
    {
        var (r, g, b) = Parse(hex);
        double R = r / 255.0, G = g / 255.0, B = b / 255.0, max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B)), d = max - min, l = (max + min) / 2;
        if (d == 0) return (0, 0, l);
        var s = d / (1 - Math.Abs(2 * l - 1));
        var h = max == R ? ((G - B) / d) % 6 : max == G ? (B - R) / d + 2 : (R - G) / d + 4;
        return ((h * 60 + 360) % 360, s, l);
    }

    static string FromHsl(double h, double s, double l)
    {
        h = (h % 360 + 360) % 360;
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        int V(double v) => (int)Math.Round((v + m) * 255);
        return $"#{V(r):x2}{V(g):x2}{V(b):x2}";
    }

    /// <summary>Moves a colour towards another in small steps until it has the contrast against the background.</summary>
    static string Nudge(string colour, string background, double contrast, string towards)
    {
        for (var t = 0.0; t <= 1.0; t += 0.05)
        {
            var c = Mix(colour, towards, t);
            if (Contrast(c, background) >= contrast) return c;
        }
        return towards;
    }

    static double Distance(string x, string y)
    {
        var (a, b) = (Parse(x), Parse(y));
        return Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
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

    /// <summary>The colour t of the way from the first colour to the second.</summary>
    static string Mix(string from, string to, double t)
    {
        var (f, o) = (Parse(from), Parse(to));
        int C(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return $"#{C(f.R, o.R):x2}{C(f.G, o.G):x2}{C(f.B, o.B):x2}";
    }
}
