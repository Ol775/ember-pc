using System.Text.Json;
using Ember.Lighting.Meross;

namespace Ember.Lighting.Shelly;

/// <summary>What a Shelly device says about itself at /shelly.</summary>
public sealed record ShellyInfo(string Id, string Name, string Model, int Generation, bool NeedsPassword);

/// <summary>
/// Shelly plugs and relays answer plain HTTP on the home network, no account and no cloud. The only addresses used are
/// /shelly (who are you), /status or /rpc/Switch.GetStatus (what do you draw). Nothing that switches a relay is ever called.
/// </summary>
public static class ShellyProtocol
{
    /// <summary>The device in a /shelly reply, or null when it is anything else.</summary>
    public static ShellyInfo? ParseInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var mac = Str("mac");
            if (mac is not { Length: >= 6 and <= 20 } || !mac.All(Uri.IsHexDigit)) return null;
            var generation = root.TryGetProperty("gen", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetInt32() : 1;
            if (generation is < 1 or > 9) return null;
            var model = Str("model") ?? Str("type") ?? "Shelly";
            if (!model.StartsWith("SH", StringComparison.OrdinalIgnoreCase) && !model.StartsWith("SN", StringComparison.OrdinalIgnoreCase) && generation == 1 && Str("type") is null) return null;
            var name = Str("name") is { Length: > 0 and <= 40 } n && n.All(c => !char.IsControl(c)) ? n : "Shelly " + mac[^4..].ToLowerInvariant();
            var password = (root.TryGetProperty("auth", out var a1) && a1.ValueKind == JsonValueKind.True) || (root.TryGetProperty("auth_en", out var a2) && a2.ValueKind == JsonValueKind.True);
            return new ShellyInfo(mac.ToLowerInvariant(), name, model.Length > 40 ? model[..40] : model, generation, password);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>The watts in a status reply (first generation: meters[0].power; later: apower), or null.</summary>
    public static double? ParseWatts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("apower", out var ap) && ap.ValueKind == JsonValueKind.Number) return Math.Clamp(ap.GetDouble(), 0, 20000);
            if (root.TryGetProperty("meters", out var meters) && meters.ValueKind == JsonValueKind.Array && meters.GetArrayLength() > 0
                && meters[0].TryGetProperty("power", out var p) && p.ValueKind == JsonValueKind.Number) return Math.Clamp(p.GetDouble(), 0, 20000);
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }

    public static string StatusPath(int generation) => generation >= 2 ? "/rpc/Switch.GetStatus?id=0" : "/status";
}

/// <summary>Shelly plugs for the power page: found by asking each address on the home network who it is, and only read.</summary>
public sealed class ShellyPlugMonitor(HttpClient http, Func<PlugLinks> links, Func<IEnumerable<string>>? hosts = null, int port = 80) : IPlugProvider
{
    static readonly TimeSpan ProbeFor = TimeSpan.FromSeconds(1.5), ScanQuick = TimeSpan.FromSeconds(45), ScanSlow = TimeSpan.FromMinutes(5);

    readonly Dictionary<string, (string Address, ShellyInfo Info)> _found = [];
    readonly Dictionary<string, PlugReading> _readings = [];
    DateTime _scannedAt;
    int _busy;
    string _status = "Looking for plugs…";

    public string Name => "Shelly";
    public IReadOnlyList<PlugInfo> Plugs { get; private set; } = [];
    public string Status => _status;

    public PlugReading? ReadingOf(string id) { lock (_readings) return _readings.GetValueOrDefault(id); }

    public async Task PollAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var now = DateTime.UtcNow;
            if (now - _scannedAt > (_found.Count == 0 ? ScanQuick : ScanSlow))
            {
                _scannedAt = now;
                await ScanAsync(ct).ConfigureAwait(false);
            }
            Plugs = _found.Values.Select(f => new PlugInfo("shelly:" + f.Info.Id, f.Info.Name, f.Info.Model, "Shelly")).ToList();
            _status = _found.Count == 0 ? "None found yet. Shelly devices must be on the same Wi-Fi as this PC, without a password on their web page." : $"{_found.Count} on the network";

            foreach (var (id, (address, info)) in _found.ToList().Where(kv => links().IsLinked("shelly:" + kv.Key)))
            {
                PlugReading reading;
                if (info.NeedsPassword) reading = new(null, "This device has a password on its web page, which Ember does not use", DateTime.UtcNow);
                else
                {
                    try
                    {
                        var json = await GetAsync(address, ShellyProtocol.StatusPath(info.Generation), ct).ConfigureAwait(false);
                        reading = json is not null && ShellyProtocol.ParseWatts(json) is { } w ? new(w, "", DateTime.UtcNow)
                            : new(null, "This device does not report power (needs a plug or relay with a power meter)", DateTime.UtcNow);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        _found.Remove(id);
                        _scannedAt = default;
                        reading = new(null, "Did not answer", DateTime.UtcNow);
                    }
                }
                lock (_readings) _readings["shelly:" + id] = reading;
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    async Task ScanAsync(CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(48);
        var known = _found.Values.Select(f => f.Address).ToHashSet();
        var tasks = (hosts?.Invoke() ?? MerossSource.Hosts()).Where(h => !known.Contains(h)).Select(async address =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var json = await GetAsync(address, "/shelly", ct, ProbeFor).ConfigureAwait(false);
                if (json is not null && ShellyProtocol.ParseInfo(json) is { } info) lock (_found) _found[info.Id] = (address, info);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { /* nothing there */ }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>A GET of a small reply, or null when the device answers with an error or too much.</summary>
    async Task<string?> GetAsync(string address, string path, CancellationToken ct, TimeSpan? wait = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(wait ?? TimeSpan.FromSeconds(3));
        using var response = await http.GetAsync($"http://{address}:{port}{path}", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 65536) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        return bytes.Length > 65536 ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }
}
