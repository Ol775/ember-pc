using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Lighting.Tapo;

namespace Ember.Lighting.Tests;

public class TapoTests
{
    /// <summary>The device side of KLAP, in memory: checks the account hash, then answers encrypted requests.</summary>
    sealed class FakeDevice(string email, string password, bool v2, Func<string, object?> answer) : HttpMessageHandler
    {
        byte[]? _local, _remote, _hash;
        KlapSession? _session;
        public List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            if (path == "/app/handshake1")
            {
                _local = body;
                _remote = RandomNumberGenerator.GetBytes(16);
                _hash = v2 ? KlapSession.AuthHashV2(email, password) : KlapSession.AuthHashV1(email, password);
                var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([.. _remote, .. KlapSession.ServerProof(v2, _local, _remote, _hash)]) };
                res.Headers.TryAddWithoutValidation("Set-Cookie", "TP_SESSIONID=ABC;TIMEOUT=1440");
                return res;
            }
            if (path == "/app/handshake2")
            {
                if (!body.AsSpan().SequenceEqual(KlapSession.ClientProof(v2, _local!, _remote!, _hash!))) return new HttpResponseMessage(HttpStatusCode.Forbidden);
                _session = KlapSession.Create(_local!, _remote!, _hash!);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (path == "/app/request" && request.Headers.TryGetValues("Cookie", out var c) && c.Single() == "TP_SESSIONID=ABC")
            {
                var seq = int.Parse(request.RequestUri.Query.Split('=')[1]);
                using var doc = JsonDocument.Parse(_session!.Decrypt(seq, body));
                var method = doc.RootElement.GetProperty("method").GetString()!;
                Methods.Add(method);
                var reply = JsonSerializer.SerializeToUtf8Bytes(new { error_code = 0, result = answer(method) });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_session.Encrypt(reply).Body) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    static readonly object Info = new { device_id = "D1", model = "P110", type = "SMART.TAPOPLUG", nickname = Convert.ToBase64String("Desk plug"u8.ToArray()), device_on = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Signs_in_with_either_firmware_generation_and_reads_the_device(bool v2)
    {
        var fake = new FakeDevice("me@example.com", "secret", v2, m => m == "get_device_info" ? Info : new { current_power = 123456 });
        var device = new TapoDevice(new HttpClient(fake), "10.0.0.9", () => new TapoAccount("me@example.com", "secret"));
        var info = await device.InfoAsync(CancellationToken.None);
        Assert.Equal("Desk plug", info!.Name);
        Assert.True(info.IsPlug);
        Assert.Equal(123.456, await device.WattsAsync(CancellationToken.None));
        Assert.Equal(["get_device_info", "get_energy_usage"], fake.Methods);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_before_anything_is_sent()
    {
        var fake = new FakeDevice("me@example.com", "secret", false, _ => Info);
        var device = new TapoDevice(new HttpClient(fake), "10.0.0.9", () => new TapoAccount("me@example.com", "wrong"));
        var ex = await Assert.ThrowsAsync<TapoException>(() => device.InfoAsync(CancellationToken.None));
        Assert.Contains("email and password", ex.Message);
        Assert.Empty(fake.Methods);
    }

    [Fact]
    public async Task Probe_recognises_a_handshake_and_ignores_other_web_servers()
    {
        var tapo = new FakeDevice("a", "b", false, _ => Info);
        Assert.True(await TapoDevice.ProbeAsync(new HttpClient(tapo), "10.0.0.9", TimeSpan.FromSeconds(1), CancellationToken.None));
        var other = new HttpClient(new StubHandler(HttpStatusCode.NotFound));
        Assert.False(await TapoDevice.ProbeAsync(other, "10.0.0.9", TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    sealed class StubHandler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(code));
    }

    [Fact]
    public void Colour_is_sent_as_hue_saturation_and_brightness()
    {
        Assert.Equal((0, 100, 100), TapoDevice.ToHsv(255, 0, 0));
        Assert.Equal((120, 100, 100), TapoDevice.ToHsv(0, 255, 0));
        Assert.Equal((240, 100, 50), TapoDevice.ToHsv(0, 0, 128));
        Assert.Equal((0, 0, 100), TapoDevice.ToHsv(255, 255, 255));
        Assert.Equal((0, 0, 0), TapoDevice.ToHsv(0, 0, 0));
    }

    [Fact]
    public void Device_info_tells_lights_from_plugs()
    {
        using var bulb = JsonDocument.Parse("""{"device_id":"B","model":"L530E","type":"SMART.TAPOBULB","hue":10,"color_temp":2700,"device_on":false}""");
        var b = TapoInfo.Parse(bulb.RootElement)!;
        Assert.True(b.IsLight);
        Assert.False(b.IsPlug);
        Assert.True(b.Colour);
        Assert.Equal("Tapo L530E", b.Name);
        using var hub = JsonDocument.Parse("""{"device_id":"H","model":"H100","type":"SMART.TAPOHUB"}""");
        var h = TapoInfo.Parse(hub.RootElement)!;
        Assert.False(h.IsLight);
        Assert.False(h.IsPlug);
        using var bad = JsonDocument.Parse("""{"model":"x"}""");
        Assert.Null(TapoInfo.Parse(bad.RootElement));
    }

    [Fact]
    public void Account_round_trips_and_rejects_blank()
    {
        var a = new TapoAccount("me@example.com", "pw");
        Assert.Equal(a, TapoAccount.Restore(a.Serialise()));
        Assert.Null(TapoAccount.Restore("""{"Email":"","Password":"x"}"""));
        Assert.Null(TapoAccount.Restore("nope"));
    }
}
