using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ember.Lighting.OpenRgb;

/// <summary>A place devices come from. <see cref="RefreshAsync"/> is called now and then and returns what is there at the moment.</summary>
public interface IDeviceSource
{
    string Name { get; }
    /// <summary>One plain-language line for the page ("Connected, 2 devices", "OpenRGB is not running").</summary>
    string Status { get; }
    Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct);
    /// <summary>A sink failed: drop the connection so the next refresh starts clean.</summary>
    void ReportFailure(string sinkId);
}

public sealed class OpenRgbSink(OpenRgbClient client, OpenRgbDevice device) : IDeviceSink
{
    bool _custom;

    public string Id => device.Id;
    public string Name => device.Name;
    public string Kind => device.TypeName;
    public int LedCount => device.LedCount;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(33);
    public bool Keepalive => true;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        if (!_custom)
        {
            await client.SetCustomModeAsync(device.Index, ct).ConfigureAwait(false);
            _custom = true;
        }
        await client.SetLedsAsync(device.Index, leds, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Talks to OpenRGB on this PC. When a path to OpenRGB.exe is configured and nothing is listening, it starts that program
/// with its server bound to localhost, and ends it again only if Ember was the one that started it.
/// </summary>
public sealed class OpenRgbSource(Func<string?> openRgbPath, Func<bool> startIfNeeded) : IDeviceSource, IAsyncDisposable
{
    OpenRgbClient? _client;
    Process? _started;
    DateTime _lastStart = DateTime.MinValue;

    public string Name => "OpenRGB";
    public string Status { get; private set; } = "Not connected yet";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        try
        {
            if (_client is null || !_client.Connected)
            {
                await DropAsync().ConfigureAwait(false);
                if (!await ListeningAsync(ct).ConfigureAwait(false)) TryStart();
                if (!await ListeningAsync(ct).ConfigureAwait(false))
                {
                    Status = openRgbPath() is null
                        ? "OpenRGB is not running. Install it and start its SDK server, or choose OpenRGB.exe in Settings."
                        : "Waiting for OpenRGB to start…";
                    return [];
                }
                var client = new OpenRgbClient();
                try { await client.ConnectAsync(ct: ct).ConfigureAwait(false); }
                catch { await client.DisposeAsync().ConfigureAwait(false); throw; }
                _client = client;
            }
            var devices = await _client.GetDevicesAsync(ct).ConfigureAwait(false);
            Status = devices.Count == 0 ? "Connected, but OpenRGB found no devices" : $"Connected, {devices.Count} device{(devices.Count == 1 ? "" : "s")}";
            return devices.Where(IsLight).Select(d => (IDeviceSink)new OpenRgbSink(_client, d)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Could not talk to OpenRGB: " + ex.Message;
            await DropAsync().ConfigureAwait(false);
            return [];
        }
    }

    /// <summary>OpenRGB also lists handheld and macro-pad hardware whose "LEDs" are keys or screens, not lighting: those are left out.</summary>
    public static bool IsLight(OpenRgbDevice d) =>
        d.LedCount > 0 && !NotLights.Any(n => d.Name.Contains(n, StringComparison.OrdinalIgnoreCase));

    static readonly string[] NotLights = ["Stream Deck", "Steam Deck"];

    public void ReportFailure(string sinkId)
    {
        var c = Interlocked.Exchange(ref _client, null);
        c?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
    }

    async Task DropAsync()
    {
        var c = Interlocked.Exchange(ref _client, null);
        if (c is not null) await c.DisposeAsync().ConfigureAwait(false);
    }

    static async Task<bool> ListeningAsync(CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try { await tcp.ConnectAsync(IPAddress.Loopback, OpenRgbClient.DefaultPort, timeout.Token).ConfigureAwait(false); return true; }
        catch (Exception) { return false; }
    }

    void TryStart()
    {
        var path = openRgbPath();
        if (path is null || !startIfNeeded() || !File.Exists(path) || DateTime.UtcNow - _lastStart < TimeSpan.FromSeconds(30)) return;
        _lastStart = DateTime.UtcNow;
        try
        {
            _started = Process.Start(new ProcessStartInfo(path, "--server --startminimized --server-host 127.0.0.1")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            });
        }
        catch (Exception ex) { Status = "Could not start OpenRGB: " + ex.Message; }
    }

    public async ValueTask DisposeAsync()
    {
        await DropAsync().ConfigureAwait(false);
        if (_started is { HasExited: false } p)
        {
            try { p.Kill(); } catch (Exception) { /* already gone */ }
        }
        _started?.Dispose();
    }
}
