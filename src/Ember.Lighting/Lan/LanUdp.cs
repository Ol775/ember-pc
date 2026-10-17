using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Ember.Lighting.Lan;

/// <summary>
/// The little UDP and TCP helpers the local-network integrations (WiZ, LIFX, Yeelight, Kasa) share. Everything stays on the
/// home network: a broadcast or multicast to find devices, then plain requests to the addresses that answered.
/// </summary>
public static class LanUdp
{
    public const int MaxReplies = 64, MaxDatagram = 4096;

    /// <summary>The private IPv4 addresses of this PC's adapters that are up (not loopback or tunnels).</summary>
    public static IReadOnlyList<(IPAddress Address, IPAddress Mask)> LocalNetworks()
    {
        var list = new List<(IPAddress, IPAddress)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork && Meross.MerossSource.IsPrivate(ua.Address) && ua.IPv4Mask is { } mask) list.Add((ua.Address, mask));
        }
        return list;
    }

    /// <summary>The directed broadcast address of a network (the address with every host bit set).</summary>
    public static IPAddress BroadcastOf(IPAddress address, IPAddress mask)
    {
        var a = address.GetAddressBytes();
        var m = mask.GetAddressBytes();
        for (var i = 0; i < 4; i++) a[i] |= (byte)~m[i];
        return new IPAddress(a);
    }

    /// <summary>
    /// Sends <paramref name="request"/> to every target (the broadcast address of each local network by default, or the
    /// multicast group on each adapter) and collects the replies that come back within <paramref name="wait"/>.
    /// </summary>
    /// <param name="targets">Where to send it; null means broadcast on <paramref name="port"/>. Tests pass a fake device.</param>
    public static async Task<IReadOnlyList<(IPEndPoint From, byte[] Data)>> AskAsync(byte[] request, int port, TimeSpan wait,
        IReadOnlyList<IPEndPoint>? targets, CancellationToken ct)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        if (targets is not null)
        {
            foreach (var t in targets) await udp.SendAsync(request, t, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var (address, mask) in LocalNetworks())
            {
                try { await udp.SendAsync(request, new IPEndPoint(BroadcastOf(address, mask), port), ct).ConfigureAwait(false); }
                catch (SocketException) { /* this adapter cannot broadcast */ }
            }
        }
        return await ReceiveAsync(udp, wait, ct).ConfigureAwait(false);
    }

    /// <summary>The same for a multicast group (Yeelight): sent out of every local adapter in turn.</summary>
    public static async Task<IReadOnlyList<(IPEndPoint From, byte[] Data)>> AskMulticastAsync(byte[] request, IPEndPoint group, TimeSpan wait,
        IReadOnlyList<IPEndPoint>? testTargets, CancellationToken ct)
    {
        if (testTargets is not null) return await AskAsync(request, group.Port, wait, testTargets, ct).ConfigureAwait(false);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        foreach (var (address, _) in LocalNetworks())
        {
            try
            {
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                await udp.SendAsync(request, group, ct).ConfigureAwait(false);
            }
            catch (SocketException) { /* this adapter cannot do multicast */ }
        }
        return await ReceiveAsync(udp, wait, ct).ConfigureAwait(false);
    }

    /// <summary>One request to one device and its one reply, or null when nothing came back in time.</summary>
    public static async Task<byte[]?> RequestAsync(IPEndPoint to, byte[] request, TimeSpan wait, CancellationToken ct)
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        await udp.SendAsync(request, to, ct).ConfigureAwait(false);
        var replies = await ReceiveAsync(udp, wait, ct, max: 1).ConfigureAwait(false);
        return replies.Count > 0 ? replies[0].Data : null;
    }

    /// <summary>A datagram with no reply wanted (a colour change that needs no answer).</summary>
    public static async Task SendAsync(IPEndPoint to, byte[] data, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        await udp.SendAsync(data, to, ct).ConfigureAwait(false);
    }

    static async Task<IReadOnlyList<(IPEndPoint From, byte[] Data)>> ReceiveAsync(UdpClient udp, TimeSpan wait, CancellationToken ct, int max = MaxReplies)
    {
        var replies = new List<(IPEndPoint From, byte[] Data)>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try
        {
            while (replies.Count < max)
            {
                var r = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (r.Buffer.Length <= MaxDatagram) replies.Add((r.RemoteEndPoint, r.Buffer));
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* the wait is over */ }
        catch (SocketException) { /* an ICMP error from a device that went away */ }
        return replies;
    }

    /// <summary>A hue (degrees), saturation and value (0 to 1) of a colour, for the bulbs that take those.</summary>
    public static (double H, double S, double V) ToHsv(Color c)
    {
        var (r, g, b) = (c.R, c.G, c.B);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var h = d == 0 ? 0 : max == r ? 60 * (((g - b) / d % 6 + 6) % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return (h, max == 0 ? 0 : d / max, max);
    }
}
