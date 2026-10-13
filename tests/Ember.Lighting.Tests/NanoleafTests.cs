using System.Net;
using Ember.Lighting.Nanoleaf;

namespace Ember.Lighting.Tests;

public class NanoleafTests
{
    const string Info = """{"name":"Living room","serialNo":"S1 2-3","model":"NL29"}""";
    const string Layout = """
    {"numPanels":4,"sideLength":150,"positionData":[
      {"panelId":50,"x":300,"y":0,"o":0,"shapeType":8},
      {"panelId":10,"x":0,"y":0,"o":0,"shapeType":8},
      {"panelId":99,"x":0,"y":0,"o":0,"shapeType":12},
      {"panelId":20,"x":150,"y":0,"o":0,"shapeType":8}]}
    """;

    [Fact]
    public void Panels_are_ordered_left_to_right_and_the_controller_part_is_left_out()
    {
        var info = NanoleafClient.ParseInfo(Info, Layout);
        Assert.Equal("Living room", info.Name);
        Assert.Equal("S123", info.Serial);
        Assert.Equal([10, 20, 50], info.Panels.Select(p => p.Id));
    }

    [Fact]
    public void Garbage_answers_give_no_panels_and_do_not_throw()
    {
        var info = NanoleafClient.ParseInfo("nope", "[1,2]");
        Assert.Empty(info.Panels);
        Assert.Equal("Nanoleaf", info.Name);
    }

    [Fact]
    public void Colours_are_written_as_one_frame_per_panel()
    {
        var panels = new[] { new NanoleafPanel(10, 0, 0), new NanoleafPanel(20, 1, 0) };
        var json = NanoleafClient.FramePayload(panels, [new Color(1, 0, 0), new Color(0, 0.5, 1)]);
        Assert.Contains("\"command\":\"display\"", json);
        Assert.Contains("\"animData\":\"2 10 1 255 0 0 0 1 20 1 0 128 255 0 1\"", json);
    }

    [Fact]
    public void A_panel_without_a_colour_is_dark()
    {
        var json = NanoleafClient.FramePayload([new NanoleafPanel(7, 0, 0)], []);
        Assert.Contains("1 7 1 0 0 0 0 1", json);
    }

    [Fact]
    public void Pairing_reads_the_token_and_refuses_anything_else()
    {
        Assert.Equal("AbC123", NanoleafClient.ParsePairing("""{"auth_token":"AbC123"}"""));
        Assert.Throws<InvalidOperationException>(() => NanoleafClient.ParsePairing("""{"auth_token":"a/../b"}"""));
        Assert.Throws<InvalidOperationException>(() => NanoleafClient.ParsePairing("<html>"));
    }

    sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Seen.Add((r.Method, r.RequestUri!.AbsoluteUri, r.Content is null ? "" : await r.Content.ReadAsStringAsync(c)));
            return answer(r);
        }
    }

    [Fact]
    public async Task A_controller_that_is_not_in_pairing_mode_says_how_to_start_it()
    {
        var http = new HttpClient(new Fake(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<NanoleafPairingException>(() => NanoleafClient.PairAsync(http, "192.168.1.30"));
    }

    [Fact]
    public async Task The_token_goes_in_the_path_to_the_controller_only()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new NanoleafClient(new HttpClient(fake), "192.168.1.30", "TOK");
        await client.ShowAsync([new NanoleafPanel(1, 0, 0)], [Color.White]);
        var (method, url, _) = Assert.Single(fake.Seen);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("http://192.168.1.30:16021/api/v1/TOK/effects", url);
    }

    [Fact]
    public async Task Dark_frames_switch_the_panels_off_once_and_light_brings_them_back()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new NanoleafClient(new HttpClient(fake), "h", "T");
        var sink = new NanoleafSink(client, NanoleafClient.ParseInfo(Info, Layout));
        Color[] dark = [Color.Black, Color.Black, Color.Black], lit = [Color.White, Color.White, Color.White];

        await sink.SendAsync(lit, default);
        Assert.Contains(fake.Seen, s => s.Url.EndsWith("/state") && s.Body.Contains("true"));
        fake.Seen.Clear();
        await sink.SendAsync(dark, default);
        Assert.Contains(fake.Seen, s => s.Url.EndsWith("/state") && s.Body.Contains("false"));
        fake.Seen.Clear();
        await sink.SendAsync(dark, default);
        Assert.Empty(fake.Seen);
    }

    [Fact]
    public void Discovery_accepts_only_nanoleaf_replies_with_a_network_address()
    {
        const string good = "HTTP/1.1 200 OK\r\nST: nanoleaf:nl29\r\nLocation: http://192.168.1.30:16021\r\n\r\n";
        Assert.Equal("192.168.1.30", NanoleafDiscovery.ParseAddress(good));
        Assert.Null(NanoleafDiscovery.ParseAddress("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\nLocation: http://192.168.1.2:80\r\n\r\n"));
        Assert.Null(NanoleafDiscovery.ParseAddress("nanoleaf\r\nLocation: http://evil.example:80\r\n"));
    }

    [Theory]
    [InlineData("192.168.1.30", true)]
    [InlineData("nanoleaf.local", true)]
    [InlineData("192.168.1.30/x", false)]
    [InlineData("a:16021", false)]
    [InlineData("", false)]
    public void Only_plain_addresses_are_accepted(string host, bool ok) => Assert.Equal(ok, NanoleafSettings.IsValidHost(host));

    [Fact]
    public async Task The_source_is_quiet_until_a_controller_is_linked()
    {
        var source = new NanoleafSource(() => null);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.StartsWith("Not set up", source.Status);
    }

    [Fact]
    public async Task A_linked_controller_shows_up_as_one_device_with_a_light_per_panel()
    {
        var fake = new Fake(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.AbsolutePath.EndsWith("layout") ? Layout : Info) });
        var source = new NanoleafSource(() => new NanoleafClient(new HttpClient(fake), "h", "T"));
        var sink = Assert.Single(await source.RefreshAsync(default));
        Assert.Equal(3, sink.LedCount);
        Assert.StartsWith("nanoleaf:", sink.Id);
    }
}
