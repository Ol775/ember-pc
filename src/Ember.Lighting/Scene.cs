namespace Ember.Lighting;

public enum BlendMode { Normal, Add, Screen, Multiply }

public sealed record Layer(string Id, string Name, Effect Effect, double Opacity = 1, BlendMode Blend = BlendMode.Normal, bool Enabled = true);

/// <summary>
/// Where a device sits on the canvas (the unit square). Its LEDs run along the longer side. <paramref name="Profile"/> is
/// the name of another scene whose layers this device shows instead of the current scene's (its own light profile); null follows the scene.
/// <paramref name="Paint"/> fixes single LEDs to a colour ("#RRGGBB", or null to leave that LED to the layers).
/// </summary>
public sealed record DevicePlacement(string DeviceId, double X, double Y, double Width, double Height, bool Enabled = true, string? Profile = null, IReadOnlyList<string?>? Paint = null)
{
    /// <summary>
    /// The canvas position of LED <paramref name="index"/> of <paramref name="count"/>. With <paramref name="columns"/> above 1
    /// the LEDs are a grid (row by row, that many to a row) spread over the whole rectangle, like the keys of a keyboard.
    /// </summary>
    public (double X, double Y) LedPosition(int index, int count, int columns = 0)
    {
        var cx = X + Width / 2;
        var cy = Y + Height / 2;
        if (count <= 1) return (cx, cy);
        if (columns > 1 && count % columns == 0)
        {
            var rows = count / columns;
            return (X + (index % columns + 0.5) / columns * Width, Y + (index / columns + 0.5) / rows * Height);
        }
        var f = (index + 0.5) / count;
        return Width >= Height ? (X + f * Width, cy) : (cx, Y + f * Height);
    }

    /// <summary>Where LED <paramref name="index"/> sits inside its own device (see <see cref="LedLocal"/>), laid out the same way as <see cref="LedPosition"/>.</summary>
    public LedLocal LocalPosition(int index, int count, int columns = 0)
    {
        if (count <= 1) return new LedLocal(0.5, 0.5, 1);
        if (columns > 1 && count % columns == 0)
        {
            var rows = count / columns;
            return new LedLocal((index % columns + 0.5) / columns, (index / columns + 0.5) / rows, rows);
        }
        var f = (index + 0.5) / count;
        return Width >= Height ? new LedLocal(f, 0.5, 1) : new LedLocal(0.5, f, count);
    }
}

/// <summary>A saved arrangement: the layers, where the devices are, and the overall brightness.</summary>
public sealed record Scene(string Name, IReadOnlyList<Layer> Layers, IReadOnlyList<DevicePlacement> Placements, double Brightness = 1)
{
    public static Scene Empty(string name) => new(name, [], []);
}
