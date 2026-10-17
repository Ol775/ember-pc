using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Nanoleaf;

/// <summary>Which controller Ember talks to. The token is not here: it is kept in Windows Credential Manager.</summary>
public sealed record NanoleafSettings(string Host)
{
    public const string KeyName = "nanoleaf-token";

    public static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 253 && (IPAddress.TryParse(host, out _) || host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'));
}

public sealed class NanoleafSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "nanoleaf.json");

    public NanoleafSettings? Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4096) return null;
            var s = JsonSerializer.Deserialize<NanoleafSettings>(File.ReadAllText(path));
            return s is not null && NanoleafSettings.IsValidHost(s.Host) ? s : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Saves the settings, or removes the file when given null. False if the folder cannot be written.</summary>
    public bool TrySave(NanoleafSettings? settings)
    {
        try
        {
            if (settings is null) { if (File.Exists(path)) File.Delete(path); return true; }
            if (!NanoleafSettings.IsValidHost(settings.Host)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Finds controllers on the home network with an SSDP search. Nothing leaves the network.</summary>
public static class NanoleafDiscovery
{
    public static async Task<IReadOnlyList<string>> FindAsync(TimeSpan wait, CancellationToken ct = default)
    {
        var found = new HashSet<string>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var request = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: ssdp:all\r\n\r\n");
        await udp.SendAsync(request, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait);
        try
        {
            while (found.Count < 8)
            {
                var r = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (ParseAddress(Encoding.ASCII.GetString(r.Buffer, 0, Math.Min(r.Buffer.Length, 2048))) is { } host) found.Add(host);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* the wait is over */ }
        return found.ToList();
    }

    /// <summary>The address in the Location header of a reply that comes from a Nanoleaf device; null for any other device.</summary>
    public static string? ParseAddress(string reply)
    {
        if (!reply.Contains("nanoleaf", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var line in reply.Split('\n'))
        {
            var l = line.Trim();
            if (!l.StartsWith("Location:", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(l["Location:".Length..].Trim(), UriKind.Absolute, out var u) && IPAddress.TryParse(u.Host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
        }
        return null;
    }
}

/// <summary>A Nanoleaf controller as one device with a light per panel, ordered left to right.</summary>
public sealed class NanoleafSink(NanoleafClient client, NanoleafInfo info) : IDeviceSink
{
    bool? _on;
    readonly NanoleafGrid? _grid = NanoleafGrid.TryBuild(info.Panels);
    /// <summary>The grid the panels form on the canvas, or null when they stay in a row.</summary>
    public NanoleafGrid? Grid => _grid;
    public NanoleafInfo Info => info;

    public string Id => "nanoleaf:" + (info.Serial.Length > 0 ? info.Serial : client.Host);
    public string Name => info.Name;
    public string Kind => "Light panels";
    public int LedCount => _grid?.Cells ?? info.Panels.Count;
    public int Columns => _grid?.Columns ?? 0;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(150);
    public bool Keepalive => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        // On a grid, each panel takes the colour of its own cell.
        if (_grid is { } grid) leds = grid.CellOfPanel.Select(cell => cell < leds.Count ? leds[cell] : Color.Black).ToList();
        var dark = leds.All(c => c.ToBytes() == (0, 0, 0));
        if (dark)
        {
            if (_on == false) return;
            await client.ShowAsync(info.Panels, leds, ct).ConfigureAwait(false);
            await client.SetOnAsync(false, ct).ConfigureAwait(false);
            _on = false;
            return;
        }
        if (_on != true) { await client.SetOnAsync(true, ct).ConfigureAwait(false); _on = true; }
        await client.ShowAsync(info.Panels, leds, ct).ConfigureAwait(false);
    }
}

/// <summary>Nanoleaf light panels. Does nothing until a controller has been linked in Settings.</summary>
public sealed class NanoleafSource(Func<NanoleafClient?> client) : IDeviceSource
{
    const string NotSetUp = "Not set up. Link your Nanoleaf controller in Settings.";
    NanoleafSink? _sink;
    NanoleafClient? _client;

    public string Name => "Nanoleaf";
    public string Status { get; private set; } = NotSetUp;

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var c = client();
        if (c is null) { _sink = null; _client = null; Status = NotSetUp; return []; }
        try
        {
            var info = await c.GetInfoAsync(ct).ConfigureAwait(false);
            if (info.Panels.Count == 0) { Status = $"Connected to {c.Host}, but it reports no light panels."; return []; }
            // A new sink only when something changed, so a send to the old one is not cut off by a refresh.
            if (!ReferenceEquals(c, _client) || _sink is null || _sink.Name != info.Name || !_sink.Info.Panels.SequenceEqual(info.Panels))
            {
                _sink = new NanoleafSink(c, info);
                _client = c;
            }
            Status = $"Connected to {info.Name} at {c.Host}: {info.Panels.Count} panel{(info.Panels.Count == 1 ? "" : "s")}.";
            return [_sink];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Nanoleaf: " + (ex is HttpRequestException or TaskCanceledException ? $"could not reach {c.Host}" : ex.Message);
            return [];
        }
    }

    public void ReportFailure(string sinkId) { /* the next refresh asks the controller again */ }
}
