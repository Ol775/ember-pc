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
    public static event Action? Changed;

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
        var resources = Application.Current.Resources;
        foreach (var (key, hex) in ThemePalette.For(IsDark, prefs.Accent))
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            if (resources[key] is SolidColorBrush { IsFrozen: false } brush) brush.Color = color;
            else resources[key] = new SolidColorBrush(color);
        }
        Changed?.Invoke();
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
            var p = ThemePalette.For(IsDark, AccentChoice.Orange);
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
