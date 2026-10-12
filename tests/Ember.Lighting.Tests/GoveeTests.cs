using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ember.Lighting.Govee;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class GoveeLanTests
{
    static int FreePort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }

    sealed class FakeLamp : IDisposable
    {
        readonly UdpClient _scan = new(new IPEndPoint(IPAddress.Loopback, 0));
        readonly UdpClient _control;
        readonly CancellationTokenSource _cts = new();
        public int ScanPort => ((IPEndPoint)_scan.Client.LocalEndPoint!).Port;
        public int ControlPort { get; } = FreePort();
        public List<string> Commands { get; } = [];
        public string ReplyJson { get; set; } =
            """{"msg":{"cmd":"scan","data":{"ip":"127.0.0.1","device":"AA:BB:CC:DD:EE:FF:00:11","sku":"H6008","bleVersionHard":"3.01.01"}}}""";

        public FakeLamp(int replyPort)
        {
            _control = new UdpClient(new IPEndPoint(IPAddress.Loopback, ControlPort));
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var r = await _scan.ReceiveAsync(_cts.Token);
                        await _scan.SendAsync(Encoding.UTF8.GetBytes(ReplyJson), new IPEndPoint(IPAddress.Loopback, replyPort), _cts.Token);
                    }
                }
                catch (Exception) { }
            });
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var r = await _control.ReceiveAsync(_cts.Token);
                        lock (Commands) Commands.Add(Encoding.UTF8.GetString(r.Buffer));
                    }
                }
                catch (Exception) { }
            });
        }

        public void Dispose() { _cts.Cancel(); _scan.Dispose(); _control.Dispose(); }
    }

    [Fact]
    public async Task Finds_a_lamp_that_answers_the_scan()
    {
        var listen = FreePort();
        using var lamp = new FakeLamp(listen);
        var client = new GoveeLanClient([new IPEndPoint(IPAddress.Loopback, lamp.ScanPort)], listen, lamp.ControlPort);
        var found = await client.DiscoverAsync(TimeSpan.FromMilliseconds(600));
        var d = Assert.Single(found);
        Assert.Equal("H6008", d.Sku);
        Assert.Equal(IPAddress.Loopback, d.Ip);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"msg":{"cmd":"devStatus","data":{}}}""")]
    [InlineData("""{"msg":{"cmd":"scan","data":{"ip":"not an ip","device":"x","sku":"y"}}}""")]
    [InlineData("""{"msg":{"cmd":"scan","data":{"ip":"::1","device":"x","sku":"y"}}}""")]
    [InlineData("""{"msg":{"cmd":"scan","data":{"ip":"10.0.0.2","device":"","sku":"y"}}}""")]
    public void Scan_replies_that_are_not_lamps_are_ignored(string json) =>
        Assert.False(GoveeLanClient.TryParseScanReply(Encoding.UTF8.GetBytes(json), out _));

    [Fact]
    public async Task Sink_turns_on_once_sets_the_average_colour_and_turns_off_for_black()
    {
        var listen = FreePort();
        using var lamp = new FakeLamp(listen);
        var client = new GoveeLanClient(null, listen, lamp.ControlPort);
        var sink = new GoveeLanSink(client, new GoveeLanDevice("id1", "H6008", IPAddress.Loopback));
        Assert.Equal("govee:id1", sink.Id);
        Assert.False(sink.Keepalive);

        await sink.SendAsync([new Color(1, 0, 0), new Color(0, 0, 1)], default);   // average: 128, 0, 128
        await sink.SendAsync([new Color(1, 0, 0)], default);
        await sink.SendAsync([Color.Black], default);
        await sink.SendAsync([Color.Black], default);                                // already off: nothing sent
        await Task.Delay(300);
        string[] cmds;
        lock (lamp.Commands) cmds = lamp.Commands.ToArray();
        Assert.Equal(4, cmds.Length);
        Assert.Contains("\"turn\"", cmds[0]);
        Assert.Contains("\"value\":1", cmds[0]);
        using (var doc = JsonDocument.Parse(cmds[1]))
        {
            var c = doc.RootElement.GetProperty("msg").GetProperty("data").GetProperty("color");
            Assert.Equal(128, c.GetProperty("r").GetInt32());
            Assert.Equal(128, c.GetProperty("b").GetInt32());
        }
        Assert.Contains("\"colorwc\"", cmds[2]);
        Assert.Contains("\"value\":0", cmds[3]);
    }

    [Fact]
    public async Task Source_keeps_a_lamp_that_one_scan_missed_and_reports_when_none_exist()
    {
        var listen = FreePort();
        using var lamp = new FakeLamp(listen);
        var source = new GoveeSource(new GoveeLanClient([new IPEndPoint(IPAddress.Loopback, lamp.ScanPort)], listen, lamp.ControlPort), () => null);
        Assert.Single(await source.RefreshAsync(default));
        Assert.Contains("1 on the network", source.Status);
        lamp.ReplyJson = "nothing useful";
        Assert.Single(await source.RefreshAsync(default)); // missed once, still known

        var none = new GoveeSource(new GoveeLanClient([new IPEndPoint(IPAddress.Loopback, FreePort())], FreePort(), 1), () => null);
        Assert.Empty(await none.RefreshAsync(default));
        Assert.Contains("LAN control", none.Status);
    }
}

public class GoveeCloudTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return reply(request);
        }
    }

    static HttpResponseMessage Json(string json, int status = 200) =>
        new((System.Net.HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    const string DeviceList = """
        {"code":200,"message":"success","data":[
          {"sku":"H6072","device":"AA:11","deviceName":"Floor lamp","type":"devices.types.light",
           "capabilities":[{"type":"devices.capabilities.on_off","instance":"powerSwitch"},{"type":"devices.capabilities.color_setting","instance":"colorRgb"}]},
          {"sku":"H5080","device":"BB:22","deviceName":"Plug","type":"devices.types.socket","capabilities":[{"type":"devices.capabilities.on_off","instance":"powerSwitch"}]}
        ]}
        """;

    [Fact]
    public async Task Lists_devices_with_the_key_in_a_header_and_flags_the_ones_that_take_a_colour()
    {
        var h = new Handler(_ => Json(DeviceList));
        var client = new GoveeCloudClient(new HttpClient(h), () => " my-key ");
        var devices = await client.GetDevicesAsync();
        Assert.Equal(["Floor lamp", "Plug"], devices.Select(d => d.Name));
        Assert.Equal([true, false], devices.Select(d => d.CanSetColor));
        var req = h.Seen[0].Request;
        Assert.Equal("https://openapi.api.govee.com/router/api/v1/user/devices", req.RequestUri!.ToString());
        Assert.Equal("my-key", req.Headers.GetValues("Govee-API-Key").Single());
    }

    [Fact]
    public async Task Sets_a_colour_as_one_rgb_number()
    {
        var h = new Handler(_ => Json("""{"code":200}"""));
        var client = new GoveeCloudClient(new HttpClient(h), () => "k");
        await client.SetColorAsync(new GoveeCloudDevice("AA:11", "H6072", "Lamp", true), 255, 128, 1);
        using var doc = JsonDocument.Parse(h.Seen[0].Body);
        var cap = doc.RootElement.GetProperty("payload").GetProperty("capability");
        Assert.Equal("colorRgb", cap.GetProperty("instance").GetString());
        Assert.Equal((255 << 16) | (128 << 8) | 1, cap.GetProperty("value").GetInt32());
        Assert.Equal("AA:11", doc.RootElement.GetProperty("payload").GetProperty("device").GetString());
    }

    [Theory]
    [InlineData(401, typeof(UnauthorizedAccessException))]
    [InlineData(429, typeof(IOException))]
    [InlineData(500, typeof(IOException))]
    public async Task Failures_become_plain_errors(int status, Type expected)
    {
        var client = new GoveeCloudClient(new HttpClient(new Handler(_ => Json("{}", status))), () => "k");
        await Assert.ThrowsAsync(expected, () => client.GetDevicesAsync());
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var h = new Handler(_ => Json(DeviceList));
        var client = new GoveeCloudClient(new HttpClient(h), () => "  ");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetDevicesAsync());
        Assert.Empty(h.Seen);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"data":"x"}""")]
    [InlineData("""{"data":[{"sku":"H1"},{"device":"x"},5]}""")]
    public void Odd_answers_give_an_empty_list(string json) => Assert.Empty(GoveeCloudClient.ParseDevices(json));

    [Fact]
    public async Task Cloud_sink_is_slow_and_only_sends_changes_and_never_realtime()
    {
        var h = new Handler(_ => Json("{}"));
        var dev = new GoveeCloudDevice("AA:11", "H6072", "Lamp", true);
        var sink = new GoveeCloudSink(new GoveeCloudClient(new HttpClient(h), () => "k"), dev);
        Assert.False(sink.Realtime);
        Assert.False(sink.Keepalive);
        Assert.True(sink.MinInterval >= TimeSpan.FromSeconds(5));
        await sink.SendAsync([new Color(1, 0, 0)], default);
        Assert.Equal(2, h.Seen.Count); // power on, then colour
        await sink.SendAsync([Color.Black], default);
        Assert.Equal(3, h.Seen.Count);
    }
}

public class SecretStoreTests
{
    [Fact]
    public void Memory_store_sets_gets_and_removes()
    {
        var s = new MemorySecretStore();
        Assert.Null(s.Get("k"));
        Assert.True(s.Set("k", "v"));
        Assert.Equal("v", s.Get("k"));
        Assert.True(s.Set("k", null));
        Assert.Null(s.Get("k"));
    }

    [Fact]
    public void Windows_credential_store_round_trips_and_leaves_nothing_behind()
    {
        if (!OperatingSystem.IsWindows()) return;
        var s = new WindowsCredentialStore();
        var name = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Null(s.Get(name));
            Assert.True(s.Set(name, "abc-123-ünï"));
            Assert.Equal("abc-123-ünï", s.Get(name));
            Assert.True(s.Set(name, "second"));
            Assert.Equal("second", s.Get(name));
        }
        finally { s.Set(name, null); }
        Assert.Null(s.Get(name));
        Assert.True(s.Set(name, null)); // removing what is not there is fine
    }
}
