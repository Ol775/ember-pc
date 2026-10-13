using System.Text;

namespace Ember.Lighting.Govee;

/// <summary>
/// The per-segment ("razer", DreamView) packets some Govee lamps accept on the same UDP port as the normal commands. The
/// layout is the one OpenRGB uses: 0xBB, a two-byte length, 0xB0, a gradient-off flag, the segment count, then red, green
/// and blue for each segment, and an XOR of every byte before it. Lamps that do not support it ignore the packets.
/// </summary>
public static class GoveeRazer
{
    public const int MaxSegments = 255;

    public static byte[] Enable { get; } = WithChecksum([0xBB, 0x00, 0x01, 0xB1, 0x01]);
    public static byte[] Disable { get; } = WithChecksum([0xBB, 0x00, 0x01, 0xB1, 0x00]);

    public static byte[] Frame(IReadOnlyList<Color> segments)
    {
        var count = Math.Min(segments.Count, MaxSegments);
        var payload = 2 + 3 * count;
        var bytes = new List<byte> { 0xBB, (byte)(payload >> 8), (byte)payload, 0xB0, 0x01, (byte)count };
        for (var i = 0; i < count; i++)
        {
            var (r, g, b) = segments[i].ToBytes();
            bytes.Add(r); bytes.Add(g); bytes.Add(b);
        }
        return WithChecksum(bytes.ToArray());
    }

    /// <summary>The JSON message that carries a packet.</summary>
    public static string Message(byte[] packet) =>
        "{\"msg\":{\"cmd\":\"razer\",\"data\":{\"pt\":\"" + Convert.ToBase64String(packet) + "\"}}}";

    static byte[] WithChecksum(byte[] bytes)
    {
        byte x = 0;
        foreach (var b in bytes) x ^= b;
        return [.. bytes, x];
    }
}
