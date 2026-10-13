namespace Ember.Lighting;

/// <summary>Small deterministic helpers for effects that look random but must give the same picture for the same time.</summary>
static class Noise
{
    /// <summary>A repeatable number from 0 up to 1 for a pair of whole numbers and a salt.</summary>
    public static double Hash01(long a, long b, int salt)
    {
        unchecked
        {
            var h = (ulong)(a * 73856093L) ^ (ulong)(b * 19349663L) ^ (ulong)(salt * 83492791L);
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
            return (h >> 11) / (double)(1UL << 53);
        }
    }

    /// <summary>Smooth value noise, 0 to 1, that changes gently between whole-number points.</summary>
    public static double Value(double x, double y, int salt)
    {
        var (x0, y0) = ((long)Math.Floor(x), (long)Math.Floor(y));
        var (fx, fy) = (x - x0, y - y0);
        var (sx, sy) = (fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy));
        var top = Hash01(x0, y0, salt) + (Hash01(x0 + 1, y0, salt) - Hash01(x0, y0, salt)) * sx;
        var bottom = Hash01(x0, y0 + 1, salt) + (Hash01(x0 + 1, y0 + 1, salt) - Hash01(x0, y0 + 1, salt)) * sx;
        return top + (bottom - top) * sy;
    }

    /// <summary>Two layers of value noise, finer detail on top of the coarse one, still 0 to 1.</summary>
    public static double Fractal(double x, double y, int salt) => 0.65 * Value(x, y, salt) + 0.35 * Value(x * 2.1, y * 2.1, salt + 17);
}

/// <summary>
/// Flames: hot at the bottom, cooler and flickering towards the top, drifting upwards. Dark to Base to Tip as the heat rises.
/// On a single row of LEDs the flame has no height, so the heat flickers along the strip instead. Covers everything below it.
/// </summary>
public sealed record FireEffect(Color Base, Color Tip, double Speed = 1, double Intensity = 0.9, EffectScope Scope = EffectScope.Device) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var (px, py, rows) = ScopeHelper.Point(Scope, x, y, inputs);
        var speed = Math.Clamp(Speed, 0.1, 5);
        var intensity = Math.Clamp(Intensity, 0.1, 1.5);
        var t = Math.Max(0, inputs.Seconds) * speed;
        px = Math.Clamp(px, 0, 1);
        py = Math.Clamp(py, 0, 1);
        double heat;
        if (rows <= 1)
        {
            var n = Noise.Fractal(px * 5 - t * 0.35, t * 1.1, 3);
            heat = (0.25 + 0.95 * n) * intensity;
        }
        else
        {
            var n = Noise.Fractal(px * 4, py * 3 + t * 1.4, 3);   // flames travel up: the pattern slides down the picture
            var fuel = Math.Pow(py, 1.4);                          // 1 at the bottom, 0 at the top
            heat = fuel * (0.4 + 1.0 * n) * intensity;
        }
        heat = Math.Clamp(heat, 0, 1);
        return heat < 0.5 ? Color.Lerp(Color.Black, Base, heat * 2) : Color.Lerp(Base, Tip, (heat - 0.5) * 2);
    }

    public override string Label => "Fire";
}

/// <summary>
/// Drops that fall down the picture, each with a fading trail. Speed is in heights per second. Only the drops are coloured,
/// so put it above another layer. On a single row of LEDs the drops run along the row instead.
/// </summary>
public sealed record RainEffect(Color Colour, double Density = 0.6, double Speed = 0.5, double Length = 0.3, EffectScope Scope = EffectScope.Device) : Effect
{
    const int Lanes = 14;

    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var (px, py, rows) = ScopeHelper.Point(Scope, x, y, inputs);
        var density = Math.Clamp(Density, 0, 1);
        var speed = Math.Clamp(Speed, 0.05, 3);
        var length = Math.Clamp(Length, 0.05, 1);
        var t = Math.Max(0, inputs.Seconds);
        px = Math.Clamp(px, 0, 1);
        py = Math.Clamp(py, 0, 1);

        var line = rows <= 1;
        var lane = line ? 0 : Math.Min(Lanes - 1, (int)(px * Lanes));
        var along = line ? px : py;
        var across = line ? 0.0 : Math.Abs(px * Lanes - lane - 0.5) * 2;          // 0 in the middle of a lane, 1 at its edge
        var drops = line ? 3 : 2;
        var strength = 0.0;
        for (var k = 0; k < drops; k++)
        {
            if (Noise.Hash01(lane, k, 1) >= density) continue;
            var pace = speed * (0.6 + 0.8 * Noise.Hash01(lane, k, 2));
            var phase = Noise.Hash01(lane, k, 3);
            var travel = (1 + length);                                              // the head runs on until the whole trail has left
            var head = (phase + t * pace) % 1.0 * travel;
            var behind = head - along;                                              // how far the trail reaches back from the head
            if (behind < 0 || behind > length) continue;
            strength += Math.Pow(1 - behind / length, 1.5) * (1 - across * across);
        }
        return Colour with { A = Math.Clamp(strength, 0, 1) };
    }

    public override string Label => "Rain";
}

/// <summary>Parts of a full-size keyboard that an effect can be limited to.</summary>
public enum KeyGroup { FunctionRow, NumberRow, Letters, BottomRow, Navigation, Numpad }

public static class KeyGroups
{
    const double Width = 22.5, Rows = 6, MainEnd = 15, NavEnd = 18.5;

    public static string Label(KeyGroup g) => g switch
    {
        KeyGroup.FunctionRow => "Function row", KeyGroup.NumberRow => "Number row", KeyGroup.Letters => "Letter keys",
        KeyGroup.BottomRow => "Space bar row", KeyGroup.Navigation => "Arrows and navigation", _ => "Number pad",
    };

    /// <summary>The group of the key at this spot of a keyboard (0 to 1 each way), using the layout of <see cref="KeyboardLayout"/>.</summary>
    public static KeyGroup Of(double x, double y)
    {
        var column = Math.Clamp(x, 0, 1) * Width;
        var row = Math.Clamp((int)(Math.Clamp(y, 0, 1) * Rows), 0, (int)Rows - 1);
        if (column >= NavEnd) return KeyGroup.Numpad;
        if (column >= MainEnd) return KeyGroup.Navigation;
        return row switch { 0 => KeyGroup.FunctionRow, 1 => KeyGroup.NumberRow, 5 => KeyGroup.BottomRow, _ => KeyGroup.Letters };
    }
}

/// <summary>Runs another effect on one group of keys only (the function row, the arrows, the number pad). Nothing outside the group, and nothing at all on a device that is not a keyboard.</summary>
public sealed record KeyGroupEffect(KeyGroup Group, Effect Inner) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        if (inputs.Local is not { Rows: > 1 } l || KeyGroups.Of(l.X, l.Y) != Group) return new Color(0, 0, 0, 0);
        return Inner.Sample(x, y, inputs);
    }

    public override string Label => "Key group: " + KeyGroups.Label(Group);
}
