namespace Ember.Lighting;

/// <summary>Somewhere colours can be sent: a device from OpenRGB, a Govee lamp.</summary>
public interface IDeviceSink
{
    string Id { get; }
    string Name { get; }
    string Kind { get; }
    int LedCount { get; }
    /// <summary>Above 1 when the LEDs form a grid (a keyboard): this many to a row. 0 for a line of LEDs.</summary>
    int Columns => 0;
    /// <summary>The least time between two sends to this device.</summary>
    TimeSpan MinInterval { get; }
    /// <summary>Resend an unchanged picture now and then (devices that fall back to their own effect when left alone).</summary>
    bool Keepalive { get; }
    /// <summary>False for devices that can only follow slow changes (the Govee cloud), which never get real-time frames.</summary>
    bool Realtime => true;
    Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct);
}

/// <summary>
/// Sends the composited picture to the devices that have a placement on the canvas: not faster than each device allows,
/// not while a send to it is still running, and not again when nothing changed. Switching the lights off sends black once.
/// </summary>
public sealed class LightingEngine(TimeProvider clock)
{
    static readonly TimeSpan KeepaliveEvery = TimeSpan.FromSeconds(2);
    const double Epsilon = 1.0 / 255 / 2;

    readonly object _gate = new();
    readonly Dictionary<string, Sent> _sent = new();
    readonly HashSet<string> _inFlight = new();
    readonly Dictionary<string, Exception> _failures = new();

    sealed record Sent(DateTimeOffset At, Color[] Leds);

    /// <summary>Sink ids whose last send failed, with the error; cleared when a send works again.</summary>
    public IReadOnlyDictionary<string, Exception> Failures
    {
        get { lock (_gate) return new Dictionary<string, Exception>(_failures); }
    }

    bool _blackoutAll;
    readonly Dictionary<string, int> _blackout = new();
    const int BlackoutFrames = 3; // a device in its own effect mode can miss a single frame

    /// <summary>The next step while off sends a few black frames to every device, lit by Ember or not.</summary>
    public void BlackoutAll() { lock (_gate) _blackoutAll = true; }

    /// <summary>Forget what was sent (after a reconnect), so the next step sends everything again.</summary>
    public void Reset()
    {
        lock (_gate) { _sent.Clear(); _failures.Clear(); }
    }

    /// <returns>The picture for the canvas preview (empty while the lights are off).</returns>
    public async Task<Dictionary<string, Color[]>> StepAsync(
        Scene scene, bool on, LightingInputs inputs, IReadOnlyList<IDeviceSink> sinks, CancellationToken ct = default,
        IReadOnlyList<Scene>? profiles = null)
    {
        var byId = sinks.ToDictionary(s => s.Id);
        var counts = sinks.ToDictionary(s => s.Id, s => s.LedCount);
        var columns = sinks.Where(s => s.Columns > 1).ToDictionary(s => s.Id, s => s.Columns);
        var frame = on ? Compositor.Render(scene, counts, inputs, profiles, columns) : [];

        if (!on)
        {
            // Everything we lit gets one black frame, then it is left alone.
            Dictionary<string, Sent> lit;
            lock (_gate) lit = _sent.Where(kv => kv.Value.Leds.Any(c => c.R > 0 || c.G > 0 || c.B > 0)).ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var id in lit.Keys)
                if (byId.TryGetValue(id, out var sink)) frame[id] = new Color[sink.LedCount];
            lock (_gate)
            {
                if (_blackoutAll) { foreach (var s in sinks) _blackout[s.Id] = BlackoutFrames; _blackoutAll = false; }
                foreach (var id in _blackout.Keys.ToList())
                    if (byId.TryGetValue(id, out var sink)) frame[id] = new Color[sink.LedCount]; else _blackout.Remove(id);
            }
        }

        var now = clock.GetUtcNow();
        var sends = new List<Task>();
        foreach (var (id, leds) in frame)
        {
            if (!byId.TryGetValue(id, out var sink)) continue;
            lock (_gate)
            {
                if (_inFlight.Contains(id)) continue;
                var forced = !on && _blackout.ContainsKey(id);
                if (_sent.TryGetValue(id, out var last))
                {
                    var age = now - last.At;
                    if (age < sink.MinInterval) continue;
                    if (!forced && Same(last.Leds, leds) && (!sink.Keepalive || age < KeepaliveEvery)) continue;
                }
                else if (!on && !forced) continue;
                if (forced && --_blackout[id] <= 0) _blackout.Remove(id);
                _inFlight.Add(id);
            }
            sends.Add(SendOne(sink, leds, now, ct));
        }
        await Task.WhenAll(sends).ConfigureAwait(false);
        return on ? frame : [];
    }

    async Task SendOne(IDeviceSink sink, Color[] leds, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            await sink.SendAsync(leds, ct).ConfigureAwait(false);
            lock (_gate) { _sent[sink.Id] = new Sent(at, leds); _failures.Remove(sink.Id); }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) { _failures[sink.Id] = ex; _sent[sink.Id] = new Sent(at, leds); } // try again after MinInterval
        }
        finally
        {
            lock (_gate) _inFlight.Remove(sink.Id);
        }
    }

    static bool Same(Color[] a, Color[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].R - b[i].R) > Epsilon || Math.Abs(a[i].G - b[i].G) > Epsilon || Math.Abs(a[i].B - b[i].B) > Epsilon) return false;
        return true;
    }
}
