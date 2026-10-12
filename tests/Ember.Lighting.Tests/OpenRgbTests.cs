using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ember.Lighting.OpenRgb;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class OpenRgbCodecTests
{
    sealed class W
    {
        readonly List<byte> _b = [];
        public W U32(uint v) { _b.AddRange(BitConverter.GetBytes(v)); return this; }
        public W U16(int v) { _b.AddRange(BitConverter.GetBytes((ushort)v)); return this; }
        public W Str(string s) { var t = Encoding.UTF8.GetBytes(s); U16(t.Length + 1); _b.AddRange(t); _b.Add(0); return this; }
        public W Zeros(int n) { _b.AddRange(new byte[n]); return this; }
        public byte[] ToArray() => _b.ToArray();
    }

    /// <summary>A controller body the way a protocol <paramref name="v"/> server sends it (after the size field).</summary>
    static byte[] Controller(int v, int leds = 7, string name = "Board", string location = "HID: x")
    {
        var w = new W().U32(0).Str(name);
        if (v >= 1) w.Str("MSI");
        w.Str("desc").Str("1.0").Str("serial").Str(location);
        w.U16(2).U32(0);
        foreach (var m in new[] { "Direct", "Static" })
        {
            w.Str(m).Zeros(4 * (v >= 3 ? 12 : 9));
            w.U16(1).U32(0xFF); // one colour
        }
        w.U16(2);
        w.Str("Zone A").U32(1).U32(0).U32(5).U32(5).U16(0);
        if (v >= 4) w.U16(1).Str("seg").U32(0).U32(0).U32(5);
        if (v >= 5) w.U32(0);
        w.Str("Zone B").U32(1).U32(0).U32(2).U32(2).U16(4).U32(2).U32(2).U32(1).U32(2).U32(3).U32(4);
        if (v >= 4) w.U16(0);
        if (v >= 5) w.U32(0);
        w.U16(leds);
        for (var i = 0; i < leds; i++) w.Str("LED " + i).U32(0);
        w.U16(0);
        return w.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Parses_every_protocol_version(int v)
    {
        var d = OpenRgbCodec.ParseController(3, Controller(v), v);
        Assert.Equal(3, d.Index);
        Assert.Equal("Board", d.Name);
        Assert.Equal("Motherboard", d.TypeName);
        Assert.Equal(7, d.LedCount);
        Assert.Equal(["Direct", "Static"], d.Modes);
        Assert.Equal(["Zone A", "Zone B"], d.Zones.Select(z => z.Name));
        Assert.Equal([5, 2], d.Zones.Select(z => z.LedCount));
        Assert.Equal("MSI".Length > 0 && v >= 1 ? "MSI" : "", d.Vendor);
        Assert.Equal("openrgb:Board|HID: x", d.Id);
    }

    [Fact]
    public void Truncated_or_absurd_data_is_a_protocol_error_not_a_crash()
    {
        var good = Controller(5);
        // Only the LED count is needed from the LED list, so cuts inside that list (the last part) are still understood.
        for (var cut = 0; cut < good.Length / 2; cut += 7)
            Assert.Throws<OpenRgbProtocolException>(() => OpenRgbCodec.ParseController(0, good[..cut], 5));
        var huge = new W().U32(0).Str("x").Str("v").Str("d").Str("v").Str("s").Str("l").U16(60000).ToArray();
        Assert.Throws<OpenRgbProtocolException>(() => OpenRgbCodec.ParseController(0, huge, 5));
    }

    [Fact]
    public void Packet_and_header_round_trip_and_bad_headers_are_refused()
    {
        var p = OpenRgbCodec.Packet(2, 1050, [1, 2, 3]);
        Assert.Equal(16 + 3, p.Length);
        Assert.True(OpenRgbCodec.TryParseHeader(p, out var dev, out var id, out var size));
        Assert.Equal((2u, 1050u, 3), (dev, id, size));
        p[0] = (byte)'X';
        Assert.False(OpenRgbCodec.TryParseHeader(p, out _, out _, out _));
        var big = OpenRgbCodec.Packet(0, 0, []);
        BinaryPrimitives.WriteUInt32LittleEndian(big.AsSpan(12), uint.MaxValue);
        Assert.False(OpenRgbCodec.TryParseHeader(big, out _, out _, out _));
        Assert.False(OpenRgbCodec.TryParseHeader(new byte[5], out _, out _, out _));
    }

    [Fact]
    public void Led_colours_are_size_count_then_rgb_and_a_zero()
    {
        var b = OpenRgbCodec.LedColors([new Color(1, 0, 0), new Color(0, 0.5, 1)]);
        Assert.Equal(4 + 2 + 8, b.Length);
        Assert.Equal(14u, BinaryPrimitives.ReadUInt32LittleEndian(b));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4)));
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 128, 255, 0 }, b[6..]);
    }
}

public class OpenRgbClientTests
{
    /// <summary>A tiny OpenRGB server: protocol 5, one device, records the LED packets it receives.</summary>
    sealed class FakeServer : IAsyncDisposable
    {
        readonly TcpListener _l = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _cts = new();
        public List<(uint Id, uint Dev, byte[] Body)> Received { get; } = [];
        public int Port => ((IPEndPoint)_l.LocalEndpoint).Port;
        public bool SendNoiseFirst;
        readonly Task _run;

        public FakeServer()
        {
            _l.Start();
            _run = Task.Run(async () =>
            {
                using var c = await _l.AcceptTcpClientAsync(_cts.Token);
                var s = c.GetStream();
                var h = new byte[16];
                try
                {
                    while (true)
                    {
                        await s.ReadExactlyAsync(h, _cts.Token);
                        OpenRgbCodec.TryParseHeader(h, out var dev, out var id, out var size);
                        var body = new byte[size];
                        await s.ReadExactlyAsync(body, _cts.Token);
                        lock (Received) Received.Add((id, dev, body));
                        if (SendNoiseFirst && id == OpenRgbCodec.RequestControllerCount)
                            await s.WriteAsync(OpenRgbCodec.Packet(0, OpenRgbCodec.DeviceListUpdated, []), _cts.Token);
                        switch (id)
                        {
                            case OpenRgbCodec.RequestProtocolVersion: await s.WriteAsync(OpenRgbCodec.Packet(0, id, OpenRgbCodec.UInt32(3)), _cts.Token); break; // older server
                            case OpenRgbCodec.RequestControllerCount: await s.WriteAsync(OpenRgbCodec.Packet(0, id, OpenRgbCodec.UInt32(1)), _cts.Token); break;
                            case OpenRgbCodec.RequestControllerData:
                                var data = Body();
                                var payload = new byte[4 + data.Length];
                                BitConverter.GetBytes((uint)payload.Length).CopyTo(payload, 0);
                                data.CopyTo(payload, 4);
                                await s.WriteAsync(OpenRgbCodec.Packet(dev, id, payload), _cts.Token);
                                break;
                        }
                    }
                }
                catch (Exception) { /* closed */ }
            });
        }

        static byte[] Body()
        {
            var w = new List<byte>();
            void U32(uint v) => w.AddRange(BitConverter.GetBytes(v));
            void U16(int v) => w.AddRange(BitConverter.GetBytes((ushort)v));
            void Str(string s) { var t = Encoding.UTF8.GetBytes(s); U16(t.Length + 1); w.AddRange(t); w.Add(0); }
            U32(4); Str("Strip"); Str("Vendor"); Str("d"); Str("v"); Str("s"); Str("loc");
            U16(1); U32(0); Str("Direct"); w.AddRange(new byte[4 * 12]); U16(0); // protocol 3 layout: 12 fields
            U16(0);   // no zones
            U16(3);   // 3 leds
            for (var i = 0; i < 3; i++) { Str("L"); U32(0); }
            U16(0);
            return w.ToArray();
        }

        public async ValueTask DisposeAsync() { _cts.Cancel(); _l.Stop(); try { await _run; } catch (Exception) { } }
    }

    [Fact]
    public async Task Connects_negotiates_the_older_version_lists_devices_and_sends_colours()
    {
        await using var server = new FakeServer { SendNoiseFirst = true };
        await using var client = new OpenRgbClient();
        await client.ConnectAsync(port: server.Port);
        Assert.Equal(3, client.ProtocolVersion);

        var devices = await client.GetDevicesAsync(); // a device-list notification arrives in the middle and is skipped
        var d = Assert.Single(devices);
        Assert.Equal("Strip", d.Name);
        Assert.Equal(3, d.LedCount);

        await client.SetCustomModeAsync(0);
        await client.SetLedsAsync(0, [new Color(1, 0, 0), new Color(0, 1, 0), new Color(0, 0, 1)]);
        await Task.Delay(300);
        lock (server.Received)
        {
            Assert.Contains(server.Received, r => r.Id == OpenRgbCodec.SetClientName && Encoding.UTF8.GetString(r.Body).TrimEnd('\0') == "Ember");
            Assert.Contains(server.Received, r => r.Id == OpenRgbCodec.SetCustomMode);
            var leds = server.Received.Single(r => r.Id == OpenRgbCodec.UpdateLeds);
            Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255, 0, 0, 0, 0, 255, 0 }, leds.Body[6..]);
        }
    }

    [Fact]
    public async Task Only_this_pc_is_allowed()
    {
        await using var client = new OpenRgbClient();
        await Assert.ThrowsAsync<ArgumentException>(() => client.ConnectAsync(IPAddress.Parse("192.168.1.5")));
    }

    [Fact]
    public async Task Nothing_listening_fails_with_a_socket_error()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        await using var client = new OpenRgbClient();
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(port: port));
    }
}
