namespace Ember.Lighting;

/// <summary>Turns a small picture of the screen into the colours the Screen sync layer paints, in vertical strips from left to right.</summary>
public static class ScreenZones
{
    public const int DefaultZones = 4;

    /// <param name="bgra">Pixels as the bytes blue, green, red, unused, row by row from the top.</param>
    public static Color[] FromBgra(ReadOnlySpan<byte> bgra, int width, int height, int zones = DefaultZones)
    {
        if (width <= 0 || height <= 0 || zones <= 0 || bgra.Length < (long)width * height * 4) return [];
        var result = new Color[zones];
        for (var z = 0; z < zones; z++)
        {
            var x0 = z * width / zones;
            var x1 = Math.Max(x0 + 1, (z + 1) * width / zones);
            double r = 0, g = 0, b = 0;
            var n = 0;
            for (var y = 0; y < height; y++)
                for (var x = x0; x < x1 && x < width; x++)
                {
                    var i = (y * width + x) * 4;
                    b += bgra[i];
                    g += bgra[i + 1];
                    r += bgra[i + 2];
                    n++;
                }
            result[z] = n == 0 ? Color.Black : new Color(r / n / 255, g / n / 255, b / n / 255);
        }
        return result;
    }

    /// <summary>Moves the last picture a step towards the new one, so a flash of white does not make the lights flicker.</summary>
    public static Color[] Smooth(Color[]? previous, Color[] next, double amount = 0.5)
    {
        if (previous is null || previous.Length != next.Length) return next;
        var result = new Color[next.Length];
        for (var i = 0; i < next.Length; i++) result[i] = Color.Lerp(previous[i], next[i], amount);
        return result;
    }
}
