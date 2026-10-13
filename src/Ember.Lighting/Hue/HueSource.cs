using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Hue;

/// <summary>One Hue light, shown as a single colour: the average of the picture under it.</summary>
public sealed class HueSink(HueClient client, HueLight light, Func<int> lightCount) : IDeviceSink
{
    bool? _on;

    public string Id => "hue:" + light.Id;
    public string Name => light.Name;
    public string Kind => "Light";
    public int LedCount => 1;
    /// <summary>The bridge takes about ten light commands a second in all, so every light waits for its share.</summary>
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(Math.Max(150, 100 * Math.Max(1, lightCount())));
    public bool Keepalive => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var (x, y, brightness) = HueColour.FromColor(Compositor.Average(leds));
        if (brightness <= 0.5)
        {
            if (_on == false) return;
            await client.SetAsync(light.Id, false, x, y, 0, ct).ConfigureAwait(false);
            _on = false;
            return;
        }
        await client.SetAsync(light.Id, true, x, y, brightness, ct).ConfigureAwait(false);
        _on = true;
    }
}

/// <summary>Philips Hue lights through the bridge. Does nothing until a bridge has been linked in Settings.</summary>
public sealed class HueSource(Func<HueClient?> client) : IDeviceSource
{
    readonly Dictionary<string, IDeviceSink> _sinks = new();
    HueClient? _client;
    int _count;

    public string Name => "Hue";
    public string Status { get; private set; } = "Not set up. Link your Hue bridge in Settings.";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var c = client();
        if (c is null) { _sinks.Clear(); _client = null; Status = "Not set up. Link your Hue bridge in Settings."; return []; }
        try
        {
            var lights = (await c.GetLightsAsync(ct).ConfigureAwait(false)).Where(l => l.CanColour).ToList();
            if (!ReferenceEquals(c, _client)) { _sinks.Clear(); _client = c; }
            _count = lights.Count;
            foreach (var gone in _sinks.Keys.Where(k => lights.All(l => "hue:" + l.Id != k)).ToList()) _sinks.Remove(gone);
            foreach (var l in lights)
                if (!_sinks.ContainsKey("hue:" + l.Id)) _sinks["hue:" + l.Id] = new HueSink(c, l, () => _count);
            Status = lights.Count == 0
                ? $"Connected to the bridge at {c.Host}, but it has no colour lights."
                : $"Connected to the bridge at {c.Host}: {lights.Count} colour light{(lights.Count == 1 ? "" : "s")}.";
            return lights.Select(l => _sinks["hue:" + l.Id]).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Hue bridge: " + (ex is HttpRequestException or TaskCanceledException ? $"could not reach {c.Host}" : ex.Message);
            return [];
        }
    }

    public void ReportFailure(string sinkId) { /* the next refresh asks the bridge again */ }
}
