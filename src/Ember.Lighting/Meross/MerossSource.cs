using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Meross;

/// <summary>A Meross bulb or light strip, driven on the local network: one colour, changed slowly.</summary>
public sealed class MerossLightSink(MerossLocalClient local, MerossDevice device, string address, string key) : IDeviceSink
{
    bool? _on;

    public string Id => "meross:" + device.Uuid;
    public string Name => device.Name;
    public string Kind => "Light";
    public int LedCount => 1;
    public string Address => address;
    public TimeSpan MinInterval => TimeSpan.FromSeconds(1.5);
    public bool Keepalive => false;
    public bool Realtime => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var (r, g, b) = Compositor.Average(leds).ToBytes();
        if (r == 0 && g == 0 && b == 0)
        {
            if (_on != false) { await local.SetPowerAsync(address, key, false, ct).ConfigureAwait(false); _on = false; }
            return;
        }
        if (_on != true) { await local.SetPowerAsync(address, key, true, ct).ConfigureAwait(false); _on = true; }
        await local.SetColourAsync(address, key, r, g, b, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Meross bulbs and light strips only: plugs, hubs and thermostats are never touched. Ember signs in to the Meross account once, only to list the devices and learn the key they check
/// commands against, then finds each device on the local network and talks to it directly. Does nothing until an account has
/// been saved in Settings.
/// </summary>
public sealed class MerossSource(Func<MerossAccount?> session, MerossCloudClient cloud, MerossLocalClient local) : IDeviceSource
{
    const string NotSetUp = "Not set up. Sign in to your Meross account in Settings.";
    static readonly TimeSpan ListFor = TimeSpan.FromMinutes(10), ScanEvery = TimeSpan.FromSeconds(45);

    readonly Dictionary<string, string> _addresses = [];
    readonly Dictionary<string, IDeviceSink> _sinks = [];
    MerossAccount? _account;
    IReadOnlyList<MerossDevice> _devices = [];
    DateTime _listedAt, _scannedAt;

    public string Name => "Meross";
    public string Status { get; private set; } = NotSetUp;

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var saved = session();
        if (saved is null) { Forget(); Status = NotSetUp; return []; }
        var now = DateTime.UtcNow;
        try
        {
            if (_account != saved || now - _listedAt > ListFor)
            {
                if (_account != saved) Forget();
                _devices = await cloud.GetDevicesAsync(saved, ct).ConfigureAwait(false);
                _account = saved;
                _listedAt = now;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Meross: " + (ex is HttpRequestException ? "could not reach the Meross account service" : ex.Message);
            return [];
        }

        var usable = _devices.Where(d => d.IsLight).ToList();
        if (usable.Count == 0) { Status = "Signed in, but the account has no Meross bulbs or light strips."; _sinks.Clear(); return []; }

        if (usable.Any(d => !_addresses.ContainsKey(d.Uuid)) && now - _scannedAt > ScanEvery)
        {
            _scannedAt = now;
            foreach (var (uuid, address) in await ScanAsync(local, _account!.Key, usable.Select(d => d.Uuid).Where(u => !_addresses.ContainsKey(u)).ToHashSet(), ct).ConfigureAwait(false))
            {
                _addresses[uuid] = address;
                _sinks.Remove("meross:" + uuid);
            }
        }

        var result = new List<IDeviceSink>();
        foreach (var d in usable)
        {
            if (!_addresses.TryGetValue(d.Uuid, out var address)) continue;
            if (!_sinks.TryGetValue("meross:" + d.Uuid, out var sink))
                _sinks["meross:" + d.Uuid] = sink = new MerossLightSink(local, d, address, _account!.Key);
            result.Add(sink);
        }
        var missing = usable.Where(d => !_addresses.ContainsKey(d.Uuid)).Select(d => $"{d.Name} ({d.Type})").ToList();
        Status = result.Count == 0
            ? $"Signed in, but none of the {usable.Count} lights answered on this network yet. They must be on the same Wi-Fi as this PC. Not found: {string.Join(", ", missing)}."
            : $"{result.Count} on the network" + (missing.Count > 0 ? $", not found yet: {string.Join(", ", missing)}" : "");
        return result;
    }

    public void ReportFailure(string sinkId)
    {
        // The device may have a new address: forget it so the next refresh looks for it again.
        var uuid = sinkId.StartsWith("meross:", StringComparison.Ordinal) ? sinkId[7..] : sinkId;
        _addresses.Remove(uuid);
        _sinks.Remove(sinkId);
        _scannedAt = default;
    }

    void Forget()
    {
        _account = null; _devices = []; _addresses.Clear(); _sinks.Clear(); _listedAt = _scannedAt = default;
    }

    /// <summary>Asks every address on this PC's own networks (each as a /24) whether a Meross device lives there; keeps the ones wanted.</summary>
    internal static async Task<List<(string Uuid, string Address)>> ScanAsync(MerossLocalClient local, string key, HashSet<string> wanted, CancellationToken ct)
    {
        var found = new List<(string, string)>();
        using var gate = new SemaphoreSlim(48);
        var tasks = Hosts().Select(async address =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var uuid = await local.ProbeAsync(address, key, ct).ConfigureAwait(false);
                if (uuid is not null && wanted.Contains(uuid)) lock (found) found.Add((uuid, address));
            }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return found;
    }

    /// <summary>Every other address in the /24 of each private IPv4 address of this PC.</summary>
    public static IEnumerable<string> Hosts()
    {
        var seen = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ua.Address)) continue;
                foreach (var h in Subnet24(ua.Address)) if (seen.Add(h)) yield return h;
            }
        }
    }

    public static IEnumerable<string> Subnet24(IPAddress own)
    {
        var b = own.GetAddressBytes();
        for (var i = 1; i < 255; i++)
            if (i != b[3]) yield return $"{b[0]}.{b[1]}.{b[2]}.{i}";
    }

    public static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
