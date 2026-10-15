using Ember.Lighting.Meross;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Tapo;

public sealed record TapoFound(string Address, TapoInfo Info, TapoDevice Device);

/// <summary>
/// Finds the Tapo devices on this PC's networks and keeps what it learned. Tapo has no list of devices to ask for, so every
/// address in each local /24 is asked whether it starts a Tapo handshake, and the ones that do are signed in to with the saved
/// account. Shared by the lights and the power page, so the network is scanned once.
/// </summary>
public sealed class TapoDirectory(Func<TapoAccount?> session, HttpClient http)
{
    static readonly TimeSpan ProbeFor = TimeSpan.FromSeconds(3), ScanQuick = TimeSpan.FromSeconds(45), ScanSlow = TimeSpan.FromMinutes(5);

    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Dictionary<string, TapoFound> _found = [];     // by address
    readonly HashSet<string> _rejected = [];                // addresses that are Tapo but did not accept the account
    readonly System.Collections.Concurrent.ConcurrentBag<string> _dropped = [];
    TapoAccount? _account;
    DateTime _scannedAt;

    public string Status { get; private set; } = "Not set up. Save your Tapo account in Settings.";
    public bool HasAccount => session() is not null;

    /// <summary>The devices known now. Scans the network at most every 45 seconds while nothing is found, and every few minutes after that.</summary>
    public async Task<IReadOnlyList<TapoFound>> RefreshAsync(CancellationToken ct)
    {
        var saved = session();
        if (saved is null) { Forget(); Status = "Not set up. Save your Tapo account in Settings."; return []; }
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_account != saved) { Forget(); _account = saved; }
            while (_dropped.TryTake(out var gone)) _found.Remove(gone);
            var now = DateTime.UtcNow;
            if (now - _scannedAt > (_found.Count == 0 ? ScanQuick : ScanSlow))
            {
                _scannedAt = now;
                await ScanAsync(saved, ct).ConfigureAwait(false);
            }
            Status = _found.Count > 0 ? $"{_found.Count} on the network" + (_rejected.Count > 0 ? $", {_rejected.Count} refused this account" : "")
                : _rejected.Count > 0 ? $"Found {_rejected.Count} Tapo device(s), but none accepted this email and password."
                : "None found yet. Tapo devices must be on the same Wi-Fi as this PC, with Third-Party Compatibility on in the Tapo app.";
            return _found.Values.ToList();
        }
        finally { _gate.Release(); }
    }

    /// <summary>A device stopped answering: forget it so the next scan looks for it again.</summary>
    public void ReportFailure(string address)
    {
        _dropped.Add(address);
        _scannedAt = default;
    }

    void Forget() { _account = null; _found.Clear(); _rejected.Clear(); _scannedAt = default; }

    async Task ScanAsync(TapoAccount account, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(48);
        var known = _found.Keys.ToHashSet();
        var candidates = new List<string>();
        var tasks = MerossSource.Hosts().Where(h => !known.Contains(h)).Select(async address =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try { if (await TapoDevice.ProbeAsync(http, address, ProbeFor, ct).ConfigureAwait(false)) lock (candidates) candidates.Add(address); }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var address in candidates)
        {
            var device = new TapoDevice(http, address, session);
            try
            {
                if (await device.InfoAsync(ct).ConfigureAwait(false) is { } info) { _found[address] = new TapoFound(address, info, device); _rejected.Remove(address); }
            }
            catch (Exception ex) when (ex is TapoException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { _rejected.Add(address); }
        }
    }
}

/// <summary>A Tapo bulb or light strip: one colour, changed slowly, the same way Meross lights are.</summary>
public sealed class TapoLightSink(TapoFound found, Action<string> failed) : IDeviceSink
{
    bool? _on;

    public string Id => "tapo:" + found.Info.Id;
    public string Name => found.Info.Name;
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromSeconds(1.5);
    public bool Keepalive => false;
    public bool Realtime => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var (r, g, b) = Compositor.Average(leds).ToBytes();
        try
        {
            if (r == 0 && g == 0 && b == 0)
            {
                if (_on != false) { await found.Device.SetOffAsync(ct).ConfigureAwait(false); _on = false; }
                return;
            }
            await found.Device.SetColourAsync(r, g, b, found.Info.Colour, ct).ConfigureAwait(false);
            _on = true;
        }
        catch (Exception ex) when (ex is TapoException or HttpRequestException) { failed(found.Address); throw; }
    }
}

/// <summary>Tapo bulbs and light strips, found on the network with the saved Tapo account. Plugs, hubs and sensors are never controlled.</summary>
public sealed class TapoSource(TapoDirectory directory) : IDeviceSource
{
    readonly Dictionary<string, IDeviceSink> _sinks = [];

    public string Name => "Tapo";
    public string Status => directory.Status;

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var lights = (await directory.RefreshAsync(ct).ConfigureAwait(false)).Where(f => f.Info.IsLight).ToList();
        var ids = lights.Select(f => "tapo:" + f.Info.Id).ToHashSet();
        foreach (var gone in _sinks.Keys.Where(k => !ids.Contains(k)).ToList()) _sinks.Remove(gone);
        var result = new List<IDeviceSink>();
        foreach (var f in lights)
        {
            var id = "tapo:" + f.Info.Id;
            if (!_sinks.TryGetValue(id, out var sink)) _sinks[id] = sink = new TapoLightSink(f, directory.ReportFailure);
            result.Add(sink);
        }
        return result;
    }

    public void ReportFailure(string sinkId) => _sinks.Remove(sinkId);
}

/// <summary>Tapo plugs for the power page: read only, never switched. Plugs without energy monitoring show as not reporting power.</summary>
public sealed class TapoPlugMonitor(TapoDirectory directory, Func<PlugLinks> links) : IPlugProvider
{
    readonly Dictionary<string, PlugReading> _readings = [];
    int _busy;

    public string Name => "Tapo";
    public IReadOnlyList<PlugInfo> Plugs { get; private set; } = [];
    public string Status => directory.HasAccount ? directory.Status : "Save your Tapo account in Settings to link Tapo plugs.";

    public PlugReading? ReadingOf(string id) { lock (_readings) return _readings.GetValueOrDefault(id); }

    public async Task PollAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var plugs = (await directory.RefreshAsync(ct).ConfigureAwait(false)).Where(f => f.Info.IsPlug).ToList();
            Plugs = plugs.Select(f => new PlugInfo("tapo:" + f.Info.Id, f.Info.Name, f.Info.Model, "Tapo")).ToList();
            foreach (var f in plugs.Where(f => links().IsLinked("tapo:" + f.Info.Id)))
            {
                PlugReading reading;
                try
                {
                    reading = await f.Device.WattsAsync(ct).ConfigureAwait(false) is { } w ? new(w, "", DateTime.UtcNow)
                        : new(null, "This plug does not report power (needs a model with energy monitoring, such as the P110 or P115)", DateTime.UtcNow);
                }
                catch (Exception ex) when (ex is TapoException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    directory.ReportFailure(f.Address);
                    reading = new(null, "Did not answer", DateTime.UtcNow);
                }
                lock (_readings) _readings["tapo:" + f.Info.Id] = reading;
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
