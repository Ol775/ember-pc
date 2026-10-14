using System.Globalization;

namespace Ember.Core;

/// <summary>How big the whole interface is drawn: a little smaller, as designed, or larger.</summary>
public enum UiSize { Small, Normal, Large }

public static class UiSizes
{
    /// <summary>The scale the interface is drawn at (1 is the size it was designed at).</summary>
    public static double Factor(UiSize size) => size switch { UiSize.Small => 0.9, UiSize.Large => 1.15, _ => 1.0 };
}

/// <summary>
/// Where the window was when it was last closed. Stored as text ("left,top,width,height,maximised") in the preferences,
/// and checked on the way back in: a window left on a monitor that is gone is moved onto the screen, one that is too big is shrunk.
/// </summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximised)
{
    const double MaxSize = 20_000, MaxPosition = 100_000;

    public string Format() => string.Join(",", F(Left), F(Top), F(Width), F(Height), Maximised ? "1" : "0");

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out WindowPlacement? placement)
    {
        placement = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 120) return false;
        var parts = text.Split(',');
        if (parts.Length != 5) return false;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || !double.IsFinite(n[i])) return false;
        if (parts[4] is not ("0" or "1")) return false;
        if (n[2] <= 0 || n[3] <= 0 || n[2] > MaxSize || n[3] > MaxSize || Math.Abs(n[0]) > MaxPosition || Math.Abs(n[1]) > MaxPosition) return false;
        placement = new WindowPlacement(n[0], n[1], n[2], n[3], parts[4] == "1");
        return true;
    }

    /// <summary>The placement made to fit the given screen area: at least the minimum size, at most the screen, and fully on it.</summary>
    public WindowPlacement Fit(double screenLeft, double screenTop, double screenWidth, double screenHeight, double minWidth, double minHeight)
    {
        var width = Math.Min(Math.Max(Width, minWidth), screenWidth);
        var height = Math.Min(Math.Max(Height, minHeight), screenHeight);
        var left = Math.Clamp(Left, screenLeft, screenLeft + screenWidth - width);
        var top = Math.Clamp(Top, screenTop, screenTop + screenHeight - height);
        return new WindowPlacement(left, top, width, height, Maximised);
    }
}
