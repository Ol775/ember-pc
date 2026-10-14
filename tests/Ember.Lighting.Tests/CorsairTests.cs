using Ember.Lighting.Corsair;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class CorsairTests
{
    sealed class FakeApi(CorsairState state, params CorsairDevice[] devices) : ICorsairApi
    {
        public CorsairState State = state;
        public bool Accepts = true;
        public int Disconnects;
        public (string Id, uint[] Leds, byte[] Rgb)? Sent;
        public CorsairState Connect() => State;
        public IReadOnlyList<CorsairDevice> Devices() => devices;
        public bool SetColors(string deviceId, uint[] ledIds, byte[] rgb) { Sent = (deviceId, ledIds, rgb); return Accepts; }
        public void Disconnect() => Disconnects++;
        public void Dispose() { }
    }

    static readonly CorsairDevice Keyboard = new("kb1", "K70", 0x0001, [10, 11, 12]);

    [Fact]
    public async Task A_connected_iCUE_lists_each_device_with_its_led_count()
    {
        var src = new CorsairSource(() => new FakeApi(CorsairState.Connected, Keyboard, new CorsairDevice("m1", "Dark Core", 0x0002, [1])));
        var sinks = await src.RefreshAsync(default);
        Assert.Equal(2, sinks.Count);
        Assert.Equal("corsair:kb1", sinks[0].Id);
        Assert.Equal("Corsair K70", sinks[0].Name);
        Assert.Equal("Keyboard", sinks[0].Kind);
        Assert.Equal(3, sinks[0].LedCount);
        Assert.Contains("2 devices", src.Status);
    }

    [Fact]
    public async Task Colours_go_to_iCUE_as_red_green_blue_for_each_led_id()
    {
        var api = new FakeApi(CorsairState.Connected, Keyboard);
        var sink = (await new CorsairSource(() => api).RefreshAsync(default))[0];
        await sink.SendAsync([new Color(1, 0.5, 0), Color.Black], default);
        Assert.Equal("kb1", api.Sent!.Value.Id);
        Assert.Equal([10u, 11u, 12u], api.Sent.Value.Leds);
        Assert.Equal([255, 128, 0, 0, 0, 0, 0, 0, 0], api.Sent.Value.Rgb); // the third LED had no colour: dark
    }

    [Fact]
    public async Task A_refusal_from_iCUE_is_reported_as_an_error()
    {
        var api = new FakeApi(CorsairState.Connected, Keyboard) { Accepts = false };
        var sink = (await new CorsairSource(() => api).RefreshAsync(default))[0];
        await Assert.ThrowsAsync<IOException>(() => sink.SendAsync([Color.Black], default));
    }

    [Fact]
    public async Task Without_iCUE_there_are_no_devices_and_the_status_says_what_to_do()
    {
        var api = new FakeApi(CorsairState.Unavailable);
        var src = new CorsairSource(() => api);
        Assert.Empty(await src.RefreshAsync(default));
        Assert.Contains("Enable SDK", src.Status);
    }

    [Fact]
    public async Task A_missing_sdk_file_is_explained()
    {
        var src = new CorsairSource(() => null);
        Assert.Empty(await src.RefreshAsync(default));
        Assert.Contains("missing", src.Status);
    }

    [Fact]
    public void The_sdk_file_is_looked_for_in_the_iCUE_folder_beside_the_app()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ember-icue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "iCUE"));
        try
        {
            Assert.Null(CorsairLocator.Find(dir));
            File.WriteAllText(Path.Combine(dir, "iCUE", CorsairLocator.FileName), "x");
            Assert.NotNull(CorsairLocator.Find(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class CorsairRefusedTests
{
    sealed class RefusedApi : ICorsairApi
    {
        public CorsairState Connect() => CorsairState.Refused;
        public IReadOnlyList<CorsairDevice> Devices() => [];
        public bool SetColors(string deviceId, uint[] ledIds, byte[] rgb) => false;
        public string Detail => "session refused, iCUE 4.32.129";
        public void Disconnect() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task A_refusal_from_a_running_iCUE_says_to_switch_on_Enable_SDK()
    {
        var src = new CorsairSource(() => new RefusedApi());
        Assert.Empty(await src.RefreshAsync(default));
        Assert.Contains("running but would not let Ember in", src.Status);
        Assert.Contains("Enable SDK", src.Status);
        Assert.Contains("4.32.129", src.Status);
    }
}
