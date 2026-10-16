namespace Ember.Lighting;

/// <summary>Draws a scene: for every enabled device, one colour per LED. Pure, so it is tested without hardware.</summary>
public static class Compositor
{
    public static Color Blend(Color under, Color over, BlendMode mode, double opacity)
    {
        var weight = Math.Clamp(opacity, 0, 1) * Math.Clamp(over.A, 0, 1);
        if (weight <= 0) return under;
        var mixed = mode switch
        {
            BlendMode.Add => new Color(under.R + over.R, under.G + over.G, under.B + over.B),
            BlendMode.Screen => new Color(1 - (1 - under.R) * (1 - over.R), 1 - (1 - under.G) * (1 - over.G), 1 - (1 - under.B) * (1 - over.B)),
            BlendMode.Multiply => new Color(under.R * over.R, under.G * over.G, under.B * over.B),
            _ => over,
        };
        return Color.Lerp(under, (mixed with { A = 1 }).Clamped(), weight);
    }

    public static Color SamplePoint(Scene scene, double x, double y, LightingInputs inputs, LedLocal? local = null, string? deviceId = null)
    {
        if (local is not null) inputs = inputs with { Local = local };
        var c = Color.Black;
        foreach (var layer in scene.Layers)
            if (layer.Enabled && layer.Covers(deviceId)) c = Blend(c, layer.Effect.Sample(x, y, inputs), layer.Blend, layer.Opacity);
        return c.Scaled(Math.Clamp(scene.Brightness, 0, 1)).Clamped();
    }

    /// <param name="ledCounts">LED count per device id. Devices without a placement, or switched off, are left out.</param>
    public static Dictionary<string, Color[]> Render(Scene scene, IReadOnlyDictionary<string, int> ledCounts, LightingInputs inputs,
        IReadOnlyList<Scene>? profiles = null, IReadOnlyDictionary<string, int>? columns = null)
    {
        var frame = new Dictionary<string, Color[]>();
        foreach (var p in scene.Placements)
        {
            if (!p.Enabled || !ledCounts.TryGetValue(p.DeviceId, out var count) || count <= 0) continue;
            var look = p.Profile is { } name && name != scene.Name && profiles?.FirstOrDefault(s => s.Name == name) is { } own ? own : scene;
            var leds = new Color[count];
            for (var i = 0; i < count; i++)
            {
                var cols = columns is not null && columns.TryGetValue(p.DeviceId, out var c) ? c : 0;
                var (x, y) = p.LedPosition(i, count, cols);
                leds[i] = p.Paint is { } paint && i < paint.Count && Color.TryParseHex(paint[i]) is { } fixedColour
                    ? fixedColour.Scaled(Math.Clamp(look.Brightness, 0, 1))
                    : SamplePoint(look, x, y, inputs, p.LocalPosition(i, count, cols), p.DeviceId);
            }
            frame[p.DeviceId] = leds;
        }
        return frame;
    }

    /// <summary>One colour for a whole device (a Govee lamp): the average of its LEDs.</summary>
    public static Color Average(IReadOnlyList<Color> leds)
    {
        if (leds.Count == 0) return Color.Black;
        return new Color(leds.Average(c => c.R), leds.Average(c => c.G), leds.Average(c => c.B));
    }
}
