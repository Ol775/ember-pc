using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Govee;

public sealed record GoveeLanDevice(string Id, string Sku, IPAddress Ip);

/// <summary>
/// Govee's local network protocol: devices answer a multicast scan (239.255.255.250:4001, replies to our port 4002) and take
/// JSON commands on UDP port 4003. "LAN control" must be switched on for the device in the Govee app.
/// </summary>
public sealed class GoveeLanClient
{
    public const int ScanPort = 4001, ListenPort = 4002, ControlPort = 4003;
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.255.255.250");

    readonly IReadOnlyList<IPEndPoint>? _scanTargets;
    readonly int _listenPort, _controlPort;

    /// <param name="scanTargets">Where to send the scan; null means the multicast group on every network adapter. Tests pass a fake device.</param>
    public GoveeLanClient(IReadOnlyList<IPEndPoint>? scanTargets = null, int listenPort = ListenPort, int controlPort = ControlPort)
    {
        _scanTargets = scanTargets;
        _listenPort = listenPort;
        _controlPort = controlPort;
    }

    public async Task<IReadOnlyList<GoveeLanDevice>> DiscoverAsync(TimeSpan wait, CancellationToken ct = default)
    {
        var found = new Dictionary<string, GoveeLanDevice>();
        using var listener = new UdpClient(AddressFamily.InterNetwork);
        listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Client.Bind(new IPEndPoint(IPAddress.Any, _listenPort));

        var scan = Encoding.UTF8.GetBytes("""{"msg":{"cmd":"scan","data":{"account_topic":"reserved"}}}""");
        if (_scanTargets is not null)
        {
            foreach (var t in _scanTargets) await listener.SendAsync(scan, t, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var local in LocalAddresses())
            {
                try
                {
                    listener.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                    await listener.SendAsync(scan, new IPEndPoint(MulticastGroup, ScanPort), ct).ConfigureAwait(false);
                }
                catch (SocketException) { /* this adapter cannot do multicast */ }
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try
        {
            while (true)
            {
                var r = await listener.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (TryParseScanReply(r.Buffer, out var d)) found[d.Id] = d;
            }
        }
        catch (OperationCanceledException) { /* the waiting time is over */ }
        return found.Values.ToList();
    }

    static IEnumerable<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.SupportsMulticast)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork);

    /// <summary>Reads a scan reply; false for anything that is not one. Never throws.</summary>
    public static bool TryParseScanReply(ReadOnlySpan<byte> bytes, out GoveeLanDevice device)
    {
        device = null!;
        try
        {
            if (bytes.Length > 4096) return false;
            using var doc = JsonDocument.Parse(bytes.ToArray());
            var msg = doc.RootElement.GetProperty("msg");
            if (msg.GetProperty("cmd").GetString() != "scan") return false;
            var data = msg.GetProperty("data");
            var id = data.GetProperty("device").GetString();
            var sku = data.GetProperty("sku").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || string.IsNullOrWhiteSpace(sku) || sku.Length > 32) return false;
            if (!IPAddress.TryParse(data.GetProperty("ip").GetString(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
            device = new GoveeLanDevice(id, sku, ip);
            return true;
        }
        catch (Exception) { return false; }
    }

    public Task TurnAsync(IPAddress ip, bool on, CancellationToken ct = default) =>
        Send(ip, "{\"msg\":{\"cmd\":\"turn\",\"data\":{\"value\":" + (on ? 1 : 0) + "}}}", ct);

    public Task SetColorAsync(IPAddress ip, byte r, byte g, byte b, CancellationToken ct = default) =>
        Send(ip, "{\"msg\":{\"cmd\":\"colorwc\",\"data\":{\"color\":{\"r\":" + r + ",\"g\":" + g + ",\"b\":" + b + "},\"colorTemInKelvin\":0}}}", ct);

    async Task Send(IPAddress ip, string json, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        await udp.SendAsync(Encoding.UTF8.GetBytes(json), new IPEndPoint(ip, _controlPort), ct).ConfigureAwait(false);
    }
}

/// <summary>A lamp on the local network. It shows one colour: the average of the picture under it.</summary>
public sealed class GoveeLanSink(GoveeLanClient client, GoveeLanDevice device) : IDeviceSink
{
    bool? _on;

    public string Id => "govee:" + device.Id;
    public string Name => "Govee " + device.Sku;
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(120);
    public bool Keepalive => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var c = Compositor.Average(leds);
        var (r, g, b) = c.ToBytes();
        if (r == 0 && g == 0 && b == 0)
        {
            if (_on != false) { await client.TurnAsync(device.Ip, false, ct).ConfigureAwait(false); _on = false; }
            return;
        }
        if (_on != true) { await client.TurnAsync(device.Ip, true, ct).ConfigureAwait(false); _on = true; }
        await client.SetColorAsync(device.Ip, r, g, b, ct).ConfigureAwait(false);
    }
}
