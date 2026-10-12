using System.Buffers.Binary;
using System.Text;

namespace Ember.Lighting.OpenRgb;

public sealed record OpenRgbZone(string Name, int LedCount);

/// <summary>One lighting device as the OpenRGB server describes it.</summary>
public sealed record OpenRgbDevice(
    int Index, string Name, string Vendor, string TypeName, string Location, int LedCount,
    IReadOnlyList<string> Modes, IReadOnlyList<OpenRgbZone> Zones)
{
    /// <summary>Stable across restarts (the index is not): name plus where the device sits.</summary>
    public string Id => "openrgb:" + Name + "|" + Location;
}

/// <summary>The OpenRGB SDK wire format. Trust boundary: the server is another program, so every length is checked and nothing throws except <see cref="OpenRgbProtocolException"/>.</summary>
public static class OpenRgbCodec
{
    public const int HeaderSize = 16, MaxPacket = 4 * 1024 * 1024, MaxClientVersion = 5;
    const int MaxString = 4096, MaxModes = 256, MaxZones = 256, MaxLeds = 8192, MaxColors = 8192, MaxMatrix = 1 << 16, MaxSegments = 256;

    public const uint RequestControllerCount = 0, RequestControllerData = 1, RequestProtocolVersion = 40, SetClientName = 50,
        DeviceListUpdated = 100, UpdateLeds = 1050, SetCustomMode = 1100;

    static readonly string[] TypeNames =
    [
        "Motherboard", "Memory", "Graphics card", "Cooler", "LED strip", "Keyboard", "Mouse", "Mouse mat", "Headset",
        "Headset stand", "Gamepad", "Light", "Speaker", "Virtual", "Storage", "Case", "Keyboard (other)", "Accessory", "Laptop",
    ];

    public static byte[] Packet(uint device, uint id, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[HeaderSize + payload.Length];
        "ORGB"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), device);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), id);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)payload.Length);
        payload.CopyTo(bytes.AsSpan(HeaderSize));
        return bytes;
    }

    /// <summary>Reads a packet header; false if the magic is wrong or the size is not allowed.</summary>
    public static bool TryParseHeader(ReadOnlySpan<byte> header, out uint device, out uint id, out int size)
    {
        device = id = 0;
        size = 0;
        if (header.Length < HeaderSize || !header[..4].SequenceEqual("ORGB"u8)) return false;
        device = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        id = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var n = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        if (n > MaxPacket) return false;
        size = (int)n;
        return true;
    }

    public static byte[] UInt32(uint value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        return b;
    }

    public static byte[] Name(string name)
    {
        var text = Encoding.UTF8.GetBytes(name);
        var b = new byte[text.Length + 1]; // the server expects a terminating zero
        text.CopyTo(b, 0);
        return b;
    }

    /// <summary>Payload of UPDATELEDS: total size, colour count, then one 4-byte colour (red, green, blue, 0) per LED.</summary>
    public static byte[] LedColors(IReadOnlyList<Color> leds)
    {
        var size = 4 + 2 + leds.Count * 4;
        var b = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)size);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), (ushort)leds.Count);
        for (var i = 0; i < leds.Count; i++)
        {
            var (r, g, bl) = leds[i].ToBytes();
            b[6 + i * 4] = r;
            b[7 + i * 4] = g;
            b[8 + i * 4] = bl;
        }
        return b;
    }

    sealed class Reader(byte[] data)
    {
        int _pos;
        public int Left => data.Length - _pos;

        void Need(int n)
        {
            if (n < 0 || n > Left) throw new OpenRgbProtocolException("The device description ended early.");
        }

        public uint U32() { Need(4); var v = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_pos)); _pos += 4; return v; }
        public int U16() { Need(2); var v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_pos)); _pos += 2; return v; }

        public int Count(int max)
        {
            var n = U16();
            if (n > max) throw new OpenRgbProtocolException("The device description has an unreasonable size.");
            return n;
        }

        public void Skip(long n)
        {
            if (n < 0 || n > Left) throw new OpenRgbProtocolException("The device description ended early.");
            _pos += (int)n;
        }

        public string Str()
        {
            var len = U16();
            if (len > MaxString) throw new OpenRgbProtocolException("A text field in the device description is too long.");
            Need(len);
            var s = Encoding.UTF8.GetString(data, _pos, len).TrimEnd('\0');
            _pos += len;
            return s;
        }
    }

    /// <summary>Parses the body of a REQUEST_CONTROLLER_DATA reply (without its leading size field) for protocol <paramref name="version"/> (0 to 5).</summary>
    public static OpenRgbDevice ParseController(int index, byte[] data, int version)
    {
        var r = new Reader(data);
        var type = r.U32();
        var name = r.Str();
        var vendor = version >= 1 ? r.Str() : "";
        r.Str(); // description
        r.Str(); // version
        r.Str(); // serial
        var location = r.Str();

        var modes = new List<string>();
        var modeCount = r.Count(MaxModes);
        r.U32(); // active mode
        for (var i = 0; i < modeCount; i++)
        {
            modes.Add(r.Str());
            // value, flags, speed min/max, [brightness min/max], colours min/max, speed, [brightness], direction, colour mode
            r.Skip(4 * (version >= 3 ? 12 : 9));
            var colours = r.Count(MaxColors);
            r.Skip(colours * 4L);
        }

        var zones = new List<OpenRgbZone>();
        var zoneCount = r.Count(MaxZones);
        for (var i = 0; i < zoneCount; i++)
        {
            var zoneName = r.Str();
            r.U32(); // type
            r.U32(); // leds min
            r.U32(); // leds max
            var leds = (int)Math.Min(r.U32(), (uint)MaxLeds);
            var matrix = r.Count(MaxMatrix);
            if (matrix > 0)
            {
                var h = r.U32();
                var w = r.U32();
                r.Skip(h * (long)w * 4);
            }
            if (version >= 4)
            {
                var segments = r.Count(MaxSegments);
                for (var s = 0; s < segments; s++) { r.Str(); r.U32(); r.U32(); r.U32(); }
            }
            if (version >= 5) r.U32(); // zone flags
            zones.Add(new OpenRgbZone(zoneName, leds));
        }

        var ledCount = r.Count(MaxLeds);
        return new OpenRgbDevice(index, name, vendor, type < TypeNames.Length ? TypeNames[type] : "Device", location, ledCount, modes, zones);
    }
}

public sealed class OpenRgbProtocolException(string message) : Exception(message);
