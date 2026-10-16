using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Applies a theme by changing the colour of the brushes in the application resources. Every control refers to those
/// brushes, so the whole window follows without being rebuilt.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; } = true;
    static Look _look = new(true);
    public static event Action? Changed;

    public static Brush Warn => Frozen(IsDark ? "#ffb04d" : "#b45f06");
    public static Brush Good => Frozen(IsDark ? "#5fd38d" : "#1c7f45");
    public static Brush GoodSoft => Frozen(IsDark ? "#12301f" : "#d9f2e3");

    /// <summary>True when Windows apps are set to the light theme.</summary>
    public static bool SystemIsLight()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v != 0; }
        catch (Exception) { return false; }
    }

    public static void Apply(AppPreferences prefs)
    {
        IsDark = prefs.Theme switch { ThemeMode.Light => false, ThemeMode.System => !SystemIsLight(), _ => true };
        _look = new Look(IsDark, prefs.Accent, prefs.Theme == ThemeMode.Oled, prefs.Tone, prefs.Highlight, prefs.CustomAccent, prefs.GpuAccent);
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in Redesigned(ThemePalette.For(_look), _look))
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (resources[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = color;
            else resources[key] = new SolidColorBrush(color);
        }
        if (BuildFeatures.NewLook) SoftCardBrushes();
        Changed?.Invoke();
    }

    /// <summary>
    /// The redesign (beta builds): on the default dark warm theme the surfaces, text and buttons take the exact colours of the mockups, and an
    /// orange accent gets the teal graphics-card line. Every other theme keeps its own palette, so a chosen accent or OLED black still works.
    /// </summary>
    static IReadOnlyDictionary<string, string> Redesigned(IReadOnlyDictionary<string, string> palette, Look look)
    {
        if (!BuildFeatures.NewLook || !look.Dark || look.Oled || look.Tone != SurfaceTone.Warm) return palette;
        var p = palette.ToDictionary(kv => kv.Key, kv => kv.Value);
        p["WindowBrush"] = "#0e0c0b"; p["SidebarBrush"] = "#120f0e"; p["PanelBrush"] = "#181412"; p["PanelBorderBrush"] = "#2b2420";
        p["TextBrush"] = "#f4ede8"; p["MutedBrush"] = "#9a8e86"; p["TrackBrush"] = "#241e1a"; p["SegmentHoverBrush"] = "#2e2722"; p["SegmentOnBrush"] = "#3a322d";
        p["ButtonBrush"] = "#241e1a"; p["ButtonHoverBrush"] = "#2e2722"; p["ButtonPressedBrush"] = "#3a322d"; p["ThumbBrush"] = "#3a322d"; p["ThumbHoverBrush"] = "#5a4e47";
        if (look.Accent == AccentChoice.Orange)
        {
            p["AccentTextBrush"] = "#ff9a5c";
            if (look.Gpu is null) p["GpuBrush"] = "#4fd1c5";
        }
        return p;
    }

    /// <summary>The accent-tinted card of the mockups: a gradient from the accent at about 16 percent down into the panel colour, with a 35 percent accent edge.</summary>
    static void SoftCardBrushes()
    {
        var resources = Application.Current.Resources;
        if (resources["AccentBrush"] is not SolidColorBrush accent || resources["PanelBrush"] is not SolidColorBrush panel) return;
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        resources["SoftCardBrush"] = new LinearGradientBrush(Mix(panel.Color, accent.Color, 0.16), panel.Color, new Point(0.25, 0), new Point(0.65, 1));
        resources["SoftCardBorderBrush"] = new SolidColorBrush(Color.FromArgb(0x59, accent.Color.R, accent.Color.G, accent.Color.B));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Gives the title bar and frame the colours of the current theme.</summary>
    public static void ApplyChrome(Window window)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var p = Redesigned(ThemePalette.For(_look), _look);
            int on = IsDark ? 1 : 0, caption = Colorref(p["SidebarBrush"]), border = Colorref(p["PanelBorderBrush"]), text = Colorref(p["TextBrush"]);
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));       // dark title bar (Windows 10 2004 and later)
            DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // frame colour (Windows 11)
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));  // title bar colour (Windows 11)
            DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));     // title text colour (Windows 11)
        }
        catch (Exception) { /* an older Windows keeps its normal frame */ }
    }

    static int Colorref(string hex) => Convert.ToInt32(hex[5..7] + hex[3..5] + hex[1..3], 16); // 0x00BBGGRR

    static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
