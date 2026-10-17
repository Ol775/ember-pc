using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Logitech;

/// <summary>
/// Logitech G keyboards and mice through G HUB's LED SDK. The SDK keeps one global "current device type", so every call
/// goes through this one object, one at a time.
/// </summary>
public sealed class LogitechContext(ILogitechLed api, TimeProvider? clock = null)
{
    static readonly TimeSpan GKeysRefresh = TimeSpan.FromSeconds(2);
    readonly object _gate = new();
    readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    (int R, int G, int B)[]? _lastGKeys;
    long _lastGKeysAt;
    bool _ready;

    public bool Start()
    {
        lock (_gate)
        {
            if (!_ready) _ready = api.Init();
            return _ready;
        }
    }

    public bool Ready { get { lock (_gate) return _ready; } }

    public void Keyboard(byte[] bgra)
    {
        lock (_gate)
        {
            if (!_ready) throw new InvalidOperationException("G HUB is not ready");
            if (!api.SetTargetDevice(LogitechTarget.PerKeyRgb) || !api.SetLightingFromBitmap(bgra)) { _ready = false; throw new IOException("G HUB did not accept the keyboard colours"); }
        }
    }

    /// <summary>
    /// Whether the keyboard takes the G key names at all: a keyboard without G keys refuses every one. Asked once, with the keys set to black
    /// (they are painted properly right after). If G HUB cannot be asked, the answer is yes, as before, so a G keys device is never hidden by a failed probe.
    /// </summary>
    public bool HasGKeys()
    {
        lock (_gate)
        {
            if (!_ready || !api.SetTargetDevice(LogitechTarget.PerKeyRgb)) return true;
            var accepted = false;
            foreach (var name in LogitechGKeysSink.KeyNames) accepted |= api.SetLightingForKeyName(name, 0, 0, 0);
            return accepted;
        }
    }

    /// <summary>The G keys, one SDK call each. A key the keyboard does not have is refused, which is not a reason to drop the connection.</summary>
    public void GKeys(IReadOnlyList<(int R, int G, int B)> percents)
    {
        lock (_gate)
        {
            if (!_ready) throw new InvalidOperationException("G HUB is not ready");
            // The G keys keep their colour while the keyboard grid is refreshed, and nine calls every frame starve the grid:
            // send them only when they change, and now and then in case G HUB let go of them.
            var now = timeProvider.GetTimestamp();
            if (_lastGKeys is { } last && last.SequenceEqual(percents) && timeProvider.GetElapsedTime(_lastGKeysAt, now) < GKeysRefresh) return;
            if (!api.SetTargetDevice(LogitechTarget.PerKeyRgb)) { _ready = false; throw new IOException("G HUB did not accept the G key colours"); }
            for (var i = 0; i < percents.Count; i++) api.SetLightingForKeyName(LogitechGKeysSink.KeyNames[i], percents[i].R, percents[i].G, percents[i].B);
            (_lastGKeys, _lastGKeysAt) = (percents.ToArray(), now);
        }
    }

    public void Devices(int r, int g, int b)
    {
        lock (_gate)
        {
            if (!_ready) throw new InvalidOperationException("G HUB is not ready");
            if (!api.SetTargetDevice(LogitechTarget.Rgb) || !api.SetLighting(r, g, b)) { _ready = false; throw new IOException("G HUB did not accept the colours"); }
        }
    }

    /// <summary>Gives the lighting back to G HUB.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            if (_ready) api.Shutdown();
            _ready = false;
            _lastGKeys = null;
        }
    }
}

/// <summary>A per-key keyboard as a grid of 21 columns by 6 rows of keys, one LED per key position (row by row).</summary>
public sealed class LogitechKeyboardSink(LogitechContext context) : IDeviceSink
{
    public const int KeyColumns = 21, KeyRows = 6, Bytes = KeyColumns * KeyRows * 4;

    public string Id => "logitech:keyboard";
    public string Name => "Logitech keyboard";
    public string Kind => "Keyboard";
    public int LedCount => KeyColumns * KeyRows;
    public int Columns => KeyColumns;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(50);
    public bool Keepalive => true;

    public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var bitmap = new byte[Bytes];
        for (var i = 0; i < KeyColumns * KeyRows; i++)
        {
            var (r, g, b) = (i < leds.Count ? leds[i] : Color.Black).ToBytes();
            var o = i * 4;
            bitmap[o] = b; bitmap[o + 1] = g; bitmap[o + 2] = r; bitmap[o + 3] = 255;
        }
        context.Keyboard(bitmap);
        return Task.CompletedTask;
    }
}
/// <summary>
/// The programmable G keys (G1 to G9: the side column and the top row, depending on the model) and the LIGHTSPEED badge as ten lights.
/// The SDK cannot say what a keyboard has, so one with fewer simply ignores the rest.
/// </summary>
public sealed class LogitechGKeysSink(LogitechContext context) : IDeviceSink
{
    /// <summary>G1 to G9, then the LIGHTSPEED badge (the SDK names it G_BADGE).</summary>
    public static readonly int[] KeyNames = [0xFFF1, 0xFFF2, 0xFFF3, 0xFFF4, 0xFFF5, 0xFFF6, 0xFFF7, 0xFFF8, 0xFFF9, 0xFFFF2];
    public static int Keys => KeyNames.Length;

    public string Id => "logitech:gkeys";
    public string Name => "Logitech G keys and badge";
    public string Kind => "Keyboard";
    public int LedCount => Keys;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(50);
    public bool Keepalive => true;

    public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var percents = new (int, int, int)[Keys];
        for (var i = 0; i < Keys; i++)
        {
            var c = i < leds.Count ? leds[i] : Color.Black;
            percents[i] = ((int)Math.Round(c.R * 100), (int)Math.Round(c.G * 100), (int)Math.Round(c.B * 100));
        }
        context.GKeys(percents);
        return Task.CompletedTask;
    }
}

/// <summary>The Logitech mice (and other non-per-key RGB devices) as one lamp that takes the average colour.</summary>
public sealed class LogitechMouseSink(LogitechContext context) : IDeviceSink
{
    public string Id => "logitech:mouse";
    public string Name => "Logitech devices (one colour)";
    public string Kind => "Keyboard and mouse";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(50);
    public bool Keepalive => true;

    public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var c = Compositor.Average(leds);
        context.Devices((int)Math.Round(c.R * 100), (int)Math.Round(c.G * 100), (int)Math.Round(c.B * 100));
        return Task.CompletedTask;
    }
}

public sealed class LogitechSource(Func<ILogitechLed?> open, Func<bool>? oneColour = null) : IDeviceSource, IAsyncDisposable
{
    ILogitechLed? _api;
    LogitechContext? _context;
    IDeviceSink[] _perKey = [], _oneColour = [];
    bool? _lastOneColour;

    public string Name => "Logitech";
    public string Status { get; private set; } = "Looking for G HUB…";

    public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        try
        {
            if (_api is null)
            {
                _api = open();
                if (_api is null) { Status = "Logitech G HUB is not installed (Ember uses its lighting engine)"; return Task.FromResult<IReadOnlyList<IDeviceSink>>([]); }
                _context = new LogitechContext(_api);
                _oneColour = [new LogitechMouseSink(_context)];
            }
            // G HUB's engine cannot light the keyboard key by key and colour the mouse at the same time: one whole-device colour
            // locks the keyboard to that colour. So the owner picks a mode, and changing it starts the engine afresh.
            var one = oneColour?.Invoke() ?? false;
            if (_lastOneColour is { } before && before != one) _context!.Stop();
            _lastOneColour = one;
            if (_context!.Start())
            {
                // The G keys device only on a keyboard that has G keys (asked once, when G HUB first answers).
                _perKey = _perKey.Length > 0 ? _perKey : _context.HasGKeys() ? [new LogitechKeyboardSink(_context), new LogitechGKeysSink(_context)] : [new LogitechKeyboardSink(_context)];
                Status = one
                    ? "Connected through G HUB, every Logitech device takes one colour."
                    : "Connected through G HUB, keyboard and G keys lit key by key (the mouse stays with G HUB).";
                return Task.FromResult<IReadOnlyList<IDeviceSink>>(one ? _oneColour : _perKey);
            }
            Status = "G HUB is installed but not answering. Start Logitech G HUB and switch on \"Allow games and applications to control lighting\" for your devices.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Logitech G HUB: " + ex.Message; }
        return Task.FromResult<IReadOnlyList<IDeviceSink>>([]);
    }

    public void ReportFailure(string sinkId) => _context?.Stop();

    public ValueTask DisposeAsync()
    {
        try { _context?.Stop(); _api?.Dispose(); } catch (Exception) { /* closing anyway */ }
        return ValueTask.CompletedTask;
    }
}
