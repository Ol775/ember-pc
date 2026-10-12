using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Ember.Lighting.OpenRgb;

/// <summary>
/// A client of the OpenRGB SDK server (default 127.0.0.1:6742). One request at a time; notifications the server sends
/// in between are skipped, so the caller asks for the device list again when it wants to see changes.
/// </summary>
public sealed class OpenRgbClient : IAsyncDisposable
{
    public const int DefaultPort = 6742;
    static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

    readonly TcpClient _tcp = new() { NoDelay = true };
    readonly SemaphoreSlim _gate = new(1, 1);
    NetworkStream? _stream;
    int _version;

    public bool Connected => _stream is not null && _tcp.Connected;
    public int ProtocolVersion => _version;

    /// <summary>Connects and agrees on a protocol version. Only the local machine is accepted.</summary>
    public async Task ConnectAsync(IPAddress? address = null, int port = DefaultPort, string clientName = "Ember", CancellationToken ct = default)
    {
        address ??= IPAddress.Loopback;
        if (!IPAddress.IsLoopback(address)) throw new ArgumentException("OpenRGB is only used on this PC.", nameof(address));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(IoTimeout);
        await _tcp.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
        _stream = _tcp.GetStream();

        var reply = await RequestAsync(0, OpenRgbCodec.RequestProtocolVersion, OpenRgbCodec.UInt32(OpenRgbCodec.MaxClientVersion), expectReply: true, ct).ConfigureAwait(false);
        var serverVersion = reply.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(reply) : 0;
        _version = (int)Math.Min(serverVersion, OpenRgbCodec.MaxClientVersion);
        await RequestAsync(0, OpenRgbCodec.SetClientName, OpenRgbCodec.Name(clientName), expectReply: false, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OpenRgbDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        var countReply = await RequestAsync(0, OpenRgbCodec.RequestControllerCount, [], true, ct).ConfigureAwait(false);
        if (countReply.Length < 4) throw new OpenRgbProtocolException("The server did not say how many devices it has.");
        var count = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(countReply), 512);
        var devices = new List<OpenRgbDevice>(count);
        for (var i = 0; i < count; i++)
        {
            var data = await RequestAsync((uint)i, OpenRgbCodec.RequestControllerData, OpenRgbCodec.UInt32((uint)_version), true, ct).ConfigureAwait(false);
            if (data.Length < 4) throw new OpenRgbProtocolException("The server sent an empty device description.");
            devices.Add(OpenRgbCodec.ParseController(i, data[4..], _version));
        }
        return devices;
    }

    /// <summary>Puts a device into its per-LED mode so colours sent afterwards show.</summary>
    public Task SetCustomModeAsync(int device, CancellationToken ct = default) =>
        RequestAsync((uint)device, OpenRgbCodec.SetCustomMode, [], false, ct);

    public Task SetLedsAsync(int device, IReadOnlyList<Color> leds, CancellationToken ct = default) =>
        RequestAsync((uint)device, OpenRgbCodec.UpdateLeds, OpenRgbCodec.LedColors(leds), false, ct);

    async Task<byte[]> RequestAsync(uint device, uint id, byte[] payload, bool expectReply, CancellationToken ct)
    {
        var stream = _stream ?? throw new InvalidOperationException("Not connected.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(IoTimeout);
            await stream.WriteAsync(OpenRgbCodec.Packet(device, id, payload), timeout.Token).ConfigureAwait(false);
            if (!expectReply) return [];
            var header = new byte[OpenRgbCodec.HeaderSize];
            while (true)
            {
                await stream.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
                if (!OpenRgbCodec.TryParseHeader(header, out _, out var replyId, out var size))
                    throw new OpenRgbProtocolException("The server sent something that is not an OpenRGB packet.");
                var body = new byte[size];
                await stream.ReadExactlyAsync(body, timeout.Token).ConfigureAwait(false);
                if (replyId == id) return body; // anything else (a device list notification) is skipped
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _tcp.Dispose();
        _gate.Dispose();
        await Task.CompletedTask;
    }
}
