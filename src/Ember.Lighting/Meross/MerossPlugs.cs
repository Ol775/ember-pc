using System.Text.Json;

namespace Ember.Lighting.Meross;

public sealed record PlugInfo(string Uuid, string Name, string Type, string Source = "Meross", bool Online = true);

/// <summary>A brand of smart plug the power page can read from. Read only by design: there is no way to switch a plug through it.</summary>
public interface IPlugProvider
{
    string Name { get; }
    IReadOnlyList<PlugInfo> Plugs { get; }
    string Status { get; }
    PlugReading? ReadingOf(string id);
    Task PollAsync(CancellationToken ct);
}

/// <summary>Which smart plugs the owner linked for power readings, and which one the PC itself is plugged into.</summary>
public sealed record PlugLinks(IReadOnlyList<string> Linked, string? PcPlug)
{
    public static PlugLinks Empty { get; } = new([], null);
    public bool IsLinked(string uuid) => Linked.Contains(uuid);
}

/// <summary>Remembers <see cref="PlugLinks"/> in a small file. Best effort: a missing or broken file is no links.</summary>
public sealed class PlugLinkStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "power-plugs.json");

    public PlugLinks Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 65_536) return PlugLinks.Empty;
            var l = JsonSerializer.Deserialize<PlugLinks>(File.ReadAllText(path));
            if (l?.Linked is null || l.Linked.Count > 32 || l.Linked.Any(u => string.IsNullOrWhiteSpace(u) || u.Length > 64)) return PlugLinks.Empty;
            return l.PcPlug is not null && !l.Linked.Contains(l.PcPlug) ? l with { PcPlug = null } : l;
        }
        catch (Exception) { return PlugLinks.Empty; }
    }

    public bool TrySave(PlugLinks links)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(links));
            return true;
        }
        catch (Exception) { return false; }
    }
}

public sealed record PlugReading(double? Watts, string Note, DateTime At);

/// <summary>
/// Reads how much power the linked Meross plugs draw. Strictly read only: the only things ever sent to a plug are "who are you"
/// and "what are you drawing". It never switches a plug. Uses the same Meross sign-in as the lights.
/// </summary>
public sealed class PlugMonitor(Func<MerossAccount?> session, MerossCloudClient cloud, MerossLocalClient local, Func<PlugLinks> links) : IPlugProvider
{
    public string Name => "Meross";

    static readonly TimeSpan ListFor = TimeSpan.FromMinutes(10), ScanEvery = TimeSpan.FromSeconds(45);

    readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _addresses = [];
    readonly Dictionary<string, PlugReading> _readings = [];
    volatile bool _scanning;
    MerossAccount? _account;
    DateTime _listedAt, _scannedAt;
    int _busy;

    public IReadOnlyList<PlugInfo> Plugs { get; private set; } = [];
    public string Status { get; private set; } = "Sign in to your Meross account in Settings to link smart plugs.";

    /// <summary>The latest reading for a plug, or null when it has not been read yet.</summary>
    public PlugReading? ReadingOf(string uuid) { lock (_readings) return _readings.GetValueOrDefault(uuid); }

    public async Task PollAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var saved = session();
            if (saved is null) { Plugs = []; _account = null; _addresses.Clear(); Status = "Sign in to your Meross account in Settings to link smart plugs."; return; }
            var now = DateTime.UtcNow;
            if (_account != saved || now - _listedAt > ListFor)
            {
                try
                {
                    var devices = await cloud.GetDevicesAsync(saved, ct).ConfigureAwait(false);
                    Plugs = devices.Where(d => d.Type.StartsWith("mss", StringComparison.OrdinalIgnoreCase)).Select(d => new PlugInfo(d.Uuid, d.Name, d.Type, "Meross", d.Online)).ToList();
                    _account = saved;
                    _listedAt = now;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Status = "Meross: " + (ex is HttpRequestException ? "could not reach the Meross account service" : ex.Message);
                    return;
                }
            }

            var wanted = Plugs.Where(p => links().IsLinked(p.Uuid)).ToList();
            if (wanted.Count == 0) { Status = Plugs.Count == 0 ? "Your Meross account has no smart plugs." : "No plug linked yet."; return; }

            // Looking for a plug that is not answering takes many seconds, so it runs on its own and never holds up the readings.
            var lost = wanted.Where(p => !_addresses.ContainsKey(p.Uuid)).Select(p => p.Uuid).ToHashSet();
            if (lost.Count > 0 && !_scanning && now - _scannedAt > ScanEvery)
            {
                _scannedAt = now;
                _scanning = true;
                var key = saved.Key;
                _ = Task.Run(async () =>
                {
                    try { foreach (var (uuid, address) in await MerossSource.ScanAsync(local, key, lost, ct).ConfigureAwait(false)) _addresses[uuid] = address; }
                    catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException) { }
                    finally { _scanning = false; }
                }, ct);
            }

            foreach (var p in wanted)
            {
                PlugReading reading;
                if (!_addresses.TryGetValue(p.Uuid, out var address))
                    reading = new(null, !p.Online ? "Meross says this plug is offline: it is unplugged or off Wi-Fi" : _scanning ? "Looking for it on this network…" : "Not answering on this network. Is it on the same Wi-Fi as this PC?", DateTime.UtcNow);
                else
                {
                    try
                    {
                        var payload = await local.GetElectricityAsync(address, saved.Key, ct).ConfigureAwait(false);
                        reading = MerossLocalClient.WattsOf(payload) is { } w
                            ? new(w, "", DateTime.UtcNow)
                            : new(null, "This plug does not report power (needs a model with energy monitoring, such as the MSS310)", DateTime.UtcNow);
                    }
                    catch (Exception ex) when (ex is MerossException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                    {
                        _addresses.TryRemove(p.Uuid, out _); // it may have a new address: look for it again
                        reading = new(null, "Did not answer", DateTime.UtcNow);
                    }
                }
                lock (_readings) _readings[p.Uuid] = reading;
            }
            Status = $"{wanted.Count(p => ReadingOf(p.Uuid)?.Watts is not null)} of {wanted.Count} linked plugs reading";
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
