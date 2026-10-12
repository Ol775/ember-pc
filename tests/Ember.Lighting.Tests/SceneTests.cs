using Ember.Lighting;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class SceneTests
{
    static readonly Color Red = new(1, 0, 0), Blue = new(0, 0, 1);
    static LightingInputs At(double s, double? cpu = null) => new(s, cpu);

    static Scene Of(params Layer[] layers) => new("t", layers, [new DevicePlacement("d", 0, 0, 1, 0.1)]);
    static Layer L(Effect e, double opacity = 1, BlendMode m = BlendMode.Normal, bool on = true) => new("id", "n", e, opacity, m, on);

    [Fact]
    public void Hex_round_trips_and_bad_text_is_refused()
    {
        Assert.Equal("#FF7A2F", Color.TryParseHex("ff7a2f")!.Value.ToHex());
        Assert.Null(Color.TryParseHex("#12345"));
        Assert.Null(Color.TryParseHex("zzzzzz"));
        Assert.Null(Color.TryParseHex(null));
    }

    [Fact]
    public void Static_fills_every_led()
    {
        var frame = Compositor.Render(Of(L(new StaticEffect(Red))), new Dictionary<string, int> { ["d"] = 4 }, At(0));
        Assert.Equal(4, frame["d"].Length);
        Assert.All(frame["d"], c => Assert.Equal(Red, c));
    }

    [Fact]
    public void Layers_stack_bottom_to_top_and_opacity_mixes()
    {
        var scene = Of(L(new StaticEffect(Red)), L(new StaticEffect(Blue), 0.5));
        var c = Compositor.SamplePoint(scene, 0.5, 0.05, At(0));
        Assert.Equal(0.5, c.R, 3);
        Assert.Equal(0.5, c.B, 3);
    }

    [Fact]
    public void Disabled_layer_and_zero_opacity_do_nothing()
    {
        var scene = Of(L(new StaticEffect(Red)), L(new StaticEffect(Blue), 1, BlendMode.Normal, on: false), L(new StaticEffect(Blue), 0));
        Assert.Equal(Red, Compositor.SamplePoint(scene, 0.5, 0.05, At(0)));
    }

    [Fact]
    public void Blend_modes()
    {
        var half = new Color(0.5, 0.5, 0.5);
        Assert.Equal(1.0, Compositor.Blend(half, new Color(0.9, 0, 0), BlendMode.Add, 1).R, 3);          // clamped
        Assert.Equal(0.25, Compositor.Blend(half, half, BlendMode.Multiply, 1).G, 3);
        Assert.Equal(0.75, Compositor.Blend(half, half, BlendMode.Screen, 1).B, 3);
    }

    [Fact]
    public void Brightness_scales_the_result()
    {
        var scene = Of(L(new StaticEffect(Red))) with { Brightness = 0.5 };
        Assert.Equal(0.5, Compositor.SamplePoint(scene, 0.5, 0.05, At(0)).R, 3);
    }

    [Fact]
    public void Breathing_goes_dark_and_bright()
    {
        var e = new BreathingEffect(Red, 4);
        Assert.Equal(0, e.Sample(0, 0, At(0)).R, 3);
        Assert.Equal(1, e.Sample(0, 0, At(2)).R, 3);
    }

    [Fact]
    public void Wave_moves_with_time_and_stays_between_its_colours()
    {
        var e = new WaveEffect(Red, Blue, Speed: 1, Wavelength: 1);
        var a = e.Sample(0.2, 0, At(0));
        var b = e.Sample(0.2, 0, At(0.25));
        Assert.NotEqual(a, b);
        Assert.InRange(a.R + a.B, 0.999, 1.001);
    }

    [Fact]
    public void Temperature_glow_follows_the_reading_and_falls_back_to_cold()
    {
        var e = new TemperatureGlowEffect(TempSensor.Cpu, Blue, Red, 40, 80);
        Assert.Equal(Blue, e.Sample(0, 0, At(0)));
        Assert.Equal(Blue, e.Sample(0, 0, At(0, 30)));
        Assert.Equal(0.5, e.Sample(0, 0, At(0, 60)).R, 3);
        Assert.Equal(Red, e.Sample(0, 0, At(0, 95)));
    }

    [Fact]
    public void Screen_sync_picks_a_zone_by_x_and_covers_nothing_without_a_picture()
    {
        var e = new ScreenSyncEffect();
        Assert.Equal(0, e.Sample(0.5, 0, At(0)).A);
        var zones = new[] { Red, Blue };
        Assert.Equal(Red, e.Sample(0.2, 0, new LightingInputs(0, null, null, zones)));
        Assert.Equal(Blue, e.Sample(1.0, 0, new LightingInputs(0, null, null, zones)));
        // with a transparent screen layer on top, the layer below still shows
        var scene = Of(L(new StaticEffect(Red)), L(e));
        Assert.Equal(Red, Compositor.SamplePoint(scene, 0.5, 0.05, At(0)));
    }

    [Fact]
    public void Leds_run_along_the_long_side_and_a_single_led_sits_in_the_middle()
    {
        var p = new DevicePlacement("d", 0.2, 0.4, 0.4, 0.1);
        Assert.Equal((0.25, 0.45), p.LedPosition(0, 4));
        Assert.Equal(0.55, p.LedPosition(3, 4).X, 6);
        var tall = new DevicePlacement("d", 0, 0, 0.1, 0.5);
        Assert.Equal(0.05, tall.LedPosition(0, 5).X, 6);
        Assert.Equal(0.05, tall.LedPosition(0, 5).Y, 6);
        Assert.Equal((0.4, 0.45), p.LedPosition(0, 1) is var (x, y) ? (Math.Round(x, 6), Math.Round(y, 6)) : default);
    }

    [Fact]
    public void Devices_without_a_placement_or_switched_off_or_unknown_are_skipped()
    {
        var scene = new Scene("t", [L(new StaticEffect(Red))],
            [new DevicePlacement("a", 0, 0, 1, 0.1), new DevicePlacement("b", 0, 0, 1, 0.1, Enabled: false), new DevicePlacement("gone", 0, 0, 1, 0.1)]);
        var frame = Compositor.Render(scene, new Dictionary<string, int> { ["a"] = 2, ["b"] = 2, ["c"] = 2 }, At(0));
        Assert.Equal(["a"], frame.Keys);
    }
}

public class LightingStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-light-" + Guid.NewGuid().ToString("N"));
    string P(string n) => Path.Combine(_dir, n);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Missing_file_gives_defaults_with_lights_off()
    {
        var s = new LightingStore(P("none.json")).Load();
        Assert.False(s.On);
        Assert.Equal("Warm glow", s.Current.Name);
    }

    [Fact]
    public void Scenes_with_every_effect_round_trip()
    {
        var scene = new Scene("Mix", [
            new Layer("1", "a", new StaticEffect(new Color(1, 0, 0)), 0.8, BlendMode.Add),
            new Layer("2", "b", new WaveEffect(new Color(0, 1, 0), new Color(0, 0, 1), 0.3, 2, 45)),
            new Layer("3", "c", new BreathingEffect(new Color(1, 1, 0), 6), 1, BlendMode.Screen, false),
            new Layer("4", "d", new TemperatureGlowEffect(TempSensor.Gpu, new Color(0, 0, 1), new Color(1, 0, 0), 30, 90)),
            new Layer("5", "e", new ScreenSyncEffect()),
        ], [new DevicePlacement("openrgb:0", 0.1, 0.2, 0.5, 0.1)], 0.7);
        var state = new LightingState(true, "Mix", [scene], "C:\\Tools\\OpenRGB.exe", true);
        var store = new LightingStore(P("sub/l.json"));
        Assert.True(store.TrySave(state));
        var back = store.Load();
        Assert.True(back.On);
        Assert.Equal("C:\\Tools\\OpenRGB.exe", back.OpenRgbPath);
        Assert.Equal(scene.Layers.Select(l => l.Effect), back.Current.Layers.Select(l => l.Effect));
        Assert.Equal(scene.Placements, back.Current.Placements);
        Assert.Equal(0.7, back.Current.Brightness);
        Assert.Equal(BlendMode.Screen, back.Current.Layers[2].Blend);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"On\":true,\"CurrentScene\":\"x\",\"Scenes\":[]}")]
    [InlineData("{\"On\":true,\"CurrentScene\":\"x\",\"Scenes\":[{\"Name\":\"x\",\"Layers\":[{\"Id\":\"1\",\"Name\":\"n\",\"Effect\":{\"type\":\"nope\"}}],\"Placements\":[],\"Brightness\":1}]}")]
    [InlineData("{\"On\":true,\"CurrentScene\":\"x\",\"Scenes\":[{\"Name\":\"x\",\"Layers\":[],\"Placements\":[],\"Brightness\":7}]}")]
    public void Bad_files_give_defaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(P("bad.json"), content);
        Assert.False(new LightingStore(P("bad.json")).Load().On);
        Assert.Equal("Warm glow", new LightingStore(P("bad.json")).Load().Current.Name);
    }

    [Fact]
    public void Invalid_state_is_not_saved()
    {
        var tooMany = Enumerable.Range(0, LightingState.MaxScenes + 1).Select(i => Scene.Empty("s" + i)).ToList();
        Assert.False(new LightingStore(P("x.json")).TrySave(new LightingState(true, "s0", tooMany)));
        Assert.False(File.Exists(P("x.json")));
    }

    [Fact]
    public void Unknown_current_scene_falls_back_to_the_first()
    {
        var s = new LightingState(true, "missing", [Scene.Empty("only")]);
        Assert.Equal("only", s.Current.Name);
    }
}

public class ScreenZonesTests
{
    static byte[] Image(int w, int h, Func<int, int, (byte r, byte g, byte b)> pixel)
    {
        var data = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var (r, g, b) = pixel(x, y);
                var i = (y * w + x) * 4;
                data[i] = b; data[i + 1] = g; data[i + 2] = r;
            }
        return data;
    }

    [Fact]
    public void Each_strip_gets_the_average_of_its_columns()
    {
        // left half red, right half blue
        var img = Image(8, 4, (x, _) => x < 4 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255));
        var zones = ScreenZones.FromBgra(img, 8, 4, 4);
        Assert.Equal(4, zones.Length);
        Assert.Equal(1, zones[0].R, 3);
        Assert.Equal(1, zones[1].R, 3);
        Assert.Equal(1, zones[2].B, 3);
        Assert.Equal(1, zones[3].B, 3);
        Assert.Equal(0, zones[0].B, 3);
    }

    [Fact]
    public void Mixed_columns_average()
    {
        var img = Image(4, 2, (_, y) => y == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0));
        Assert.Equal(0.5, ScreenZones.FromBgra(img, 4, 2, 1)[0].G, 2);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    public void Empty_sizes_give_nothing(int w, int h) => Assert.Empty(ScreenZones.FromBgra(new byte[64], w, h));

    [Fact]
    public void A_buffer_that_is_too_short_gives_nothing() => Assert.Empty(ScreenZones.FromBgra(new byte[10], 8, 4));

    [Fact]
    public void More_zones_than_columns_still_works()
    {
        var zones = ScreenZones.FromBgra(Image(2, 2, (_, _) => ((byte)10, (byte)20, (byte)30)), 2, 2, 5);
        Assert.Equal(5, zones.Length);
    }

    [Fact]
    public void Smoothing_moves_part_of_the_way()
    {
        var s = ScreenZones.Smooth([new Color(0, 0, 0)], [new Color(1, 1, 1)], 0.25);
        Assert.Equal(0.25, s[0].R, 3);
        Assert.Same(s, ScreenZones.Smooth(null, s));
    }
}
