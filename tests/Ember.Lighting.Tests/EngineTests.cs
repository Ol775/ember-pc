using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class EngineTests
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    sealed class Sink(string id, int leds = 3, double minMs = 100, bool keepalive = true) : IDeviceSink
    {
        public string Id => id;
        public string Name => id;
        public string Kind => "test";
        public int LedCount => leds;
        public TimeSpan MinInterval => TimeSpan.FromMilliseconds(minMs);
        public bool Keepalive => keepalive;
        public List<Color[]> Sent { get; } = [];
        public Func<Task>? Block;
        public Exception? Fail;

        public async Task SendAsync(IReadOnlyList<Color> l, CancellationToken ct)
        {
            if (Block is not null) await Block();
            if (Fail is not null) throw Fail;
            Sent.Add(l.ToArray());
        }
    }

    static Scene Scene(Color c, params string[] devices) =>
        new("s", [new Layer("a", "a", new StaticEffect(c))], devices.Select(d => new DevicePlacement(d, 0, 0, 1, 0.1)).ToList());

    static readonly LightingInputs In = new(0);
    static readonly Color Red = new(1, 0, 0), Green = new(0, 1, 0);

    [Fact]
    public async Task Sends_to_placed_devices_only()
    {
        var clock = new Clock();
        var a = new Sink("a");
        var b = new Sink("b");
        var frame = await new LightingEngine(clock).StepAsync(Scene(Red, "a"), true, In, [a, b]);
        Assert.Single(a.Sent);
        Assert.Empty(b.Sent);
        Assert.Equal(["a"], frame.Keys);
    }

    [Fact]
    public async Task Does_not_resend_an_unchanged_picture_until_the_keepalive()
    {
        var clock = new Clock();
        var a = new Sink("a");
        var e = new LightingEngine(clock);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        clock.Now += TimeSpan.FromMilliseconds(500);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        Assert.Single(a.Sent);
        clock.Now += TimeSpan.FromSeconds(2);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        Assert.Equal(2, a.Sent.Count);
    }

    [Fact]
    public async Task A_device_without_keepalive_is_only_sent_changes()
    {
        var clock = new Clock();
        var a = new Sink("a", keepalive: false);
        var e = new LightingEngine(clock);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        clock.Now += TimeSpan.FromSeconds(10);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        Assert.Single(a.Sent);
        await e.StepAsync(Scene(Green, "a"), true, In, [a]);
        Assert.Equal(2, a.Sent.Count);
    }

    [Fact]
    public async Task Respects_the_minimum_interval_of_each_device()
    {
        var clock = new Clock();
        var slow = new Sink("slow", minMs: 1000);
        var fast = new Sink("fast", minMs: 10);
        var e = new LightingEngine(clock);
        await e.StepAsync(Scene(Red, "slow", "fast"), true, In, [slow, fast]);
        clock.Now += TimeSpan.FromMilliseconds(200);
        await e.StepAsync(Scene(Green, "slow", "fast"), true, In, [slow, fast]);
        Assert.Single(slow.Sent);
        Assert.Equal(2, fast.Sent.Count);
        clock.Now += TimeSpan.FromSeconds(1);
        await e.StepAsync(Scene(Green, "slow", "fast"), true, In, [slow, fast]);
        Assert.Equal(2, slow.Sent.Count);
    }

    [Fact]
    public async Task Does_not_start_a_second_send_while_one_is_running()
    {
        var clock = new Clock();
        var gate = new TaskCompletionSource();
        var a = new Sink("a", minMs: 0) { Block = () => gate.Task };
        var e = new LightingEngine(clock);
        var first = e.StepAsync(Scene(Red, "a"), true, In, [a]);
        await e.StepAsync(Scene(Green, "a"), true, In, [a]); // returns at once: the send to "a" is still running
        gate.SetResult();
        await first;
        Assert.Single(a.Sent);
    }

    [Fact]
    public async Task Off_sends_one_black_frame_to_lit_devices_and_then_stays_quiet()
    {
        var clock = new Clock();
        var a = new Sink("a", minMs: 0);
        var e = new LightingEngine(clock);
        await e.StepAsync(Scene(Red, "a"), true, In, [a]);
        clock.Now += TimeSpan.FromSeconds(1);
        var frame = await e.StepAsync(Scene(Red, "a"), false, In, [a]);
        Assert.Empty(frame);
        Assert.Equal(2, a.Sent.Count);
        Assert.All(a.Sent[1], c => Assert.Equal(0, c.R));
        clock.Now += TimeSpan.FromSeconds(5);
        await e.StepAsync(Scene(Red, "a"), false, In, [a]);
        Assert.Equal(2, a.Sent.Count);
    }

    [Fact]
    public async Task Off_never_touches_a_device_that_was_never_lit()
    {
        var a = new Sink("a");
        await new LightingEngine(new Clock()).StepAsync(Scene(Red, "a"), false, In, [a]);
        Assert.Empty(a.Sent);
    }

    [Fact]
    public async Task A_failing_device_is_reported_and_retried_later_without_stopping_the_others()
    {
        var clock = new Clock();
        var bad = new Sink("bad") { Fail = new IOException("gone") };
        var good = new Sink("good");
        var e = new LightingEngine(clock);
        await e.StepAsync(Scene(Red, "bad", "good"), true, In, [bad, good]);
        Assert.Single(good.Sent);
        Assert.IsType<IOException>(e.Failures["bad"]);
        bad.Fail = null;
        clock.Now += TimeSpan.FromSeconds(3);
        await e.StepAsync(Scene(Red, "bad", "good"), true, In, [bad, good]);
        Assert.Single(bad.Sent);
        Assert.Empty(e.Failures);
    }
}

public class ScreenSyncMonitorTests
{
    [Fact]
    public void A_scene_remembers_its_own_screen_and_old_files_mean_the_main_screen()
    {
        var scene = new Scene("Movies", [new Layer("l", "Screen sync", new ScreenSyncEffect(@"\.\DISPLAY2"))], []);
        var json = System.Text.Json.JsonSerializer.Serialize(scene);
        var back = System.Text.Json.JsonSerializer.Deserialize<Scene>(json)!;
        Assert.Equal(@"\.\DISPLAY2", ((ScreenSyncEffect)back.Layers[0].Effect).Monitor);
        // a scene saved before this existed has no Monitor
        var old = System.Text.Json.JsonSerializer.Serialize(new Scene("Movies", [new Layer("l", "Screen sync", new ScreenSyncEffect())], [])).Replace(@",""Monitor"":null", "");
        Assert.DoesNotContain("Monitor", old);
        Assert.Null(((ScreenSyncEffect)System.Text.Json.JsonSerializer.Deserialize<Scene>(old)!.Layers[0].Effect).Monitor);
    }
}
