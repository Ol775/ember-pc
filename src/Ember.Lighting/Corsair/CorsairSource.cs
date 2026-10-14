using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Corsair;

/// <summary>A Corsair device lit through iCUE: one LED per iCUE LED id.</summary>
public sealed class CorsairSink(ICorsairApi api, CorsairDevice device) : IDeviceSink
{
    public string Id => "corsair:" + device.Id;
    public string Name => "Corsair " + device.Model;
    public string Kind => TypeName(device.Type);
    public int LedCount => device.LedIds.Length;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(33);
    public bool Keepalive => false;

    public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var rgb = new byte[device.LedIds.Length * 3];
        for (var i = 0; i < device.LedIds.Length; i++)
        {
            var (r, g, b) = (i < leds.Count ? leds[i] : Color.Black).ToBytes();
            rgb[i * 3] = r; rgb[i * 3 + 1] = g; rgb[i * 3 + 2] = b;
        }
        if (!api.SetColors(device.Id, device.LedIds, rgb)) throw new IOException("iCUE did not accept the colours");
        return Task.CompletedTask;
    }

    static string TypeName(int type) => type switch
    {
        0x0001 => "Keyboard", 0x0002 => "Mouse", 0x0004 => "Mousepad", 0x0008 => "Headset", 0x0010 => "Headset stand",
        0x0020 or 0x0040 => "LED controller", 0x0080 => "Memory", 0x0100 => "Cooler", 0x0200 => "Motherboard",
        0x0400 => "Graphics card", 0x0800 => "Touchbar", 0x1000 => "Game controller", _ => "Device",
    };
}

/// <summary>
/// Corsair keyboards, mice, headsets, coolers, memory and LED controllers through the iCUE program and its SDK. iCUE has to be
/// running with "Enable SDK" switched on in its settings; Ember lights the devices alongside iCUE and gives them back on exit.
/// </summary>
public sealed class CorsairSource(Func<ICorsairApi?> open) : IDeviceSource, IAsyncDisposable
{
    ICorsairApi? _api;

    public string Name => "Corsair";
    public string Status { get; private set; } = "Looking for iCUE…";

    public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        try
        {
            if (_api is null)
            {
                _api = open();
                if (_api is null) { Status = "The iCUE SDK file is missing from this Ember install. Run the Ember installer again."; return Task.FromResult<IReadOnlyList<IDeviceSink>>([]); }
            }
            switch (_api.Connect())
            {
                case CorsairState.Connected:
                    var devices = _api.Devices();
                    Status = devices.Count == 0 ? "Connected to iCUE, but it lists no devices (" + _api.Detail + ")" : $"Connected through iCUE, {devices.Count} device{(devices.Count == 1 ? "" : "s")}";
                    return Task.FromResult<IReadOnlyList<IDeviceSink>>(devices.Select(d => (IDeviceSink)new CorsairSink(_api, d)).ToList());
                case CorsairState.Connecting:
                    Status = "Connecting to iCUE… " + _api.Detail;
                    break;
                default:
                    Status = "Start Corsair iCUE and switch on Enable SDK in its settings (Settings, Software and Games). " + _api.Detail;
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Corsair iCUE: " + ex.Message; }
        return Task.FromResult<IReadOnlyList<IDeviceSink>>([]);
    }

    public void ReportFailure(string sinkId) => _api?.Disconnect();

    public ValueTask DisposeAsync()
    {
        try { _api?.Dispose(); } catch (Exception) { /* closing anyway */ }
        return ValueTask.CompletedTask;
    }
}
