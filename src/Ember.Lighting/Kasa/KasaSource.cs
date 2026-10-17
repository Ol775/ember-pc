using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ember.Lighting.Lan;
using Ember.Lighting.Meross;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Kasa;

/// <summary>What a Kasa device says about itself.</summary>
public sealed record KasaInfo(string Id, string Alias, string Model, bool IsLight, bool IsColour, bool IsPlug, bool HasPower);

public sealed record KasaFound(IPAddress Ip, KasaInfo Info);

/// <summary>
/// TP-Link Kasa devices (the older ones) speak JSON, scrambled with a fixed running XOR, over UDP and TCP port 9999 on the home
/// network. No account and no cloud. Newer Kasa firmware uses a signed-in protocol that this does not speak: those devices
/// are simply not found. Plugs are only ever asked what they draw; there is no command here that switches a relay.
/// </summary>
public static class KasaProtocol
{
    public const int Port = 9999;

    public static byte[] Encrypt(string json)
    {
        var data = Encoding.UTF8.GetBytes(json);
        var key = 171;
        for (var i = 0; i < data.Length; i++) { key ^= data[i]; data[i] = (byte)key; }
        return data;
    }

    public static string Decrypt(ReadOnlySpan<byte> data)
    {
        var plain = new byte[data.Length];
        var key = 171;
        for (var i = 0; i < data.Length; i++) { plain[i] = (byte)(key ^ data[i]); key = data[i]; }
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>TCP messages carry their length first, as four big-endian bytes.</summary>
    public static byte[] Framed(string json)
    {
        var body = Encrypt(json);
        var framed = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed, (uint)body.Length);
        body.CopyTo(framed, 4);
        return framed;
    }

    public const string SysInfoRequest = """{"system":{"get_sysinfo":{}}}""";
    public static readonly byte[] Discover = Encrypt(SysInfoRequest);

    public const string EnergyRequest = """{"emeter":{"get_realtime":{}}}""";

    /// <summary>The device in a decrypted get_sysinfo reply, or null when it is anything else.</summary>
    public static KasaInfo? ParseInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("system", out var system) || !system.TryGetProperty("get_sysinfo", out var info) || info.ValueKind != JsonValueKind.Object) return null;
            string? Str(string name) => info.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var id = Str("deviceId") ?? Str("mic_mac");
            if (id is not { Length: >= 6 and <= 64 } || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-')) return null;
            var kind = Str("mic_type") ?? Str("type") ?? "";
            var light = kind.Contains("BULB", StringComparison.OrdinalIgnoreCase);
            var plug = kind.Contains("PLUG", StringComparison.OrdinalIgnoreCase) && !info.TryGetProperty("children", out _);
            var colour = light && info.TryGetProperty("is_color", out var c) && c.ValueKind == JsonValueKind.Number && c.GetInt32() == 1;
            var feature = Str("feature") ?? "";
            var alias = Str("alias") is { Length: > 0 and <= 40 } a && a.All(ch => !char.IsControl(ch)) ? a : "Kasa " + id[^4..];
            return new KasaInfo(id, alias, Str("model") ?? "Kasa", light, colour, plug, feature.Contains("ENE", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    public static string SetLight(Color c)
    {
        var (h, s, v) = LanUdp.ToHsv(c);
        var brightness = (int)Math.Clamp(Math.Round(v * 100), 1, 100);
        return "{\"smartlife.iot.smartbulb.lightingservice\":{\"transition_light_state\":{\"on_off\":1,\"mode\":\"normal\",\"hue\":" + (int)Math.Round(h) % 360 + ",\"saturation\":" + (int)Math.Round(s * 100) + ",\"brightness\":" + brightness + ",\"color_temp\":0,\"transition_period\":300}}}";
    }

    public const string LightOff = """{"smartlife.iot.smartbulb.lightingservice":{"transition_light_state":{"on_off":0,"transition_period":300}}}""";

    /// <summary>The watts in an energy reply (older plugs say "power", newer ones "power_mw"), or null.</summary>
    public static double? ParseWatts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("emeter", out var e) || !e.TryGetProperty("get_realtime", out var r)) return null;
            if (r.TryGetProperty("power_mw", out var mw) && mw.ValueKind == JsonValueKind.Number) return Math.Clamp(mw.GetDouble() / 1000, 0, 20000);
            if (r.TryGetProperty("power", out var w) && w.ValueKind == JsonValueKind.Number) return Math.Clamp(w.GetDouble(), 0, 20000);
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>One request and one answer over TCP.</summary>
    public static async Task<string> AskAsync(IPEndPoint to, string json, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await tcp.ConnectAsync(to, timeout.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Framed(json), timeout.Token).ConfigureAwait(false);
            var head = new byte[4];
            await stream.ReadExactlyAsync(head, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (length is 0 or > 65536) throw new IOException("The device sent a reply of an odd size.");
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, timeout.Token).ConfigureAwait(false);
            return Decrypt(body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("The device did not answer."); }
    }
}

/// <summary>Finds Kasa devices with a broadcast and keeps what it learned. Shared by the lights and the power page.</summary>
public sealed class KasaDirectory(IReadOnlyList<IPEndPoint>? testTargets = null, int? testPort = null)
{
    static readonly TimeSpan ScanQuick = TimeSpan.FromSeconds(30), ScanSlow = TimeSpan.FromMinutes(3), ScanFor = TimeSpan.FromSeconds(1.2);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Dictionary<string, KasaFound> _found = [];
    readonly System.Collections.Concurrent.ConcurrentBag<string> _dropped = [];
    DateTime _scannedAt;

    public string Status { get; private set; } = "Looking for devices…";
    public int Port => testPort ?? KasaProtocol.Port;

    public async Task<IReadOnlyList<KasaFound>> RefreshAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (_dropped.TryTake(out var gone)) _found.Remove(gone);
            var now = DateTime.UtcNow;
            if (now - _scannedAt > (_found.Count == 0 ? ScanQuick : ScanSlow))
            {
                _scannedAt = now;
                try
                {
                    foreach (var (from, data) in await LanUdp.AskAsync(KasaProtocol.Discover, KasaProtocol.Port, ScanFor, testTargets, ct).ConfigureAwait(false))
                        if (KasaProtocol.ParseInfo(KasaProtocol.Decrypt(data)) is { } info) _found[info.Id] = new KasaFound(from.Address, info);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Could not scan the network: " + ex.Message; return _found.Values.ToList(); }
            }
            Status = _found.Count == 0 ? "None found yet. Kasa devices must be on the same Wi-Fi as this PC; newer firmware that needs a TP-Link sign-in is not supported."
                : $"{_found.Count} on the network";
            return _found.Values.ToList();
        }
        finally { _gate.Release(); }
    }

    /// <summary>A device stopped answering: forget it so the next scan looks for it again.</summary>
    public void ReportFailure(string id)
    {
        _dropped.Add(id);
        _scannedAt = default;
    }
}

/// <summary>A Kasa colour bulb as one light.</summary>
public sealed class KasaLightSink(KasaFound found, Action<string> failed, int port) : IDeviceSink
{
    bool? _on;
    Color _last;

    public KasaFound Found => found;
    public string Id => "kasa:" + found.Info.Id;
    public string Name => found.Info.Alias;
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromSeconds(1);
    public bool Keepalive => false;
    public bool Realtime => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var c = Compositor.Average(leds);
        var (r, g, b) = c.ToBytes();
        try
        {
            if (r == 0 && g == 0 && b == 0)
            {
                if (_on == false) return;
                await KasaProtocol.AskAsync(new IPEndPoint(found.Ip, port), KasaProtocol.LightOff, ct).ConfigureAwait(false);
                _on = false;
                return;
            }
            if (_on == true && c == _last) return;
            await KasaProtocol.AskAsync(new IPEndPoint(found.Ip, port), KasaProtocol.SetLight(c), ct).ConfigureAwait(false);
            (_on, _last) = (true, c);
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException) { failed(found.Info.Id); throw; }
    }
}

/// <summary>Kasa colour bulbs on the network. Plugs and switches are never lights and never controlled.</summary>
public sealed class KasaSource(KasaDirectory directory) : IDeviceSource
{
    readonly Dictionary<string, KasaLightSink> _sinks = [];

    public string Name => "Kasa";
    public string Status => directory.Status;

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var lights = (await directory.RefreshAsync(ct).ConfigureAwait(false)).Where(f => f.Info.IsColour).ToList();
        var ids = lights.Select(f => f.Info.Id).ToHashSet();
        foreach (var gone in _sinks.Keys.Where(k => !ids.Contains(k)).ToList()) _sinks.Remove(gone);
        foreach (var f in lights)
            if (!_sinks.TryGetValue(f.Info.Id, out var s) || s.Found != f) _sinks[f.Info.Id] = new KasaLightSink(f, directory.ReportFailure, directory.Port);
        return _sinks.Values.ToList();
    }

    public void ReportFailure(string sinkId) { /* the next scan finds the bulb again */ }
}

/// <summary>Kasa plugs for the power page: read only. Only plugs with energy monitoring (such as the KP115 and HS110) report watts.</summary>
public sealed class KasaPlugMonitor(KasaDirectory directory, Func<PlugLinks> links) : IPlugProvider
{
    readonly Dictionary<string, PlugReading> _readings = [];
    int _busy;

    public string Name => "Kasa";
    public IReadOnlyList<PlugInfo> Plugs { get; private set; } = [];
    public string Status => directory.Status;

    public PlugReading? ReadingOf(string id) { lock (_readings) return _readings.GetValueOrDefault(id); }

    public async Task PollAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var plugs = (await directory.RefreshAsync(ct).ConfigureAwait(false)).Where(f => f.Info.IsPlug).ToList();
            Plugs = plugs.Select(f => new PlugInfo("kasa:" + f.Info.Id, f.Info.Alias, f.Info.Model, "Kasa")).ToList();
            foreach (var f in plugs.Where(f => links().IsLinked("kasa:" + f.Info.Id)))
            {
                PlugReading reading;
                if (!f.Info.HasPower) reading = new(null, "This plug does not report power (needs a model with energy monitoring, such as the KP115 or HS110)", DateTime.UtcNow);
                else
                {
                    try
                    {
                        var answer = await KasaProtocol.AskAsync(new IPEndPoint(f.Ip, directory.Port), KasaProtocol.EnergyRequest, ct).ConfigureAwait(false);
                        reading = KasaProtocol.ParseWatts(answer) is { } w ? new(w, "", DateTime.UtcNow) : new(null, "Did not report power", DateTime.UtcNow);
                    }
                    catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)
                    {
                        directory.ReportFailure(f.Info.Id);
                        reading = new(null, "Did not answer", DateTime.UtcNow);
                    }
                }
                lock (_readings) _readings["kasa:" + f.Info.Id] = reading;
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
