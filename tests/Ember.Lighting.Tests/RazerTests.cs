using System.Net;
using Ember.Lighting.Razer;

namespace Ember.Lighting.Tests;

public class RazerTests
{
    sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string Body)> Seen { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Seen.Add((r.Method, r.RequestUri!.AbsoluteUri, r.Content is null ? "" : await r.Content.ReadAsStringAsync(c)));
            return answer(r);
        }
    }

    static HttpResponseMessage Ok(string body = "{}") => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    const string Session = """{"sessionid":"1","uri":"http://localhost:54236/chromasdk"}""";

    [Fact]
    public void A_colour_is_packed_blue_green_red()
    {
        Assert.Equal(0x0000FF, RazerChroma.Pack(new Color(1, 0, 0)));
        Assert.Equal(0xFF0000, RazerChroma.Pack(new Color(0, 0, 1)));
        Assert.Equal(0x00FF00, RazerChroma.Pack(new Color(0, 1, 0)));
    }

    [Fact]
    public void A_keyboard_is_six_rows_of_22_and_a_pad_is_one_row()
    {
        var kb = RazerKind.All.Single(k => k.Key == "keyboard");
        var json = RazerChroma.Payload(kb, Enumerable.Repeat(Color.White, kb.LedCount).ToList());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rows = doc.RootElement.GetProperty("param").EnumerateArray().ToList();
        Assert.Equal(6, rows.Count);
        Assert.All(rows, r => Assert.Equal(22, r.GetArrayLength()));

        var pad = RazerKind.All.Single(k => k.Key == "mousepad");
        using var doc2 = System.Text.Json.JsonDocument.Parse(RazerChroma.Payload(pad, []));
        Assert.Equal(15, doc2.RootElement.GetProperty("param").GetArrayLength());
        Assert.Equal(System.Text.Json.JsonValueKind.Number, doc2.RootElement.GetProperty("param")[0].ValueKind);
    }

    [Fact]
    public void The_mouse_is_not_one_of_the_devices()
    {
        Assert.DoesNotContain(RazerKind.All, k => k.Key == "mouse");
    }

    [Theory]
    [InlineData("""{"uri":"http://localhost:54236/chromasdk"}""", true)]
    [InlineData("""{"uri":"http://127.0.0.1:54236/chromasdk/"}""", true)]
    [InlineData("""{"uri":"http://evil.example/chromasdk"}""", false)]
    [InlineData("""{"uri":"https://localhost:5/chromasdk"}""", false)]
    [InlineData("""{"uri":"http://localhost:5/other"}""", false)]
    [InlineData("nope", false)]
    public void Only_a_session_on_this_pc_is_accepted(string body, bool ok)
    {
        if (ok) Assert.Equal("/chromasdk", RazerChroma.ParseSession(body).AbsolutePath);
        else Assert.Throws<InvalidOperationException>(() => RazerChroma.ParseSession(body));
    }

    [Fact]
    public async Task Sending_registers_once_then_sends_the_grid_to_the_device()
    {
        var fake = new Fake(r => r.Method == HttpMethod.Post ? Ok(Session) : Ok());
        var chroma = new RazerChroma(new HttpClient(fake));
        var kb = RazerKind.All[0];
        await chroma.SendAsync(kb, [Color.White], default);
        await chroma.SendAsync(kb, [Color.Black], default);
        Assert.Single(fake.Seen, s => s.Method == HttpMethod.Post);
        Assert.Equal(2, fake.Seen.Count(s => s.Url == "http://localhost:54236/chromasdk/keyboard" && s.Method == HttpMethod.Put));
    }

    [Fact]
    public async Task A_failure_means_registering_again_on_the_next_frame()
    {
        var fail = true;
        var fake = new Fake(r => r.Method == HttpMethod.Post ? Ok(Session) : fail ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : Ok());
        var chroma = new RazerChroma(new HttpClient(fake));
        await Assert.ThrowsAsync<InvalidOperationException>(() => chroma.SendAsync(RazerKind.All[0], [], default));
        fail = false;
        await chroma.SendAsync(RazerKind.All[0], [], default);
        Assert.Equal(2, fake.Seen.Count(s => s.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task The_source_does_not_contact_synapse_until_it_is_switched_on()
    {
        var fake = new Fake(_ => Ok(Session));
        var on = false;
        var source = new RazerSource(() => on, new RazerChroma(new HttpClient(fake)));
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Empty(fake.Seen);

        on = true;
        var sinks = await source.RefreshAsync(default);
        Assert.Equal(RazerKind.All.Count, sinks.Count);
        Assert.All(sinks, s => Assert.StartsWith("razer:", s.Id));
    }

    [Fact]
    public async Task Synapse_not_running_is_said_plainly()
    {
        var fake = new Fake(_ => throw new HttpRequestException("refused"));
        var source = new RazerSource(() => true, new RazerChroma(new HttpClient(fake)));
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("Synapse did not answer", source.Status);
    }
}
