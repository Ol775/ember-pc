using System.Globalization;

namespace Ember.Lighting;

/// <summary>A colour with channels from 0 to 1. <see cref="A"/> is how much of the layer below it covers (0 = nothing).</summary>
public readonly record struct Color(double R, double G, double B, double A = 1)
{
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(1, 1, 1);

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }

    public Color Scaled(double factor) => new(R * factor, G * factor, B * factor, A);

    public Color Clamped() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1), Math.Clamp(A, 0, 1));

    public (byte R, byte G, byte B) ToBytes()
    {
        var c = Clamped();
        return ((byte)Math.Round(c.R * 255), (byte)Math.Round(c.G * 255), (byte)Math.Round(c.B * 255));
    }

    public string ToHex()
    {
        var (r, g, b) = ToBytes();
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static Color FromBytes(byte r, byte g, byte b) => new(r / 255.0, g / 255.0, b / 255.0);

    /// <summary>"#RRGGBB" or "RRGGBB"; null when it is anything else.</summary>
    public static Color? TryParseHex(string? text)
    {
        if (text is null) return null;
        var s = text.StartsWith('#') ? text[1..] : text;
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v)) return null;
        return FromBytes((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
