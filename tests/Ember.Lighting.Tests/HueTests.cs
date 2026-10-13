using System.Net;
using System.Text;
using Ember.Lighting.Hue;
using Ember.Lighting.OpenRgb;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class HueTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-hue-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    /// <summary>A pretend bridge: answers each request from a function and remembers what it was asked.</summary>
    sealed class Bridge(Func<HttpRequestMessage, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body, string? Key)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.ToString(), body, request.Headers.TryGetValues("hue-application-key", out var v) ? v.Single() : null));
            var (status, text) = answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    const string LightList = """
        {"errors":[],"data":[
          {"id":"a1","metadata":{"name":"Desk lamp"},"on":{"on":true},"dimming":{"brightness":80},"color":{"xy":{"x":0.4,"y":0.4}}},
          {"id":"b2","metadata":{"name":"Hall bulb"},"on":{"on":false},"dimming":{"brightness":50}},
          {"id":"c3","metadata":{"name":"Strip"},"color":{"xy":{"x":0.3,"y":0.3}}}
        ]}
        """;

    // ---- colour ----

    [Theory]
    [InlineData(1, 0, 0, 0.700, 0.299)]    // red
    [InlineData(0, 1, 0, 0.172, 0.747)]    // green
    [InlineData(0, 0, 1, 0.136, 0.040)]    // blue
    [InlineData(1, 1, 1, 0.323, 0.329)]    // white
    public void Primary_colours_land_where_Philips_puts_them(double r, double g, double b, double x, double y)
    {
        var c = HueColour.FromColor(new Color(r, g, b));
        Assert.InRange(c.X, x - 0.01, x + 0.01);
        Assert.InRange(c.Y, y - 0.01, y + 0.01);
        Assert.Equal(100, c.Brightness);
    }

    [Fact]
    public void A_dim_colour_keeps_its_hue_and_lowers_the_brightness()
    {
        var full = HueColour.FromColor(new Color(1, 0.45, 0.1));
        var dim = HueColour.FromColor(new Color(0.5, 0.225, 0.05));
        Assert.Equal(full.X, dim.X, 3);
        Assert.Equal(full.Y, dim.Y, 3);
        Assert.Equal(50, dim.Brightness, 1);
    }

    [Fact]
    public void Black_has_no_brightness_and_never_divides_by_zero()
    {
        var c = HueColour.FromColor(new Color(0, 0, 0));
        Assert.Equal(0, c.Brightness);
        Assert.False(double.IsNaN(c.X) || double.IsNaN(c.Y));
    }

    // ---- pairing and lights ----

    [Fact]
    public void Pairing_returns_the_key_the_bridge_hands_out() =>
        Assert.Equal("abc123", HueClient.ParsePairing("""[{"success":{"username":"abc123"}}]"""));

    [Fact]
    public void Pairing_before_the_button_is_pressed_says_so() =>
        Assert.Throws<HueLinkButtonException>(() => HueClient.ParsePairing("""[{"error":{"type":101,"address":"","description":"link button not pressed"}}]"""));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Pairing_with_something_that_is_not_a_bridge_fails_clearly(string body) =>
        Assert.Contains("did not look like", Assert.Throws<InvalidOperationException>(() => HueClient.ParsePairing(body)).Message);

    [Fact]
    public async Task Pairing_posts_to_the_bridge_and_returns_the_key()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.OK, """[{"success":{"username":"k"}}]"""));
        Assert.Equal("k", await HueClient.PairAsync(new HttpClient(bridge), "192.168.1.5"));
        Assert.Equal(("https://192.168.1.5/api", HttpMethod.Post), (bridge.Requests[0].Url, bridge.Requests[0].Method));
        Assert.Contains("ember#pc", bridge.Requests[0].Body);
    }

    [Fact]
    public void Only_colour_lights_are_offered_and_odd_answers_give_no_lights()
    {
        var lights = HueClient.ParseLights(LightList);
        Assert.Equal(["a1", "b2", "c3"], lights.Select(l => l.Id));
        Assert.Equal([true, false, true], lights.Select(l => l.CanColour));
        Assert.Empty(HueClient.ParseLights("not json"));
        Assert.Empty(HueClient.ParseLights("""{"data":[{"nothing":1}]}"""));
    }

    [Fact]
    public async Task Asking_for_lights_sends_the_key_in_the_header_and_nowhere_else()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.OK, LightList));
        var lights = await new HueClient(new HttpClient(bridge), "10.0.0.2", "secret").GetLightsAsync();
        Assert.Equal(3, lights.Count);
        Assert.Equal("https://10.0.0.2/clip/v2/resource/light", bridge.Requests[0].Url);
        Assert.Equal("secret", bridge.Requests[0].Key);
    }

    [Fact]
    public async Task A_rejected_key_asks_for_the_bridge_to_be_linked_again()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.Forbidden, "{}"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new HueClient(new HttpClient(bridge), "h", "old").GetLightsAsync());
        Assert.Contains("Link it again", ex.Message);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HueClient(new HttpClient(bridge), "h", null).GetLightsAsync());
        Assert.Empty(bridge.Requests);
    }

    [Fact]
    public void The_command_for_a_colour_has_on_brightness_colour_and_no_fade()
    {
        var json = HueClient.Payload(true, 0.4, 0.35, 70);
        Assert.Contains("\"on\":{\"on\":true}", json);
        Assert.Contains("\"brightness\":70", json);
        Assert.Contains("\"xy\":{\"x\":0.4,\"y\":0.35}", json);
        Assert.Contains("\"duration\":0", json);
        Assert.Equal("{\"on\":{\"on\":false},\"dynamics\":{\"duration\":0}}", HueClient.Payload(false, 0, 0, 0));
    }

    // ---- the source and its lights ----

    [Fact]
    public async Task Without_a_linked_bridge_the_source_offers_nothing_and_says_how_to_start()
    {
        var source = new HueSource(() => null);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("Link your Hue bridge", source.Status);
    }

    [Fact]
    public async Task A_linked_bridge_gives_one_sink_per_colour_light_and_they_are_kept()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.OK, LightList));
        var client = new HueClient(new HttpClient(bridge), "10.0.0.2", "k");
        var source = new HueSource(() => client);
        var first = await source.RefreshAsync(default);
        Assert.Equal(["hue:a1", "hue:c3"], first.Select(s => s.Id));
        Assert.Equal("Desk lamp", first[0].Name);
        Assert.Equal(1, first[0].LedCount);
        Assert.Same(first[0], (await source.RefreshAsync(default))[0]);
        Assert.Contains("2 colour lights", source.Status);
    }

    [Fact]
    public async Task A_bridge_that_cannot_be_reached_is_reported_without_throwing()
    {
        var client = new HueClient(new HttpClient(new Bridge(_ => throw new HttpRequestException("no route"))), "10.0.0.9", "k");
        var source = new HueSource(() => client);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("could not reach 10.0.0.9", source.Status);
    }

    [Fact]
    public async Task A_colour_is_sent_as_xy_and_brightness_and_black_switches_the_light_off_once()
    {
        var bridge = new Bridge(r => (HttpStatusCode.OK, r.Method == HttpMethod.Get ? LightList : "{}"));
        var source = new HueSource(() => new HueClient(new HttpClient(bridge), "h", "k"));
        var sink = (await source.RefreshAsync(default))[0];
        bridge.Requests.Clear();

        await sink.SendAsync([new Color(1, 0, 0)], default);
        Assert.Equal(HttpMethod.Put, bridge.Requests[0].Method);
        Assert.EndsWith("/light/a1", bridge.Requests[0].Url);
        Assert.Contains("\"on\":{\"on\":true}", bridge.Requests[0].Body);

        await sink.SendAsync([new Color(0, 0, 0)], default);
        Assert.Contains("\"on\":false", bridge.Requests[1].Body);
        await sink.SendAsync([new Color(0, 0, 0)], default);
        Assert.Equal(2, bridge.Requests.Count);   // already off: nothing more is sent
    }

    [Fact]
    public async Task Each_light_waits_for_its_share_of_the_bridge_limit_of_about_ten_commands_a_second()
    {
        var bridge = new Bridge(_ => (HttpStatusCode.OK, LightList));
        var sinks = await new HueSource(() => new HueClient(new HttpClient(bridge), "h", "k")).RefreshAsync(default);
        Assert.All(sinks, s => Assert.True(s.MinInterval >= TimeSpan.FromMilliseconds(200)));   // two lights: 100 ms each in turn
        Assert.False(sinks[0].Keepalive);
    }

    // ---- certificate pinning, settings and discovery ----

    [Fact]
    public void Only_the_remembered_certificate_is_trusted_after_linking()
    {
        using var mine = MakeCert("CN=bridge-one");
        using var other = MakeCert("CN=bridge-two");
        Assert.True(HuePinning.Accept(mine, mine.Thumbprint, null));
        Assert.False(HuePinning.Accept(other, mine.Thumbprint, null));
        Assert.False(HuePinning.Accept(null, mine.Thumbprint, null));
    }

    [Fact]
    public void While_linking_the_certificate_is_remembered_but_only_if_someone_is_listening()
    {
        using var cert = MakeCert("CN=bridge");
        string? learned = null;
        Assert.True(HuePinning.Accept(cert, null, t => learned = t));
        Assert.Equal(cert.Thumbprint, learned);
        Assert.False(HuePinning.Accept(cert, null, null));   // an unpinned bridge is never trusted silently
    }

    static System.Security.Cryptography.X509Certificates.X509Certificate2 MakeCert(string subject)
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(subject, rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData("hue-bridge.local", true)]
    [InlineData("", false)]
    [InlineData("192.168.1.5/evil", false)]
    [InlineData("host:8080", false)]
    [InlineData("a b", false)]
    public void Only_plain_addresses_are_accepted_as_a_bridge(string host, bool ok) => Assert.Equal(ok, HueSettings.IsValidHost(host));

    [Fact]
    public void Settings_survive_a_save_and_a_missing_or_damaged_file_gives_none()
    {
        var store = new HueSettingsStore(Path.Combine(_dir, "hue.json"));
        Assert.Null(store.Load());
        Assert.True(store.TrySave(new HueSettings("192.168.1.5", "ABCD")));
        Assert.Equal(new HueSettings("192.168.1.5", "ABCD"), store.Load());
        File.WriteAllText(Path.Combine(_dir, "hue.json"), "{ nope");
        Assert.Null(store.Load());
        Assert.False(store.TrySave(new HueSettings("bad host/", null)));
        Assert.True(store.TrySave(null));
        Assert.False(File.Exists(Path.Combine(_dir, "hue.json")));
    }

    [Fact]
    public void Discovery_keeps_only_real_ipv4_addresses()
    {
        var found = HueDiscovery.ParseAddresses("""[{"id":"x","internalipaddress":"192.168.1.20"},{"id":"y","internalipaddress":"not an ip"},{"id":"z","internalipaddress":"192.168.1.20"},{"id":"w","internalipaddress":"fe80::1"}]""");
        Assert.Equal(["192.168.1.20"], found);
        Assert.Empty(HueDiscovery.ParseAddresses("garbage"));
    }
}
