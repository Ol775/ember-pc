using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ember.Lighting.Lan;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Lifx;

public sealed record LifxDevice(string Mac, IPAddress Ip, string? Label);

/// <summary>LIFX lights speak a small binary protocol over UDP port 56700 on the home network. No account and no cloud.</summary>
public static class LifxProtocol
{
    public const int Port = 56700, HeaderSize = 36;
    public const ushort GetService = 2, StateService = 3, GetLabel = 23, StateLabel = 25, SetColor = 102, SetPower = 117;

    static readonly uint Source = 0x456D6272;   // "Embr": how the lights know who is asking

    /// <summary>A message: the 36 byte header (target all zeros reaches every light) and the payload.</summary>
    public static byte[] Packet(ushort type, byte[]? target, byte[] payload, bool tagged)
    {
        var p = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(p, (ushort)p.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(2), (ushort)(1024 | 1 << 12 | (tagged ? 1 << 13 : 0)));
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), Source);
        if (target is { Length: 6 }) target.CopyTo(p, 8);
        p[22] = (byte)(type == GetService || type == GetLabel ? 1 : 0);   // res_required: only for questions
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(32), type);
        payload.CopyTo(p, HeaderSize);
        return p;
    }

    public static byte[] Discover => Packet(GetService, null, [], tagged: true);

    public static byte[] AskLabel(string mac) => Packet(GetLabel, MacBytes(mac), [], tagged: false);

    /// <summary>The MAC (as twelve hex digits) of the light that sent a StateService reply for UDP, or null for anything else.</summary>
    public static string? ParseService(byte[] data)
    {
        if (data.Length < HeaderSize + 5 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32)) != StateService) return null;
        if (data[HeaderSize] != 1) return null;   // service 1 is UDP
        return Convert.ToHexString(data.AsSpan(8, 6)).ToLowerInvariant();
    }

    public static string? ParseLabel(byte[] data)
    {
        if (data.Length < HeaderSize + 32 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(32)) != StateLabel) return null;
        var text = Encoding.UTF8.GetString(data, HeaderSize, 32).TrimEnd('\0').Trim();
        return text.Length == 0 ? null : text;
    }

    public static byte[] MacBytes(string mac) => Convert.FromHexString(mac);

    /// <summary>Colour and brightness at once, changed over <paramref name="milliseconds"/>.</summary>
    public static byte[] Colour(string mac, Color c, uint milliseconds)
    {
        var (h, s, v) = LanUdp.ToHsv(c);
        var payload = new byte[13];
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(1), (ushort)Math.Round(h / 360 * 65535));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(3), (ushort)Math.Round(s * 65535));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(5), (ushort)Math.Round(v * 65535));
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(7), 3500);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(9), milliseconds);
        return Packet(SetColor, MacBytes(mac), payload, tagged: false);
    }

    public static byte[] Power(string mac, bool on)
    {
        var payload = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, on ? (ushort)65535 : (ushort)0);
        return Packet(SetPower, MacBytes(mac), payload, tagged: false);
    }
}

/// <summary>One LIFX light as one light.</summary>
public sealed class LifxSink(LifxDevice device, int port = LifxProtocol.Port) : IDeviceSink
{
    public LifxDevice Device => device;
    bool? _on;
    Color _last;

    public string Id => "lifx:" + device.Mac;
    public string Name => device.Label is { Length: > 0 } l ? l : "LIFX light " + device.Mac[^4..];
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(200);
    public bool Keepalive => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var c = Compositor.Average(leds);
        var (r, g, b) = c.ToBytes();
        var to = new IPEndPoint(device.Ip, port);
        if (r == 0 && g == 0 && b == 0)
        {
            if (_on == false) return;
            await LanUdp.SendAsync(to, LifxProtocol.Power(device.Mac, false), ct).ConfigureAwait(false);
            _on = false;
            return;
        }
        if (_on == true && c == _last) return;
        if (_on != true) await LanUdp.SendAsync(to, LifxProtocol.Power(device.Mac, true), ct).ConfigureAwait(false);
        await LanUdp.SendAsync(to, LifxProtocol.Colour(device.Mac, c, 150), ct).ConfigureAwait(false);
        (_on, _last) = (true, c);
    }
}

/// <summary>LIFX lights on the network.</summary>
public sealed class LifxSource(IReadOnlyList<IPEndPoint>? testTargets = null, int port = LifxProtocol.Port) : IDeviceSource
{
    static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(1), ScanFor = TimeSpan.FromSeconds(1.2);
    readonly Dictionary<string, (LifxDevice Device, DateTime Seen)> _known = [];
    readonly Dictionary<string, LifxSink> _sinks = [];

    public string Name => "LIFX";
    public string Status { get; private set; } = "Looking for lights…";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var (from, data) in await LanUdp.AskAsync(LifxProtocol.Discover, port, ScanFor, testTargets, ct).ConfigureAwait(false))
            {
                if (LifxProtocol.ParseService(data) is not { } mac) continue;
                var label = _known.TryGetValue(mac, out var old) ? old.Device.Label : null;
                if (label is null)
                {
                    var answer = await LanUdp.RequestAsync(new IPEndPoint(from.Address, port), LifxProtocol.AskLabel(mac), TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false);
                    label = answer is null ? null : LifxProtocol.ParseLabel(answer);
                }
                _known[mac] = (new LifxDevice(mac, from.Address, label), now);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Could not scan the network: " + ex.Message; return Sinks(); }
        foreach (var gone in _known.Where(kv => now - kv.Value.Seen > KeepFor).Select(kv => kv.Key).ToList()) { _known.Remove(gone); _sinks.Remove(gone); }
        Status = _known.Count == 0 ? "No lights found. LIFX lights must be on the same Wi-Fi as this PC." : $"{_known.Count} on the network";
        return Sinks();
    }

    IReadOnlyList<IDeviceSink> Sinks()
    {
        foreach (var (mac, (device, _)) in _known)
            if (!_sinks.TryGetValue(mac, out var s) || s.Device != device) _sinks[mac] = new LifxSink(device, port);
        return _sinks.Values.ToList();
    }

    public void ReportFailure(string sinkId) { /* UDP has no connection to drop; the next scan decides */ }
}
