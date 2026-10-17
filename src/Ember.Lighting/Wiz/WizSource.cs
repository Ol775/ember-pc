using System.Net;
using System.Text;
using System.Text.Json;
using Ember.Lighting.Lan;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Wiz;

public sealed record WizDevice(string Mac, IPAddress Ip);

/// <summary>WiZ bulbs speak small JSON messages over UDP port 38899 on the home network. No account and no cloud.</summary>
public static class WizProtocol
{
    public const int Port = 38899;

    public static readonly byte[] Discover = Encoding.UTF8.GetBytes("""{"method":"getPilot","params":{}}""");

    /// <summary>The device in a reply to <see cref="Discover"/>, or null when the datagram is anything else.</summary>
    public static string? ParseMac(byte[] reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
            if (!result.TryGetProperty("mac", out var mac) || mac.ValueKind != JsonValueKind.String) return null;
            var text = mac.GetString() ?? "";
            return text.Length is >= 6 and <= 20 && text.All(Uri.IsHexDigit) ? text.ToLowerInvariant() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A colour, with the brightness carried by "dimming" (WiZ ignores very dark colours).</summary>
    public static byte[] Colour(Color c)
    {
        var (_, _, v) = LanUdp.ToHsv(c);
        var (r, g, b) = (v <= 0 ? c : c.Scaled(1 / v)).ToBytes();
        var dimming = (int)Math.Clamp(Math.Round(v * 100), 10, 100);
        return Encoding.UTF8.GetBytes($$$"""{"method":"setPilot","params":{"state":true,"r":{{{r}}},"g":{{{g}}},"b":{{{b}}},"dimming":{{{dimming}}}}}""");
    }

    public static readonly byte[] Off = Encoding.UTF8.GetBytes("""{"method":"setPilot","params":{"state":false}}""");
}

/// <summary>One WiZ bulb as one light, changed a few times a second.</summary>
public sealed class WizSink(WizDevice device, Action<string> failed, int port = WizProtocol.Port) : IDeviceSink
{
    public WizDevice Device => device;
    bool? _on;
    Color _last;

    public string Id => "wiz:" + device.Mac;
    public string Name => "WiZ light " + device.Mac[^4..];
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(250);
    public bool Keepalive => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var c = Compositor.Average(leds);
        var (r, g, b) = c.ToBytes();
        try
        {
            var to = new IPEndPoint(device.Ip, port);
            if (r == 0 && g == 0 && b == 0)
            {
                if (_on == false) return;
                await LanUdp.SendAsync(to, WizProtocol.Off, ct).ConfigureAwait(false);
                _on = false;
                return;
            }
            if (_on == true && c == _last) return;
            await LanUdp.SendAsync(to, WizProtocol.Colour(c), ct).ConfigureAwait(false);
            (_on, _last) = (true, c);
        }
        catch (System.Net.Sockets.SocketException) { failed(device.Mac); throw; }
    }
}

/// <summary>WiZ bulbs on the network. Does nothing but ask the network which ones answer.</summary>
public sealed class WizSource(IReadOnlyList<IPEndPoint>? testTargets = null, int port = WizProtocol.Port) : IDeviceSource
{
    static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(1), ScanFor = TimeSpan.FromSeconds(1.2);
    readonly Dictionary<string, (WizDevice Device, DateTime Seen)> _known = [];
    readonly Dictionary<string, WizSink> _sinks = [];

    public string Name => "WiZ";
    public string Status { get; private set; } = "Looking for bulbs…";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var (from, data) in await LanUdp.AskAsync(WizProtocol.Discover, port, ScanFor, testTargets, ct).ConfigureAwait(false))
                if (WizProtocol.ParseMac(data) is { } mac) _known[mac] = (new WizDevice(mac, from.Address), now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Could not scan the network: " + ex.Message; return Sinks(); }
        foreach (var gone in _known.Where(kv => now - kv.Value.Seen > KeepFor).Select(kv => kv.Key).ToList()) { _known.Remove(gone); _sinks.Remove(gone); }
        Status = _known.Count == 0 ? "No bulbs found. WiZ bulbs must be on the same Wi-Fi as this PC, with local communication on in the WiZ app (Settings, Privacy)."
            : $"{_known.Count} on the network";
        return Sinks();
    }

    IReadOnlyList<IDeviceSink> Sinks()
    {
        foreach (var (mac, (device, _)) in _known)
            if (!_sinks.TryGetValue(mac, out var s) || s.Device != device) _sinks[mac] = new WizSink(device, m => _known.Remove(m), port);
        return _sinks.Values.ToList();
    }

    public void ReportFailure(string sinkId) { /* UDP has no connection to drop; the next scan decides */ }
}
