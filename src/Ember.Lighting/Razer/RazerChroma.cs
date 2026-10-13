using System.Net;
using System.Text;
using System.Text.Json;
using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Razer;

/// <summary>One kind of Razer Chroma device and the size of its colour grid. The mouse is left out on purpose.</summary>
public sealed record RazerKind(string Key, string Name, string Kind, int Rows, int Columns)
{
    public int LedCount => Rows * Columns;

    public static readonly IReadOnlyList<RazerKind> All =
    [
        new("keyboard", "Razer keyboard", "Keyboard", 6, 22),
        new("keypad", "Razer keypad", "Keypad", 4, 5),
        new("mousepad", "Razer mousepad", "Mousepad", 1, 15),
        new("headset", "Razer headset", "Headset", 1, 5),
        new("chromalink", "Razer Chroma Link", "Light strip", 1, 5),
    ];
}

/// <summary>
/// Razer Chroma through the local REST SDK that Razer Synapse offers on this PC (http://localhost:54235). Ember registers as an
/// application, keeps the session alive with a heartbeat, and sends one custom colour grid per device. Nothing leaves the PC.
/// </summary>
public sealed class RazerChroma(HttpClient http)
{
    public const string RegisterUrl = "http://localhost:54235/razer/chromasdk";
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(5);

    readonly SemaphoreSlim _gate = new(1, 1);
    Uri? _session;
    DateTime _lastBeat;

    /// <summary>Registers with Synapse. Throws when Synapse is not running or Chroma Connect is off.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_session is null) await ConnectLockedAsync(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    async Task ConnectLockedAsync(CancellationToken ct)
    {
        const string app = """
        {"title":"Ember","description":"Fan control and lighting for your PC","author":{"name":"Ol775","contact":"https://github.com/Ol775/ember-pc"},
         "device_supported":["keyboard","mousepad","headset","keypad","chromalink"],"category":"application"}
        """;
        using var res = await http.PostAsync(RegisterUrl, new StringContent(app, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Razer Synapse answered {(int)res.StatusCode}.");
        _session = ParseSession(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        _lastBeat = DateTime.UtcNow;
    }

    /// <summary>The session address Synapse hands out. Only this PC is ever contacted, so anything else is refused.</summary>
    public static Uri ParseSession(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("uri", out var u) && Uri.TryCreate(u.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp && (uri.Host is "localhost" || IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip))
                && uri.AbsolutePath.TrimEnd('/') == "/chromasdk" && uri.Port is > 0)
                return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/chromasdk");
        }
        catch (Exception) { /* not the answer of Synapse */ }
        throw new InvalidOperationException("That did not look like Razer Synapse.");
    }

    public async Task SendAsync(RazerKind kind, IReadOnlyList<Color> leds, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null) await ConnectLockedAsync(ct).ConfigureAwait(false);
            if (DateTime.UtcNow - _lastBeat >= HeartbeatEvery)
            {
                using var beat = await http.PutAsync(new Uri(_session! + "/heartbeat"), new StringContent("", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
                if (!beat.IsSuccessStatusCode) throw new InvalidOperationException($"Razer Synapse answered {(int)beat.StatusCode}.");
                _lastBeat = DateTime.UtcNow;
            }
            using var res = await http.PutAsync(new Uri(_session! + "/" + kind.Key), new StringContent(Payload(kind, leds), Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Razer Synapse answered {(int)res.StatusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _session = null; throw; }   // connect again on the next frame
        finally { _gate.Release(); }
    }

    public void Forget() => _session = null;

    /// <summary>A Chroma colour is one number, blue then green then red.</summary>
    public static int Pack(Color c)
    {
        var (r, g, b) = c.ToBytes();
        return (b << 16) | (g << 8) | r;
    }

    /// <summary>The "custom" effect: a grid of colours, rows of columns (a single row for the devices that are one line).</summary>
    public static string Payload(RazerKind kind, IReadOnlyList<Color> leds)
    {
        int At(int i) => Pack(i < leds.Count ? leds[i] : Color.Black);
        object param = kind.Rows == 1
            ? Enumerable.Range(0, kind.Columns).Select(At).ToArray()
            : Enumerable.Range(0, kind.Rows).Select(r => Enumerable.Range(0, kind.Columns).Select(c => At(r * kind.Columns + c)).ToArray()).ToArray();
        return JsonSerializer.Serialize(new { effect = "CHROMA_CUSTOM", param });
    }
}

public sealed class RazerSink(RazerChroma chroma, RazerKind kind) : IDeviceSink
{
    public string Id => "razer:" + kind.Key;
    public string Name => kind.Name;
    public string Kind => kind.Kind;
    public int LedCount => kind.LedCount;
    public int Columns => kind.Rows > 1 ? kind.Columns : 0;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(80);
    /// <summary>The unchanged picture is sent every couple of seconds, which also keeps the session with Synapse alive.</summary>
    public bool Keepalive => true;

    public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct) => chroma.SendAsync(kind, leds, ct);
}

/// <summary>Razer Chroma devices. Does nothing until it is switched on in Settings, so Synapse is not contacted before then.</summary>
public sealed class RazerSource(Func<bool> enabled, RazerChroma chroma) : IDeviceSource
{
    const string Off = "Off. Turn on Razer Chroma in Settings (needs Razer Synapse with Chroma Connect on).";
    readonly List<IDeviceSink> _sinks = RazerKind.All.Select(k => (IDeviceSink)new RazerSink(chroma, k)).ToList();

    public string Name => "Razer";
    public string Status { get; private set; } = Off;

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        if (!enabled()) { chroma.Forget(); Status = Off; return []; }
        try
        {
            await chroma.ConnectAsync(ct).ConfigureAwait(false);
            Status = "Connected to Razer Synapse. Ember cannot tell which Razer devices are plugged in, so switch off the ones you do not have on the Devices page.";
            return _sinks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Razer: " + (ex is HttpRequestException or TaskCanceledException ? "Synapse did not answer. Start Razer Synapse and turn on Chroma Connect." : ex.Message);
            return [];
        }
    }

    public void ReportFailure(string sinkId) => chroma.Forget();
}

public sealed record RazerSettings(bool Enabled);

public sealed class RazerSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "razer.json");

    public bool Enabled
    {
        get
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length <= 1024 && JsonSerializer.Deserialize<RazerSettings>(File.ReadAllText(path))?.Enabled == true;
            }
            catch (Exception) { return false; }
        }
    }

    public bool TrySave(bool enabled)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(new RazerSettings(enabled)));
            return true;
        }
        catch (Exception) { return false; }
    }
}
