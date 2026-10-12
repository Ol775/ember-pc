namespace Ember.Lighting;

public enum BlendMode { Normal, Add, Screen, Multiply }

public sealed record Layer(string Id, string Name, Effect Effect, double Opacity = 1, BlendMode Blend = BlendMode.Normal, bool Enabled = true);

/// <summary>Where a device sits on the canvas (the unit square). Its LEDs run along the longer side.</summary>
public sealed record DevicePlacement(string DeviceId, double X, double Y, double Width, double Height, bool Enabled = true)
{
    /// <summary>The canvas position of LED <paramref name="index"/> of <paramref name="count"/>.</summary>
    public (double X, double Y) LedPosition(int index, int count)
    {
        var cx = X + Width / 2;
        var cy = Y + Height / 2;
        if (count <= 1) return (cx, cy);
        var f = (index + 0.5) / count;
        return Width >= Height ? (X + f * Width, cy) : (cx, Y + f * Height);
    }
}

/// <summary>A saved arrangement: the layers, where the devices are, and the overall brightness.</summary>
public sealed record Scene(string Name, IReadOnlyList<Layer> Layers, IReadOnlyList<DevicePlacement> Placements, double Brightness = 1)
{
    public static Scene Empty(string name) => new(name, [], []);
}
