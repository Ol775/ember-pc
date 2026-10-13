namespace Ember.Lighting.Hue;

/// <summary>Turns an RGB colour into the CIE xy point and brightness that a Hue light is told to show.</summary>
public static class HueColour
{
    /// <summary>
    /// Philips' published conversion: undo the display gamma, convert with the wide-gamut matrix, and divide by the sum.
    /// The bridge fits the point to what each lamp can show, so no gamut clamping is done here. Brightness is the strongest
    /// channel, as a percentage, so a dim colour dims the lamp instead of changing its hue.
    /// </summary>
    public static (double X, double Y, double Brightness) FromColor(Color c)
    {
        var (r, g, b) = (Math.Clamp(c.R, 0, 1), Math.Clamp(c.G, 0, 1), Math.Clamp(c.B, 0, 1));
        var peak = Math.Max(r, Math.Max(g, b));
        if (peak <= 0) return (0.3127, 0.3290, 0);                 // black: the white point, at no brightness
        // Brightness carries the level; the colour is the same colour at full strength.
        (r, g, b) = (r / peak, g / peak, b / peak);
        r = Gamma(r); g = Gamma(g); b = Gamma(b);
        var x = r * 0.664511 + g * 0.154324 + b * 0.162028;
        var y = r * 0.283881 + g * 0.668433 + b * 0.047685;
        var z = r * 0.000088 + g * 0.072310 + b * 0.986039;
        var sum = x + y + z;
        if (sum <= 0) return (0.3127, 0.3290, peak * 100);
        return (Math.Round(x / sum, 4), Math.Round(y / sum, 4), Math.Round(peak * 100, 1));
    }

    static double Gamma(double v) => v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;
}
