using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ember.Lighting;
using Ember.Lighting.Kasa;
using Ember.Lighting.Lifx;
using Ember.Lighting.Meross;
using Ember.Lighting.Shelly;
using Ember.Lighting.Wiz;
using Ember.Lighting.Yeelight;

namespace Ember.Lighting.Tests;

/// <summary>A device on this PC's loopback: answers datagrams with what the test gives it and remembers what it was sent.</summary>
sealed class FakeUdpDevice : IDisposable
{
    readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    readonly CancellationTokenSource _stop = new();
    public List<byte[]> Received { get; } = [];
    public int Port => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
    public IPEndPoint Endpoint => new(IPAddress.Loopback, Port);

    public FakeUdpDevice(Func<byte[], byte[]?> reply)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var r = await _udp.ReceiveAsync(_stop.Token);
                    lock (Received) Received.Add(r.Buffer);
                    if (reply(r.Buffer) is { } answer) await _udp.SendAsync(answer, r.RemoteEndPoint);
                }
            }
            catch (Exception) { /* closed */ }
        });
    }

    public string[] Texts { get { lock (Received) return Received.Select(b => Encoding.UTF8.GetString(b)).ToArray(); } }
    public void Dispose() { _stop.Cancel(); _udp.Dispose(); }
}

/// <summary>A TCP device on loopback that reads one message per connection, answers it and remembers it.</summary>
sealed class FakeTcpDevice : IDisposable
{
    readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource _stop = new();
    public List<string> Received { get; } = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeTcpDevice(Func<byte[], byte[]> answer, bool framed)
    {
        _listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var stream = client.GetStream();
                    byte[] message;
                    if (framed)
                    {
                        var head = new byte[4];
                        await stream.ReadExactlyAsync(head);
                        message = new byte[BinaryPrimitives.ReadUInt32BigEndian(head)];
                        await stream.ReadExactlyAsync(message);
                    }
                    else
                    {
                        var buffer = new byte[1024];
                        var n = await stream.ReadAsync(buffer);
                        message = buffer[..n];
                    }
                    lock (Received) Received.Add(framed ? KasaProtocol.Decrypt(message) : Encoding.ASCII.GetString(message));
                    await stream.WriteAsync(answer(message));
                }
            }
            catch (Exception) { /* closed */ }
        });
    }

    public string[] Messages { get { lock (Received) return Received.ToArray(); } }
    public void Dispose() { _stop.Cancel(); _listener.Stop(); }
}

public class WizTests
{
    static byte[] Pilot(string mac) => Encoding.UTF8.GetBytes($$$"""{"method":"getPilot","env":"pro","result":{"mac":"{{{mac}}}","rssi":-60,"state":true,"r":255,"g":0,"b":0,"dimming":80}}""");

    [Fact]
    public void Only_replies_with_a_mac_are_bulbs()
    {
        Assert.Equal("a8bb50123456", WizProtocol.ParseMac(Pilot("A8BB50123456")));
        Assert.Null(WizProtocol.ParseMac(Encoding.UTF8.GetBytes("""{"result":{"mac":"not hex!"}}""")));
        Assert.Null(WizProtocol.ParseMac(Encoding.UTF8.GetBytes("garbage")));
        Assert.Null(WizProtocol.ParseMac(Encoding.UTF8.GetBytes("""{"error":{"code":-32601}}""")));
    }

    [Fact]
    public void A_colour_is_sent_as_full_colour_and_dimming()
    {
        var text = Encoding.UTF8.GetString(WizProtocol.Colour(new Color(0.5, 0, 0)));
        Assert.Contains("\"r\":255", text);
        Assert.Contains("\"dimming\":50", text);
        Assert.Contains("\"state\":true", text);
        Assert.Contains("\"dimming\":10", Encoding.UTF8.GetString(WizProtocol.Colour(new Color(0.01, 0.01, 0.01))));   // WiZ ignores anything dimmer
    }

    [Fact]
    public async Task A_bulb_is_found_and_follows_colours_and_off()
    {
        using var bulb = new FakeUdpDevice(m => Encoding.UTF8.GetString(m).Contains("getPilot") ? Pilot("a8bb50123456") : null);
        var source = new WizSource([bulb.Endpoint], bulb.Port);
        var sinks = await source.RefreshAsync(default);
        var sink = Assert.Single(sinks);
        Assert.Equal("wiz:a8bb50123456", sink.Id);
        Assert.Equal("1 on the network", source.Status);

        await sink.SendAsync([new Color(0, 1, 0)], default);
        await sink.SendAsync([Color.Black], default);
        await Task.Delay(200);
        Assert.Contains(bulb.Texts, t => t.Contains("\"g\":255") && t.Contains("setPilot"));
        Assert.Contains(bulb.Texts, t => t.Contains("\"state\":false"));
    }

    [Fact]
    public async Task No_answer_means_no_bulbs_and_a_helpful_line()
    {
        using var silent = new FakeUdpDevice(_ => null);
        var source = new WizSource([silent.Endpoint], silent.Port);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("local communication", source.Status);
    }
}

public class LifxTests
{
    static byte[] StateService(string mac)
    {
        var p = new byte[LifxProtocol.HeaderSize + 5];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(32), LifxProtocol.StateService);
        Convert.FromHexString(mac).CopyTo(p, 8);
        p[LifxProtocol.HeaderSize] = 1;
        return p;
    }

    static byte[] StateLabel(string mac, string label)
    {
        var p = new byte[LifxProtocol.HeaderSize + 32];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(32), LifxProtocol.StateLabel);
        Convert.FromHexString(mac).CopyTo(p, 8);
        Encoding.UTF8.GetBytes(label).CopyTo(p, LifxProtocol.HeaderSize);
        return p;
    }

    [Fact]
    public void Packets_have_the_right_header()
    {
        var discover = LifxProtocol.Discover;
        Assert.Equal(36, discover.Length);
        Assert.Equal(36, BinaryPrimitives.ReadUInt16LittleEndian(discover));
        Assert.Equal(0x3400, BinaryPrimitives.ReadUInt16LittleEndian(discover.AsSpan(2)));   // protocol 1024, addressable, tagged
        Assert.Equal(LifxProtocol.GetService, BinaryPrimitives.ReadUInt16LittleEndian(discover.AsSpan(32)));

        var set = LifxProtocol.Colour("d073d5000001", new Color(1, 0, 0), 150);
        Assert.Equal(36 + 13, set.Length);
        Assert.Equal(0x1400, BinaryPrimitives.ReadUInt16LittleEndian(set.AsSpan(2)));         // not tagged: aimed at one light
        Assert.Equal(LifxProtocol.SetColor, BinaryPrimitives.ReadUInt16LittleEndian(set.AsSpan(32)));
        Assert.Equal(65535, BinaryPrimitives.ReadUInt16LittleEndian(set.AsSpan(36 + 5)));    // full brightness
        Assert.Equal(150u, BinaryPrimitives.ReadUInt32LittleEndian(set.AsSpan(36 + 9)));
        Assert.Equal(0, set[22] & 1);                                                         // no answer wanted for a colour
    }

    [Fact]
    public void Replies_are_read_defensively()
    {
        Assert.Equal("d073d5000001", LifxProtocol.ParseService(StateService("d073d5000001")));
        Assert.Null(LifxProtocol.ParseService([1, 2, 3]));
        var notUdp = StateService("d073d5000001");
        notUdp[LifxProtocol.HeaderSize] = 2;
        Assert.Null(LifxProtocol.ParseService(notUdp));
        Assert.Equal("Desk", LifxProtocol.ParseLabel(StateLabel("d073d5000001", "Desk")));
    }

    [Fact]
    public async Task A_light_is_found_with_its_label_and_follows_colours()
    {
        const string mac = "d073d5000001";
        using var light = new FakeUdpDevice(m =>
        {
            var type = BinaryPrimitives.ReadUInt16LittleEndian(m.AsSpan(32));
            return type == LifxProtocol.GetService ? StateService(mac) : type == LifxProtocol.GetLabel ? StateLabel(mac, "Desk") : null;
        });
        var source = new LifxSource([light.Endpoint], light.Port);
        var sink = Assert.Single(await source.RefreshAsync(default));
        Assert.Equal("Desk", sink.Name);
        Assert.Equal("lifx:" + mac, sink.Id);

        await sink.SendAsync([new Color(0, 0, 1)], default);
        await sink.SendAsync([Color.Black], default);
        await Task.Delay(200);
        var types = light.Received.Select(m => BinaryPrimitives.ReadUInt16LittleEndian(m.AsSpan(32))).ToList();
        Assert.Contains(LifxProtocol.SetColor, types);
        Assert.Equal(2, types.Count(t => t == LifxProtocol.SetPower));   // on, then off
    }
}

public class YeelightTests
{
    static byte[] Reply(int port, string support = "get_prop set_power set_rgb set_bright") => Encoding.ASCII.GetBytes(
        $"HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nLocation: yeelight://127.0.0.1:{port}\r\nid: 0x000000000abc1234\r\nmodel: color\r\nsupport: {support}\r\nname: Desk\r\n\r\n");

    [Fact]
    public void Only_colour_bulbs_are_found()
    {
        var bulb = YeelightProtocol.ParseReply(Reply(55443));
        Assert.Equal("0x000000000abc1234".Replace("0x", ""), bulb!.Id.Replace("0x", ""));
        Assert.Equal(55443, bulb.Port);
        Assert.Equal("Desk", bulb.Name);
        Assert.Null(YeelightProtocol.ParseReply(Reply(55443, "get_prop set_power set_bright")));        // white only
        Assert.Null(YeelightProtocol.ParseReply(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nLocation: http://8.8.8.8/\r\nsupport: set_rgb\r\n\r\n")));
        Assert.Null(YeelightProtocol.ParseReply(Encoding.ASCII.GetBytes("nonsense")));
    }

    [Fact]
    public async Task A_bulb_is_found_and_takes_colour_commands_over_tcp()
    {
        using var tcp = new FakeTcpDevice(_ => Encoding.ASCII.GetBytes("""{"id":1,"result":["ok"]}""" + "\r\n"), framed: false);
        using var search = new FakeUdpDevice(m => Encoding.ASCII.GetString(m).Contains("M-SEARCH") ? Reply(tcp.Port) : null);
        var source = new YeelightSource([search.Endpoint]);
        var sink = Assert.Single(await source.RefreshAsync(default));
        Assert.Equal("Desk", sink.Name);

        await sink.SendAsync([new Color(1, 0, 0)], default);
        await sink.SendAsync([Color.Black], default);
        Assert.Contains(tcp.Messages, m => m.Contains("set_rgb") && m.Contains("16711680"));
        Assert.Contains(tcp.Messages, m => m.Contains("set_power") && m.Contains("\"off\""));
    }
}

public class KasaTests
{
    [Fact]
    public void The_scrambling_round_trips_and_matches_the_known_first_bytes()
    {
        var json = KasaProtocol.SysInfoRequest;
        Assert.Equal(json, KasaProtocol.Decrypt(KasaProtocol.Encrypt(json)));
        Assert.Equal(0xD0, KasaProtocol.Encrypt("{")[0]);   // 171 xor '{' (123), the start of every Kasa message
        var framed = KasaProtocol.Framed(json);
        Assert.Equal((uint)json.Length, BinaryPrimitives.ReadUInt32BigEndian(framed));
    }

    static string Plug(string extra = "") => "{\"system\":{\"get_sysinfo\":{\"deviceId\":\"8006AABBCCDDEEFF\",\"alias\":\"PC plug\",\"model\":\"HS110(EU)\",\"mic_type\":\"IOT.SMARTPLUGSWITCH\",\"feature\":\"TIM:ENE\"" + extra + "}}}";
    const string Bulb = """{"system":{"get_sysinfo":{"deviceId":"8012BULB0001","alias":"Lamp","model":"KL130","mic_type":"IOT.SMARTBULB","is_color":1}}}""";

    [Fact]
    public void Plugs_and_bulbs_are_told_apart()
    {
        var plug = KasaProtocol.ParseInfo(Plug())!;
        Assert.True(plug.IsPlug); Assert.True(plug.HasPower); Assert.False(plug.IsLight);
        var bulb = KasaProtocol.ParseInfo(Bulb)!;
        Assert.True(bulb.IsColour); Assert.False(bulb.IsPlug);
        Assert.False(KasaProtocol.ParseInfo(Plug(",\"children\":[]"))!.IsPlug);   // a power strip is not one plug
        Assert.Null(KasaProtocol.ParseInfo("""{"system":{}}"""));
        Assert.Null(KasaProtocol.ParseInfo("nope"));
    }

    [Fact]
    public void Watts_are_read_from_both_generations_and_never_negative()
    {
        Assert.Equal(12.5, KasaProtocol.ParseWatts("""{"emeter":{"get_realtime":{"power":12.5}}}"""));
        Assert.Equal(12.5, KasaProtocol.ParseWatts("""{"emeter":{"get_realtime":{"power_mw":12500}}}"""));
        Assert.Equal(0, KasaProtocol.ParseWatts("""{"emeter":{"get_realtime":{"power":-3}}}"""));
        Assert.Null(KasaProtocol.ParseWatts("""{"emeter":{"err_code":-1}}"""));
    }

    [Fact]
    public void The_light_command_carries_hue_saturation_and_brightness()
    {
        var text = KasaProtocol.SetLight(new Color(0, 0, 1));
        Assert.Contains("\"hue\":240", text);
        Assert.Contains("\"saturation\":100", text);
        Assert.Contains("\"brightness\":100", text);
    }

    [Fact]
    public async Task A_plug_is_read_and_a_bulb_is_driven_and_nothing_ever_switches_a_relay()
    {
        using var tcp = new FakeTcpDevice(m =>
        {
            var request = KasaProtocol.Decrypt(m);
            var reply = request.Contains("emeter") ? """{"emeter":{"get_realtime":{"power_mw":42000}}}""" : """{"ok":true}""";
            return KasaProtocol.Framed(reply);
        }, framed: true);
        using var udp = new FakeUdpDevice(_ => KasaProtocol.Encrypt(Plug()));
        var directory = new KasaDirectory([udp.Endpoint], tcp.Port);
        var monitor = new KasaPlugMonitor(directory, () => new PlugLinks(["kasa:8006AABBCCDDEEFF"], null));
        await monitor.PollAsync(default);
        var plug = Assert.Single(monitor.Plugs);
        Assert.Equal("PC plug", plug.Name);
        Assert.Equal(42, monitor.ReadingOf("kasa:8006AABBCCDDEEFF")!.Watts);
        Assert.All(tcp.Messages, m => Assert.DoesNotContain("relay", m, StringComparison.OrdinalIgnoreCase));
        Assert.All(tcp.Messages, m => Assert.DoesNotContain("set_", m, StringComparison.OrdinalIgnoreCase));

        using var udp2 = new FakeUdpDevice(_ => KasaProtocol.Encrypt(Bulb));
        var lightSource = new KasaSource(new KasaDirectory([udp2.Endpoint], tcp.Port));
        var sink = Assert.Single(await lightSource.RefreshAsync(default));
        await sink.SendAsync([new Color(1, 0, 0)], default);
        Assert.Contains(tcp.Messages, m => m.Contains("transition_light_state") && m.Contains("\"hue\":0"));
    }
}

public class ShellyTests
{
    [Fact]
    public void Devices_are_recognised_from_the_shelly_page_only()
    {
        var gen1 = ShellyProtocol.ParseInfo("""{"type":"SHPLG-S","mac":"AABBCCDDEEFF","auth":false,"fw":"1.0"}""")!;
        Assert.Equal(1, gen1.Generation); Assert.False(gen1.NeedsPassword);
        var gen2 = ShellyProtocol.ParseInfo("""{"id":"shellyplus1pm-aabbccddeeff","mac":"AABBCCDDEEFF","model":"SNSW-001P16EU","gen":2,"auth_en":true}""")!;
        Assert.Equal(2, gen2.Generation); Assert.True(gen2.NeedsPassword);
        Assert.Null(ShellyProtocol.ParseInfo("""{"hello":"router"}"""));
        Assert.Null(ShellyProtocol.ParseInfo("<html>"));
    }

    [Fact]
    public void Watts_come_from_either_generation()
    {
        Assert.Equal(7.5, ShellyProtocol.ParseWatts("""{"meters":[{"power":7.5}]}"""));
        Assert.Equal(60, ShellyProtocol.ParseWatts("""{"id":0,"apower":60.0}"""));
        Assert.Null(ShellyProtocol.ParseWatts("""{"relays":[{"ison":true}]}"""));
    }

    [Fact]
    public void Only_read_paths_exist()
    {
        Assert.Equal("/status", ShellyProtocol.StatusPath(1));
        Assert.Equal("/rpc/Switch.GetStatus?id=0", ShellyProtocol.StatusPath(2));
    }

    [Fact]
    public async Task A_plug_is_found_and_read_and_only_read()
    {
        var paths = new List<string>();
        using var listener = new HttpListener();
        var port = new Random().Next(20000, 40000);
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var ctx = await listener.GetContextAsync();
                    lock (paths) paths.Add(ctx.Request.HttpMethod + " " + ctx.Request.Url!.PathAndQuery);
                    var body = ctx.Request.Url!.AbsolutePath == "/shelly" ? """{"type":"SHPLG-S","mac":"AABBCCDDEEFF","auth":false}""" : """{"meters":[{"power":33.0}]}""";
                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }
            catch (Exception) { /* closed */ }
        });

        using var http = new HttpClient();
        var monitor = new ShellyPlugMonitor(http, () => new PlugLinks(["shelly:aabbccddeeff"], null), () => ["127.0.0.1"], port);
        await monitor.PollAsync(default);
        Assert.Single(monitor.Plugs);
        Assert.Equal(33, monitor.ReadingOf("shelly:aabbccddeeff")!.Watts);
        lock (paths)
        {
            Assert.All(paths, p => Assert.StartsWith("GET ", p));
            Assert.All(paths, p => Assert.DoesNotContain("relay", p, StringComparison.OrdinalIgnoreCase));
            Assert.All(paths, p => Assert.DoesNotContain("turn", p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
