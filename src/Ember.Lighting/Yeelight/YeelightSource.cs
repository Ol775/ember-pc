using System.Net;
using System.Net.Sockets;
using System.Text;
using Ember.Lighting.Lan;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Yeelight;

public sealed record YeelightDevice(string Id, IPAddress Ip, int Port, string Model, string? Name);

/// <summary>
/// Yeelight bulbs answer a multicast search (239.255.255.250:1982) and take JSON commands over TCP, once "LAN Control" is
/// switched on for the bulb in the Yeelight app. No account and no cloud.
/// </summary>
public static class YeelightProtocol
{
    public static readonly IPEndPoint Group = new(IPAddress.Parse("239.255.255.250"), 1982);

    public static readonly byte[] Search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n\r\n");

    /// <summary>The colour bulb in a search reply, or null for anything else (white-only bulbs cannot show colours).</summary>
    public static YeelightDevice? ParseReply(byte[] data)
    {
        var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 2048));
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var i = line.IndexOf(':');
            if (i > 0) headers[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        if (!headers.TryGetValue("Location", out var location) || !location.StartsWith("yeelight://", StringComparison.OrdinalIgnoreCase)) return null;
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return null;
        if (!headers.TryGetValue("support", out var support) || !support.Split(' ').Contains("set_rgb")) return null;
        var id = headers.GetValueOrDefault("id") ?? uri.Host;
        if (id.Length is 0 or > 40 || !id.All(c => char.IsAsciiLetterOrDigit(c))) return null;
        return new YeelightDevice(id, ip, uri.Port > 0 ? uri.Port : 55443, headers.GetValueOrDefault("model") ?? "color", headers.GetValueOrDefault("name") is { Length: > 0 and <= 40 } n && n.All(c => !char.IsControl(c)) ? n : null);
    }

    public static string SetRgb(int rgb, int milliseconds) => $$"""{"id":1,"method":"set_rgb","params":[{{rgb}},"smooth",{{milliseconds}}]}""" + "\r\n";

    public static string SetPower(bool on, int milliseconds) => $$"""{"id":2,"method":"set_power","params":["{{(on ? "on" : "off")}}","smooth",{{milliseconds}}]}""" + "\r\n";
}

/// <summary>One Yeelight bulb as one light. Yeelight allows about one command a second, so it follows slow changes.</summary>
public sealed class YeelightSink(YeelightDevice device, Action<string> failed, int? testPort = null) : IDeviceSink
{
    bool? _on;
    int _lastRgb = -1;

    public YeelightDevice Device => device;
    public string Id => "yeelight:" + device.Id;
    public string Name => device.Name ?? "Yeelight " + device.Id[^Math.Min(4, device.Id.Length)..];
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromSeconds(1.5);
    public bool Keepalive => false;
    public bool Realtime => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var (r, g, b) = Compositor.Average(leds).ToBytes();
        var rgb = r << 16 | g << 8 | b;
        try
        {
            if (rgb == 0)
            {
                if (_on == false) return;
                await CommandAsync(YeelightProtocol.SetPower(false, 500), ct).ConfigureAwait(false);
                (_on, _lastRgb) = (false, -1);
                return;
            }
            if (_on == true && rgb == _lastRgb) return;
            if (_on != true) await CommandAsync(YeelightProtocol.SetPower(true, 500), ct).ConfigureAwait(false);
            await CommandAsync(YeelightProtocol.SetRgb(rgb, 500), ct).ConfigureAwait(false);
            (_on, _lastRgb) = (true, rgb);
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException) { failed(device.Id); throw; }
    }

    /// <summary>One command on its own short connection, closed straight after the answer, so no connection is left open.</summary>
    async Task CommandAsync(string line, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await tcp.ConnectAsync(device.Ip, testPort ?? device.Port, timeout.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(line), timeout.Token).ConfigureAwait(false);
            var buffer = new byte[512];
            _ = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("The bulb did not answer."); }
    }
}

/// <summary>Yeelight colour bulbs on the network.</summary>
public sealed class YeelightSource(IReadOnlyList<IPEndPoint>? testTargets = null) : IDeviceSource
{
    static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(2), ScanFor = TimeSpan.FromSeconds(1.5);
    readonly Dictionary<string, (YeelightDevice Device, DateTime Seen)> _known = [];
    readonly Dictionary<string, YeelightSink> _sinks = [];

    public string Name => "Yeelight";
    public string Status { get; private set; } = "Looking for bulbs…";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var (_, data) in await LanUdp.AskMulticastAsync(YeelightProtocol.Search, YeelightProtocol.Group, ScanFor, testTargets, ct).ConfigureAwait(false))
                if (YeelightProtocol.ParseReply(data) is { } d) _known[d.Id] = (d, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Could not scan the network: " + ex.Message; return Sinks(); }
        foreach (var gone in _known.Where(kv => now - kv.Value.Seen > KeepFor).Select(kv => kv.Key).ToList()) { _known.Remove(gone); _sinks.Remove(gone); }
        Status = _known.Count == 0 ? "No bulbs found. Switch on LAN Control for each bulb in the Yeelight app, and keep them on the same Wi-Fi as this PC." : $"{_known.Count} on the network";
        return Sinks();
    }

    IReadOnlyList<IDeviceSink> Sinks()
    {
        foreach (var (id, (device, _)) in _known)
            if (!_sinks.TryGetValue(id, out var s) || s.Device != device) _sinks[id] = new YeelightSink(device, m => _known.Remove(m));
        return _sinks.Values.ToList();
    }

    public void ReportFailure(string sinkId) { /* a failed bulb is forgotten by its own sink; the next scan finds it again */ }
}
