using Ember.Lighting.OpenRgb;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class ManagerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-mgr-" + Guid.NewGuid().ToString("N"));
    string P => Path.Combine(_dir, "l.json");
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    sealed class Sink(string id) : IDeviceSink
    {
        public string Id => id;
        public string Name => id;
        public string Kind => "Strip";
        public int LedCount => 4;
        public TimeSpan MinInterval => TimeSpan.Zero;
        public bool Keepalive => true;
        public List<Color[]> Sent { get; } = [];
        public Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct) { lock (Sent) Sent.Add(leds.ToArray()); return Task.CompletedTask; }
    }

    sealed class Source(params Sink[] sinks) : IDeviceSource
    {
        public string Name => "Fake";
        public string Status => "ok";
        public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<IDeviceSink>>(sinks);
        public void ReportFailure(string sinkId) { }
    }

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }

    [Fact]
    public void New_devices_get_a_row_each_on_the_current_scene_and_never_twice()
    {
        var s = LightingState.Default;
        var a = new Sink("a");
        var b = new Sink("b");
        var once = LightingManager.Placed(s, [a, b]);
        Assert.Equal(["a", "b"], once.Current.Placements.Select(p => p.DeviceId));
        Assert.True(once.Current.Placements[1].Y > once.Current.Placements[0].Y);
        Assert.Same(once, LightingManager.Placed(once, [a, b]));
        Assert.True(LightingState.IsValid(once));
    }

    [Fact]
    public async Task Lights_start_off_and_nothing_is_sent_until_switched_on()
    {
        var sink = new Sink("a");
        await using var m = new LightingManager(new LightingStore(P), TimeProvider.System, [new Source(sink)]);
        m.Start();
        await Until(() => m.Devices.Count == 1);
        await Task.Delay(200);
        Assert.Empty(sink.Sent);
        Assert.False(m.State.On);

        m.SetOn(true);
        await Until(() => { lock (sink.Sent) return sink.Sent.Count > 0; });
        Assert.Equal(4, sink.Sent[0].Length);
        Assert.True(sink.Sent[0][0].R > 0.9); // the default warm colour
        Assert.True(m.Preview.ContainsKey("a"));
    }

    [Fact]
    public async Task Changes_are_saved_and_come_back()
    {
        await using (var m = new LightingManager(new LightingStore(P), TimeProvider.System, []))
        {
            m.SetOn(true);
            Assert.True(m.SaveSceneAs("Evening"));
            m.EditScene(s => s with { Brightness = 0.4 });
            m.SetOpenRgb("C:\\Tools\\OpenRGB.exe", true);
        }
        var back = new LightingStore(P).Load();
        Assert.True(back.On);
        Assert.Equal("Evening", back.CurrentScene);
        Assert.Equal(0.4, back.Current.Brightness);
        Assert.Equal(2, back.Scenes.Count);
        Assert.True(back.StartOpenRgb);
        Assert.True(back.ShouldStartOpenRgb);
    }

    [Fact]
    public async Task Scene_commands_keep_at_least_one_scene_and_reject_bad_names()
    {
        await using var m = new LightingManager(new LightingStore(P), TimeProvider.System, []);
        Assert.False(m.SaveSceneAs("   "));
        Assert.False(m.SaveSceneAs(new string('x', 61)));
        m.DeleteScene("Warm glow");
        Assert.Single(m.State.Scenes);
        Assert.True(m.SaveSceneAs("Two"));
        m.DeleteScene("Two");
        Assert.Equal("Warm glow", m.State.CurrentScene);
        m.SelectScene("missing");
        Assert.Equal("Warm glow", m.State.CurrentScene);
    }

    [Fact]
    public async Task Invalid_edits_are_ignored()
    {
        await using var m = new LightingManager(new LightingStore(P), TimeProvider.System, []);
        m.EditScene(s => s with { Brightness = 5 });                  // clamped by SetBrightness only; raw edit is out of range
        Assert.Equal(1, m.State.Current.Brightness);
        m.SetBrightness(5);
        Assert.Equal(1, m.State.Current.Brightness);                   // clamped, unchanged
        m.SetBrightness(0.25);
        Assert.Equal(0.25, m.State.Current.Brightness);
    }

    [Fact]
    public async Task Screen_is_only_wanted_while_an_enabled_screen_layer_is_on()
    {
        await using var m = new LightingManager(new LightingStore(P), TimeProvider.System, []);
        Assert.False(m.WantsScreen);
        m.EditScene(s => s with { Layers = [.. s.Layers, new Layer("s", "Screen", new ScreenSyncEffect())] });
        Assert.False(m.WantsScreen); // lights are off
        m.SetOn(true);
        Assert.True(m.WantsScreen);
    }
}
