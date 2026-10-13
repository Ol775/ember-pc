using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Logitech;

/// <summary>
/// Logitech G keyboards and mice through G HUB's LED SDK. The SDK keeps one global "current device type", so every call
/// goes through this one object, one at a time.
/// </summary>
public sealed class LogitechContext(ILogitechLed api)
{
    readonly object _gate = new();
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
/// <summary>The Logitech mice (and other non-per-key RGB devices) as one lamp that takes the average colour.</summary>
public sealed class LogitechMouseSink(LogitechContext context) : IDeviceSink
{
    public string Id => "logitech:mouse";
    public string Name => "Logitech mouse";
    public string Kind => "Mouse";
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

public sealed class LogitechSource(Func<ILogitechLed?> open) : IDeviceSource, IAsyncDisposable
{
    ILogitechLed? _api;
    LogitechContext? _context;
    IDeviceSink[] _sinks = [];

    public string Name => "Logitech";
    public string Status { get; private set; } = "Looking for G HUB…";

    public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        try
        {
            if (_context is { Ready: true }) return Task.FromResult<IReadOnlyList<IDeviceSink>>(_sinks);
            if (_api is null)
            {
                _api = open();
                if (_api is null) { Status = "Logitech G HUB is not installed (Ember uses its lighting engine)"; return Task.FromResult<IReadOnlyList<IDeviceSink>>([]); }
                _context = new LogitechContext(_api);
                _sinks = [new LogitechKeyboardSink(_context), new LogitechMouseSink(_context)];
            }
            if (_context!.Start())
            {
                Status = "Connected through G HUB (keyboard and mouse). If a device does not change, switch on 'Allow games and applications to control lighting' for it in G HUB.";
                return Task.FromResult<IReadOnlyList<IDeviceSink>>(_sinks);
            }
            Status = "G HUB is installed but not answering. Start Logitech G HUB.";
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
