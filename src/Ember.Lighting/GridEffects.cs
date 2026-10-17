namespace Ember.Lighting;

/// <summary>Whether an effect is drawn across the whole canvas or separately on each device (so a keyboard always shows the whole picture).</summary>
public enum EffectScope { Device, Canvas }

/// <summary>
/// Where one LED sits inside its own device, from 0 to 1 each way. <paramref name="Rows"/> is how many rows the device has:
/// a keyboard has several, a horizontal strip has one, a vertical strip has as many as LEDs.
/// </summary>
public readonly record struct LedLocal(double X, double Y, int Rows);

/// <summary>A key that was just pressed: where it sits on a keyboard (0 to 1 each way) and when, in lighting seconds.</summary>
public readonly record struct KeyPulse(double X, double Y, double Seconds);

static class ScopeHelper
{
    /// <summary>The point an effect should use: the device-local one when asked for and known, else the canvas point.</summary>
    public static (double X, double Y, int Rows) Point(EffectScope scope, double x, double y, LightingInputs inputs) =>
        scope == EffectScope.Device && inputs.Local is { } l ? (l.X, l.Y, l.Rows) : (x, y, 8);
}

/// <summary>
/// Rings that spread out from a point and fade as they travel, one new ring every <paramref name="IntervalSeconds"/>.
/// Speed is in widths per second. Only the rings are coloured; everything else is left to the layers below.
/// </summary>
public sealed record RippleEffect(Color Colour, double IntervalSeconds = 1.6, double Speed = 0.35, double Width = 0.14,
    double CenterX = 0.5, double CenterY = 0.5, EffectScope Scope = EffectScope.Canvas) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var (px, py, _) = ScopeHelper.Point(Scope, x, y, inputs);
        var interval = Math.Clamp(IntervalSeconds, 0.2, 30);
        var speed = Math.Clamp(Speed, 0.02, 3);
        var half = Math.Clamp(Width, 0.02, 1) / 2;
        var maxAge = 1.4 / speed;
        var d = Math.Sqrt((px - CenterX) * (px - CenterX) + (py - CenterY) * (py - CenterY));
        var t = Math.Max(0, inputs.Seconds);
        var strength = 0.0;
        for (var n = (long)Math.Floor(t / interval); n >= 0; n--)
        {
            var age = t - n * interval;
            if (age > maxAge) break;
            var offset = (d - speed * age) / half;
            strength += Math.Exp(-offset * offset) * (1 - age / maxAge);
        }
        return Colour with { A = Math.Clamp(strength, 0, 1) };
    }

    public override string Label => "Ripples";
}

/// <summary>
/// Bars that rise with the sound, one per pitch from left to right, coloured from Low at the bottom to High at the top.
/// On a device with a single row of LEDs the bar becomes brightness instead, so it still follows the music.
/// </summary>
public sealed record AudioBarsEffect(Color Low, Color High, double Sensitivity = 1, EffectScope Scope = EffectScope.Device) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        var a = inputs.Audio ?? AudioLevels.Silent;
        if (a.Bands.Count == 0) return new Color(0, 0, 0, 0);
        var (px, py, rows) = ScopeHelper.Point(Scope, x, y, inputs);
        var band = Math.Clamp((int)Math.Floor(Math.Clamp(px, 0, 1) * a.Bands.Count), 0, a.Bands.Count - 1);
        var level = Math.Clamp(a.Bands[band] * Math.Clamp(Sensitivity, 0.1, 4), 0, 1);
        if (rows <= 1) return Color.Lerp(Low, High, level) with { A = level };   // brightness as see-through: silence leaves the layers below alone
        var height = 1 - Math.Clamp(py, 0, 1);                  // 0 at the bottom, 1 at the top
        var soft = (level - height) * rows + 0.5;               // a one-row-wide soft edge at the top of the bar
        return Color.Lerp(Low, High, height) with { A = Math.Clamp(soft, 0, 1) };
    }

    public override string Label => "Spectrum bars";
}

/// <summary>A glow that flashes where a key was pressed and fades out. Needs the keyboard's key positions, so it is drawn per device.</summary>
public sealed record ReactiveKeysEffect(Color Colour, Color Tail, double FadeSeconds = 0.9, double Radius = 0.12) : Effect
{
    public override Color Sample(double x, double y, LightingInputs inputs)
    {
        if (inputs.Pulses is not { Count: > 0 } pulses || inputs.Local is not { } l) return new Color(0, 0, 0, 0);
        var fade = Math.Clamp(FadeSeconds, 0.1, 5);
        var radius = Math.Clamp(Radius, 0.02, 1);
        var strength = 0.0;
        var mix = 0.0;
        foreach (var p in pulses)
        {
            var age = inputs.Seconds - p.Seconds;
            if (age < 0 || age > fade * 3) continue;
            var life = Math.Exp(-age / fade);
            var reach = radius * (1 + age * 2.5);                // the glow widens as it fades
            // Keys are about twice as wide as tall on screen, so distance on the x axis counts for less.
            var dx = (l.X - p.X) * 2.2;
            var dy = l.Y - p.Y;
            var d = Math.Sqrt(dx * dx + dy * dy) / reach;
            if (d >= 1) continue;
            var s = life * (1 - d * d);
            strength += s;
            mix = Math.Max(mix, Math.Clamp(age / (fade * 1.5), 0, 1));
        }
        return Color.Lerp(Colour, Tail, mix) with { A = Math.Clamp(strength, 0, 1) };
    }

    public override string Label => "Reactive keys";
}

/// <summary>
/// Where each Windows virtual key sits on a standard full-size keyboard, as a fraction of its width and height. It is only a
/// map of a typical layout, so it works for any keyboard whose LEDs are drawn as a grid over its whole area.
/// </summary>
public static class KeyboardLayout
{
    const double Width = 22.5, Rows = 6;

    static readonly Dictionary<int, (double X, int Row)> Keys = Build();

    /// <summary>The key's place, or null for a key that is not on the map (media keys and the like).</summary>
    public static (double X, double Y)? Position(int virtualKey) =>
        Keys.TryGetValue(virtualKey, out var k) ? (k.X / Width, (k.Row + 0.5) / Rows) : null;

    static Dictionary<int, (double, int)> Build()
    {
        var m = new Dictionary<int, (double, int)>();
        void Put(int vk, double x, int row) => m[vk] = (x, row);

        // Function row.
        Put(0x1B, 0.5, 0);
        for (var i = 0; i < 4; i++) { Put(0x70 + i, 2.5 + i, 0); Put(0x74 + i, 7.0 + i, 0); Put(0x78 + i, 11.5 + i, 0); }
        Put(0x2C, 15.75, 0); Put(0x91, 16.75, 0); Put(0x13, 17.75, 0);   // Print Screen, Scroll Lock, Pause

        // Number row: ` 1 2 3 4 5 6 7 8 9 0 - =
        int[] number = [0xC0, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xBB];
        for (var i = 0; i < number.Length; i++) Put(number[i], 0.5 + i, 1);
        Put(0x08, 14.0, 1);
        Put(0x2D, 15.75, 1); Put(0x24, 16.75, 1); Put(0x21, 17.75, 1);   // Insert, Home, Page Up

        // Top letter row.
        Put(0x09, 0.75, 2);
        int[] top = [0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xDB, 0xDD];
        for (var i = 0; i < top.Length; i++) Put(top[i], 2.0 + i, 2);
        Put(0xDC, 14.25, 2);
        Put(0x2E, 15.75, 2); Put(0x23, 16.75, 2); Put(0x22, 17.75, 2);   // Delete, End, Page Down

        // Home row.
        Put(0x14, 0.875, 3);
        int[] home = [0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBA, 0xDE];
        for (var i = 0; i < home.Length; i++) Put(home[i], 2.25 + i, 3);
        Put(0x0D, 14.125, 3);

        // Bottom letter row.
        Put(0xA0, 1.125, 4); Put(0x10, 1.125, 4);
        int[] bottom = [0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF];
        for (var i = 0; i < bottom.Length; i++) Put(bottom[i], 2.75 + i, 4);
        Put(0xA1, 13.875, 4);
        Put(0x26, 16.75, 4);                                               // Up

        // Space row.
        Put(0xA2, 0.625, 5); Put(0x11, 0.625, 5); Put(0x5B, 1.875, 5); Put(0xA4, 3.125, 5); Put(0x12, 3.125, 5);
        Put(0x20, 6.875, 5);
        Put(0xA5, 10.625, 5); Put(0x5C, 11.875, 5); Put(0x5D, 13.125, 5); Put(0xA3, 14.375, 5);
        Put(0x25, 15.75, 5); Put(0x28, 16.75, 5); Put(0x27, 17.75, 5);    // Left, Down, Right

        // Number pad.
        Put(0x90, 19, 1); Put(0x6F, 20, 1); Put(0x6A, 21, 1); Put(0x6D, 22, 1);
        Put(0x67, 19, 2); Put(0x68, 20, 2); Put(0x69, 21, 2); Put(0x6B, 22, 2);
        Put(0x64, 19, 3); Put(0x65, 20, 3); Put(0x66, 21, 3);
        Put(0x61, 19, 4); Put(0x62, 20, 4); Put(0x63, 21, 4);
        Put(0x60, 19.5, 5); Put(0x6E, 21, 5);
        return m;
    }
}
