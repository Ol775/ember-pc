using Ember.Lighting;
using Ember.Lighting.Logitech;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class LogitechTests
{
    sealed class FakeLed(bool initOk = true, bool accepts = true) : ILogitechLed
    {
        public List<string> Calls { get; } = [];
        public byte[]? Bitmap;
        public (int, int, int)? Light;
        public bool Init() { Calls.Add("init"); return initOk; }
        public bool SetTargetDevice(LogitechTarget t) { Calls.Add("target " + t); return accepts; }
        public bool SetLighting(int r, int g, int b) { Calls.Add("light"); Light = (r, g, b); return accepts; }
        public bool SetLightingFromBitmap(byte[] bgra) { Calls.Add("bitmap"); Bitmap = bgra; return accepts; }
        public void Shutdown() => Calls.Add("shutdown");
        public void Dispose() => Calls.Add("dispose");
    }

    [Fact]
    public async Task The_keyboard_gets_one_colour_per_key_column_in_blue_green_red_order()
    {
        var api = new FakeLed();
        var ctx = new LogitechContext(api);
        Assert.True(ctx.Start());
        var leds = Enumerable.Range(0, 21).Select(i => i == 0 ? new Color(1, 0.5, 0) : Color.Black).ToArray();
        await new LogitechKeyboardSink(ctx).SendAsync(leds, default);

        Assert.Equal(21 * 6 * 4, api.Bitmap!.Length);
        Assert.Equal([0, 128, 255, 255], api.Bitmap[..4]); // row 0, column 0: B, G, R, A
        var lastRow = (5 * 21) * 4;
        Assert.Equal([0, 128, 255, 255], api.Bitmap[lastRow..(lastRow + 4)]); // every row of the column
        Assert.Equal([0, 0, 0, 255], api.Bitmap[4..8]); // column 1 is black
        Assert.Contains("target PerKeyRgb", api.Calls);
    }

    [Fact]
    public async Task The_mouse_gets_the_average_colour_in_percent()
    {
        var api = new FakeLed();
        var ctx = new LogitechContext(api);
        ctx.Start();
        await new LogitechMouseSink(ctx).SendAsync([new Color(1, 0, 0), new Color(0, 0, 0)], default);
        Assert.Equal((50, 0, 0), api.Light);
        Assert.Contains("target Rgb", api.Calls);
    }

    [Fact]
    public async Task A_refused_call_makes_the_send_fail_and_the_next_refresh_start_over()
    {
        var api = new FakeLed(accepts: false);
        var ctx = new LogitechContext(api);
        ctx.Start();
        await Assert.ThrowsAsync<IOException>(() => new LogitechMouseSink(ctx).SendAsync([Color.Black], default));
        Assert.False(ctx.Ready);
    }

    [Fact]
    public void Sending_before_G_HUB_is_ready_fails_instead_of_calling_the_sdk()
    {
        var api = new FakeLed(initOk: false);
        var ctx = new LogitechContext(api);
        Assert.False(ctx.Start());
        Assert.Throws<InvalidOperationException>(() => ctx.Devices(1, 2, 3));
        Assert.DoesNotContain("light", api.Calls);
    }

    [Fact]
    public async Task Without_G_HUB_the_source_offers_nothing_and_says_why()
    {
        var source = new LogitechSource(() => null);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("not installed", source.Status);
    }

    [Fact]
    public async Task With_G_HUB_running_the_source_offers_the_keyboard_and_the_mouse_and_keeps_them()
    {
        var source = new LogitechSource(() => new FakeLed());
        var first = await source.RefreshAsync(default);
        Assert.Equal(["logitech:keyboard", "logitech:mouse"], first.Select(s => s.Id));
        Assert.Same(first[0], (await source.RefreshAsync(default))[0]);
        Assert.Contains("Connected", source.Status);
    }

    [Fact]
    public async Task G_HUB_not_answering_is_reported_and_retried()
    {
        var api = new FakeLed(initOk: false);
        var source = new LogitechSource(() => api);
        Assert.Empty(await source.RefreshAsync(default));
        Assert.Contains("not answering", source.Status);
        await source.RefreshAsync(default);
        Assert.Equal(2, api.Calls.Count(c => c == "init"));
    }

    [Fact]
    public async Task Closing_hands_the_lighting_back_to_G_HUB()
    {
        var api = new FakeLed();
        var source = new LogitechSource(() => api);
        await source.RefreshAsync(default);
        await source.DisposeAsync();
        Assert.Contains("shutdown", api.Calls);
    }

    [Fact]
    public void Only_the_expected_file_under_Program_Files_is_trusted()
    {
        var root = Path.Combine(Path.GetTempPath(), "ember-pf-" + Guid.NewGuid().ToString("N"));
        var good = Path.Combine(root, "LGHUB", "sdks", LogitechLocator.FileName);
        var wrongName = Path.Combine(root, "LGHUB", "sdks", "evil.dll");
        var elsewhere = Path.Combine(Path.GetTempPath(), "ember-else-" + Guid.NewGuid().ToString("N"), LogitechLocator.FileName);
        try
        {
            foreach (var f in new[] { good, wrongName, elsewhere }) { Directory.CreateDirectory(Path.GetDirectoryName(f)!); File.WriteAllText(f, "x"); }
            Assert.True(LogitechLocator.IsTrusted(good, root));
            Assert.False(LogitechLocator.IsTrusted(wrongName, root));
            Assert.False(LogitechLocator.IsTrusted(elsewhere, root));
            Assert.False(LogitechLocator.IsTrusted(Path.Combine(root, "LGHUB", "..", "..", Path.GetFileName(Path.GetDirectoryName(elsewhere)!), LogitechLocator.FileName), root));
            Assert.False(LogitechLocator.IsTrusted(Path.Combine(root, "missing", LogitechLocator.FileName), root));
            Assert.False(LogitechLocator.IsTrusted(null, root));
            Assert.False(LogitechLocator.IsTrusted("", root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.Delete(Path.GetDirectoryName(elsewhere)!, true);
        }
    }
}
