using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Ember.Lighting.Govee;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class GoveeSegmentTests
{
    [Fact]
    public void The_enable_and_disable_packets_are_the_ones_other_software_sends()
    {
        Assert.Equal("uwABsQEK", Convert.ToBase64String(GoveeRazer.Enable));
        Assert.Equal("uwABsQAL", Convert.ToBase64String(GoveeRazer.Disable));
    }

    [Fact]
    public void A_frame_has_the_header_the_length_the_count_the_colours_and_an_xor_checksum()
    {
        var f = GoveeRazer.Frame([new Color(1, 0, 0), new Color(0, 0, 1)]);
        Assert.Equal([0xBB, 0x00, 0x08, 0xB0, 0x01, 0x02, 255, 0, 0, 0, 0, 255], f[..^1]);
        byte x = 0;
        foreach (var b in f[..^1]) x ^= b;
        Assert.Equal(x, f[^1]);
        Assert.Equal(13, f.Length);
    }

    [Fact]
    public void The_message_wraps_the_packet_as_base64_in_a_razer_command()
    {
        using var doc = JsonDocument.Parse(GoveeRazer.Message(GoveeRazer.Enable));
        var data = doc.RootElement.GetProperty("msg");
        Assert.Equal("razer", data.GetProperty("cmd").GetString());
        Assert.Equal("uwABsQEK", data.GetProperty("data").GetProperty("pt").GetString());
    }

    [Fact]
    public void More_than_255_segments_are_cut_to_what_the_packet_can_hold() =>
        Assert.Equal(255, GoveeRazer.Frame(Enumerable.Repeat(Color.White, 300).ToArray())[5]);

    static int FreePort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task A_lamp_set_to_several_segments_gets_segment_mode_once_then_frames_and_goes_back_to_normal_when_dark()
    {
        var port = FreePort();
        using var lamp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        var got = new List<string>();
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () => { try { while (true) { var r = await lamp.ReceiveAsync(cts.Token); lock (got) got.Add(Encoding.UTF8.GetString(r.Buffer)); } } catch (Exception) { } });

        var client = new GoveeLanClient(null, FreePort(), port);
        var segments = 4;
        var sink = new GoveeLanSink(client, new GoveeLanDevice("id1", "H6056", IPAddress.Loopback), () => segments);
        Assert.Equal(4, sink.LedCount);

        var red = Enumerable.Repeat(new Color(1, 0, 0), 4).ToArray();
        await sink.SendAsync(red, default);
        await sink.SendAsync(red, default);
        await sink.SendAsync(new Color[4], default);
        await Task.Delay(300);
        cts.Cancel();

        string[] cmds;
        lock (got) cmds = got.ToArray();
        Assert.Contains("\"turn\"", cmds[0]); Assert.Contains("\"value\":1", cmds[0]);
        Assert.Contains("uwABsQEK", cmds[1]);                                     // segment mode on, once
        Assert.Contains("\"razer\"", cmds[2]);                                    // frame
        Assert.Contains("\"razer\"", cmds[3]);                                    // frame again
        Assert.Contains("uwABsQAL", cmds[4]);                                     // dark: segment mode off
        Assert.Contains("\"value\":0", cmds[5]);                                  // and the lamp off
        Assert.Equal(6, cmds.Length);
    }

    [Fact]
    public void One_segment_is_the_old_behaviour_with_no_keepalive()
    {
        var sink = new GoveeLanSink(new GoveeLanClient(), new GoveeLanDevice("id1", "H6008", IPAddress.Loopback));
        Assert.Equal(1, sink.LedCount);
        Assert.False(sink.Keepalive);
    }

    [Fact]
    public async Task The_segment_count_is_saved_per_lamp_and_one_means_default()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-seg-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LightingStore(path);
            await using (var m = new LightingManager(store, TimeProvider.System, []))
            {
                m.SetSegments("govee:a", 12);
                Assert.Equal(12, m.SegmentsOf("govee:a"));
                Assert.Equal(1, m.SegmentsOf("govee:b"));
            }
            Assert.Equal(12, store.Load().Segments!["govee:a"]);
            await using var again = new LightingManager(store, TimeProvider.System, []);
            again.SetSegments("govee:a", 1);
            Assert.Null(again.State.Segments);
            Assert.False(store.TrySave(again.State with { Segments = new Dictionary<string, int> { ["x"] = 0 } }));
            Assert.False(store.TrySave(again.State with { Segments = new Dictionary<string, int> { ["x"] = 999 } }));
        }
        finally { File.Delete(path); }
    }
}
