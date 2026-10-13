using System.Text.Json;
using Ember.Lighting.Govee;
using Ember.Lighting.OpenRgb;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class PaintToolsTests
{
    [Fact]
    public void A_gradient_on_a_strip_runs_from_the_first_colour_to_the_last()
    {
        var g = PaintTools.Gradient(5, 0, new Color(1, 0, 0), new Color(0, 0, 1), 0);
        Assert.Equal("#FF0000", g[0]);
        Assert.Equal("#0000FF", g[4]);
        Assert.Equal("#800080", g[2]);
    }

    [Fact]
    public void A_gradient_on_a_grid_follows_the_angle()
    {
        var across = PaintTools.Gradient(12, 4, new Color(1, 0, 0), new Color(0, 0, 1), 0);   // 4 columns, 3 rows
        Assert.Equal(across[0], across[8]);                 // same column, same colour
        Assert.Equal("#FF0000", across[0]);
        Assert.Equal("#0000FF", across[3]);
        var down = PaintTools.Gradient(12, 4, new Color(1, 0, 0), new Color(0, 0, 1), 90);
        Assert.Equal(down[0], down[3]);                     // same row, same colour
        Assert.Equal("#FF0000", down[0]);
        Assert.Equal("#0000FF", down[11]);
    }

    [Fact]
    public void A_single_led_gets_the_first_colour_and_nothing_throws_on_empty()
    {
        Assert.Equal("#FF0000", PaintTools.Gradient(1, 0, new Color(1, 0, 0), new Color(0, 0, 1), 0)[0]);
        Assert.Empty(PaintTools.Gradient(0, 0, Color.Black, Color.White, 0));
        Assert.Empty(PaintTools.Rainbow(0, 0, 0));
    }

    [Fact]
    public void A_rainbow_starts_red_and_changes_hue_along_the_device()
    {
        var r = PaintTools.Rainbow(10, 0, 0);
        Assert.Equal("#FF0000", r[0]);
        Assert.Equal(10, r.Distinct().Count());
        Assert.All(r, c => Assert.NotNull(Color.TryParseHex(c)));
    }

    [Fact]
    public void Fill_gives_every_led_the_colour_and_is_capped()
    {
        Assert.All(PaintTools.Fill(7, new Color(0, 1, 0)), c => Assert.Equal("#00FF00", c));
        Assert.Equal(PaintTools.MaxLeds, PaintTools.Fill(100_000, Color.White).Count);
    }

    [Fact]
    public void Resample_keeps_the_picture_when_the_led_count_differs()
    {
        var paint = new string?[] { "#FF0000", "#00FF00", "#0000FF", null };
        Assert.Equal(paint, PaintTools.Resample(paint, 4));
        var bigger = PaintTools.Resample(paint, 8);
        Assert.Equal(["#FF0000", "#FF0000", "#00FF00", "#00FF00", "#0000FF", "#0000FF", null, null], bigger);
        Assert.Equal(["#00FF00", null], PaintTools.Resample(paint, 2));
        Assert.Equal(3, PaintTools.Resample([], 3).Count);
    }

    [Fact]
    public void Mirror_flips_each_row_of_a_grid_and_the_whole_of_a_strip()
    {
        string?[] strip = ["a1", "a2", "a3"];
        Assert.Equal(["a3", "a2", "a1"], PaintTools.Mirror(strip, 0));
        string?[] grid = ["1", "2", "3", "4", "5", "6"];
        Assert.Equal(["3", "2", "1", "6", "5", "4"], PaintTools.Mirror(grid, 3));
    }

    [Fact]
    public void An_exported_file_reads_back_the_same()
    {
        var paint = new string?[] { "#FF8000", null, "#00FFAA" };
        var text = PaintFile.Export(paint, 0, "My strip");
        Assert.Equal(paint, PaintFile.TryImport(text));
        using var doc = JsonDocument.Parse(text);
        Assert.Equal("ember-paint", doc.RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public void A_plain_list_of_colours_can_be_imported_with_blank_lines_for_unpainted_leds()
    {
        var read = PaintFile.TryImport("﻿#ff0000\r\n\r\n00FF00\r\n");
        Assert.Equal(["#FF0000", null, "#00FF00", null], read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a colour")]
    [InlineData("{\"format\":\"something-else\",\"paint\":[\"#FF0000\"]}")]
    [InlineData("{\"format\":\"ember-paint\",\"paint\":\"#FF0000\"}")]
    [InlineData("{\"format\":\"ember-paint\",\"paint\":[\"#GG0000\"]}")]
    [InlineData("{\"format\":\"ember-paint\",\"paint\":[12]}")]
    [InlineData("{\"format\":\"ember-paint\",\"paint\":[null,null]}")]
    [InlineData("{\"format\":\"ember-paint\"")]
    [InlineData("#FF0000\nbroken")]
    public void Bad_files_are_refused_without_throwing(string text) => Assert.Null(PaintFile.TryImport(text));

    [Fact]
    public void Oversized_files_and_lists_are_refused()
    {
        Assert.Null(PaintFile.TryImport(new string('x', PaintFile.MaxBytes + 1)));
        var tooMany = string.Join("\n", Enumerable.Repeat("#FF0000", PaintTools.MaxLeds + 1));
        Assert.Null(PaintFile.TryImport(tooMany));
        Assert.Equal(PaintTools.MaxLeds, PaintFile.TryImport(string.Join("\n", Enumerable.Repeat("#FF0000", PaintTools.MaxLeds)))!.Count);
    }
}

public class GridEffectsTests
{
    static LightingInputs At(double seconds, AudioLevels? audio = null, IReadOnlyList<KeyPulse>? pulses = null, LedLocal? local = null) =>
        new(seconds, Audio: audio, Pulses: pulses, Local: local);

    static AudioLevels Level(params double[] bands) =>
        new(bands.Max(), bands.Max(), bands.Max(), bands.Max(), bands.Concat(Enumerable.Repeat(0.0, AudioAnalyzer.BandCount - bands.Length)).ToArray());

    [Fact]
    public void A_ripple_ring_moves_outwards_over_time_and_fades()
    {
        var e = new RippleEffect(Color.White, IntervalSeconds: 10, Speed: 0.2, Width: 0.1);
        // At t = 1 s the ring has radius 0.2 around the middle.
        Assert.True(e.Sample(0.7, 0.5, At(1)).A > 0.8);
        Assert.True(e.Sample(0.5, 0.5, At(1)).A < 0.01);   // inside the ring
        Assert.True(e.Sample(0.95, 0.5, At(1)).A < 0.01);  // not reached yet
        var later = e.Sample(0.9, 0.5, At(2));             // radius 0.4 at t = 2 s, a bit dimmer
        Assert.InRange(later.A, 0.5, 0.95);
        Assert.True(e.Sample(0.7, 0.5, At(1)).A > e.Sample(0.9, 0.5, At(2)).A);
    }

    [Fact]
    public void Ripples_repeat_on_their_interval_and_die_out_far_away()
    {
        var e = new RippleEffect(Color.White, IntervalSeconds: 2, Speed: 0.2, Width: 0.1);
        Assert.True(e.Sample(0.7, 0.5, At(1)).A > 0.8);
        Assert.True(e.Sample(0.7, 0.5, At(3)).A > 0.8);     // the next ring, 2 s later
        Assert.InRange(e.Sample(0.7, 0.5, At(100_000)).A, 0, 1);
    }

    [Fact]
    public void A_ripple_in_device_scope_uses_the_leds_place_inside_the_device()
    {
        var e = new RippleEffect(Color.White, 10, 0.2, 0.1, Scope: EffectScope.Device);
        var local = new LedLocal(0.7, 0.5, 6);
        Assert.True(e.Sample(0.0, 0.0, At(1, local: local)).A > 0.8);
        Assert.True(e.Sample(0.7, 0.5, At(1, local: new LedLocal(0.5, 0.5, 6))).A < 0.01);
    }

    [Fact]
    public void Absurd_ripple_settings_stay_in_range()
    {
        var e = new RippleEffect(Color.White, IntervalSeconds: 0, Speed: 0, Width: 0);
        var a = e.Sample(0.3, 0.3, At(5000)).A;
        Assert.InRange(a, 0, 1);
    }

    [Fact]
    public void Spectrum_bars_rise_with_the_sound_on_a_grid()
    {
        var loud = Level(1.0, 0.25);                         // band 0 full, band 1 a quarter
        var e = new AudioBarsEffect(new Color(0, 1, 0), new Color(1, 0, 0));
        // 6 rows; the leftmost bar fills the column, the second only the bottom row or two.
        double Alpha(double x, double y) => e.Sample(0, 0, At(0, loud, local: new LedLocal(x, y, 6))).A;
        var col0 = 0.5 / 16;
        var col1 = 1.5 / 16;
        Assert.True(Alpha(col0, 0.08) > 0.9);                // top row of the full bar
        Assert.True(Alpha(col1, 0.92) > 0.9);                // bottom row of the short bar
        Assert.True(Alpha(col1, 0.08) < 0.01);               // top row of the short bar is dark
        Assert.True(Alpha(col1, 0.5) < Alpha(col1, 0.92));
    }

    [Fact]
    public void Spectrum_bars_colour_runs_from_low_at_the_bottom_to_high_at_the_top()
    {
        var e = new AudioBarsEffect(new Color(0, 1, 0), new Color(1, 0, 0));
        var l = Level(1.0);
        var bottom = e.Sample(0, 0, At(0, l, local: new LedLocal(0.01, 0.95, 6)));
        var top = e.Sample(0, 0, At(0, l, local: new LedLocal(0.01, 0.05, 6)));
        Assert.True(bottom.G > bottom.R);
        Assert.True(top.R > top.G);
    }

    [Fact]
    public void Spectrum_bars_on_one_row_become_brightness_and_silence_is_dark()
    {
        var e = new AudioBarsEffect(new Color(0, 1, 0), new Color(1, 0, 0));
        var one = new LedLocal(0.01, 0.5, 1);
        var loud = e.Sample(0, 0, At(0, Level(1.0), local: one));
        var quiet = e.Sample(0, 0, At(0, Level(0.2), local: one));
        Assert.True(loud.R + loud.G > quiet.R + quiet.G);
        var silent = e.Sample(0, 0, At(0, null, local: one));
        Assert.Equal(0, silent.R + silent.G + silent.B, 6);
        var grid = e.Sample(0, 0, At(0, null, local: new LedLocal(0.5, 0.9, 6)));
        Assert.True(grid.A < 0.01);
    }

    [Fact]
    public void A_key_press_lights_the_leds_near_it_and_fades_with_time()
    {
        var e = new ReactiveKeysEffect(Color.White, new Color(1, 0, 0), FadeSeconds: 1, Radius: 0.12);
        var pulses = new[] { new KeyPulse(0.5, 0.5, 10) };
        double Alpha(double x, double y, double t) => e.Sample(0, 0, At(t, pulses: pulses, local: new LedLocal(x, y, 6))).A;
        Assert.True(Alpha(0.5, 0.5, 10) > 0.95);             // right on the key, right now
        Assert.True(Alpha(0.5, 0.5, 10) > Alpha(0.5, 0.5, 11));   // later it is dimmer
        Assert.True(Alpha(0.5, 0.5, 10.1) > Alpha(0.6, 0.5, 10.1));    // nearer is brighter
        Assert.Equal(0, Alpha(0.1, 0.9, 10.1), 6);           // far away: nothing
        Assert.Equal(0, Alpha(0.5, 0.5, 9), 6);              // before the press: nothing
        Assert.Equal(0, Alpha(0.5, 0.5, 40), 6);             // long after: gone
    }

    [Fact]
    public void Reactive_keys_without_pulses_or_a_keyboard_position_show_nothing()
    {
        var e = new ReactiveKeysEffect(Color.White, Color.White);
        Assert.Equal(0, e.Sample(0.5, 0.5, At(1)).A);
        Assert.Equal(0, e.Sample(0.5, 0.5, At(1, pulses: [new KeyPulse(0.5, 0.5, 1)])).A);   // canvas scope has no local point
        Assert.Equal(0, e.Sample(0.5, 0.5, At(1, pulses: [], local: new LedLocal(0.5, 0.5, 6))).A);
    }

    [Fact]
    public void The_keyboard_map_puts_keys_where_a_keyboard_has_them()
    {
        (double X, double Y) P(int vk) => KeyboardLayout.Position(vk)!.Value;
        Assert.True(P(0x51 /* Q */).X < P(0x50 /* P */).X);
        Assert.True(P(0x31 /* 1 */).Y < P(0x51).Y && P(0x51).Y < P(0x41 /* A */).Y && P(0x41).Y < P(0x5A /* Z */).Y);
        Assert.True(P(0x20 /* Space */).Y > P(0x5A).Y);
        Assert.True(P(0x1B /* Esc */).X < P(0x70 /* F1 */).X);
        Assert.True(P(0x60 /* Num 0 */).X > P(0x27 /* Right */).X);
        Assert.True(P(0xA0).X < P(0x5A).X && P(0xA1).X > P(0xBF /* / */).X);          // the shifts frame the letters
        Assert.Equal(P(0x10), P(0xA0));                                                // generic Shift = left Shift
        Assert.Null(KeyboardLayout.Position(0xAF));                                    // volume up is not on the map
        Assert.Null(KeyboardLayout.Position(-1));
    }

    [Fact]
    public void Every_mapped_key_is_inside_the_keyboard()
    {
        var mapped = 0;
        for (var vk = 0; vk < 256; vk++)
        {
            if (KeyboardLayout.Position(vk) is not { } p) continue;
            mapped++;
            Assert.InRange(p.X, 0, 1);
            Assert.InRange(p.Y, 0, 1);
        }
        Assert.InRange(mapped, 100, 130);
    }

    [Fact]
    public void Letters_and_digits_all_have_a_place()
    {
        foreach (var vk in Enumerable.Range('A', 26).Concat(Enumerable.Range('0', 10)).Concat(Enumerable.Range(0x70, 12)))
            Assert.NotNull(KeyboardLayout.Position(vk));
    }

    [Fact]
    public void The_compositor_gives_effects_the_place_inside_the_device_so_bars_fill_a_keyboard_wherever_it_sits()
    {
        var bars = new AudioBarsEffect(new Color(0, 1, 0), new Color(1, 0, 0), Scope: EffectScope.Device);
        var scene = new Scene("s", [new Layer("a", "Bars", bars)], [new DevicePlacement("kb", 0.3, 0.6, 0.4, 0.3)]);
        var inputs = new LightingInputs(0, Audio: Level(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
        var frame = Compositor.Render(scene, new Dictionary<string, int> { ["kb"] = 126 }, inputs, columns: new Dictionary<string, int> { ["kb"] = 21 });
        Assert.Equal(126, frame["kb"].Length);
        Assert.All(frame["kb"], c => Assert.True(c.R + c.G > 0.2));                    // every row is lit at full volume
        var quiet = Compositor.Render(scene, new Dictionary<string, int> { ["kb"] = 126 }, new LightingInputs(0, Audio: Level(0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4)), columns: new Dictionary<string, int> { ["kb"] = 21 });
        Assert.True(quiet["kb"][0].R + quiet["kb"][0].G < 0.01);                       // top row stays dark at 40 %
        Assert.True(quiet["kb"][21 * 5].R + quiet["kb"][21 * 5].G > 0.2);              // bottom row is lit
    }

    [Fact]
    public void The_new_effects_survive_saving_and_loading()
    {
        var scene = new Scene("fx", [
            new Layer("1", "r", new RippleEffect(Color.White, 2, 0.3, 0.2, 0.1, 0.9, EffectScope.Device)),
            new Layer("2", "b", new AudioBarsEffect(Color.Black, Color.White, 2, EffectScope.Canvas)),
            new Layer("3", "k", new ReactiveKeysEffect(Color.White, new Color(1, 0, 0), 2, 0.3)),
        ], []);
        var path = Path.Combine(Path.GetTempPath(), "ember-fx-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LightingStore(path);
            Assert.True(store.TrySave(new LightingState(false, "fx", [scene])));
            var back = store.Load().Current;
            Assert.Equal(scene.Layers.Select(l => l.Effect), back.Layers.Select(l => l.Effect));
        }
        finally { File.Delete(path); }
    }
}

public class GoveeDetectTests
{
    [Fact]
    public void The_cloud_range_gives_the_segment_count()
    {
        var json = """
        {"data":[{"device":"AA:BB","sku":"H6099","deviceName":"Strip","capabilities":[
          {"type":"devices.capabilities.on_off","instance":"powerSwitch"},
          {"type":"devices.capabilities.color_setting","instance":"colorRgb"},
          {"type":"devices.capabilities.segment_color_setting","instance":"segmentedColorRgb","parameters":{"dataType":"STRUCT","fields":[
            {"fieldName":"segment","dataType":"Array","elementRange":{"min":0,"max":9}},
            {"fieldName":"rgb","dataType":"INTEGER"}]}}]}]}
        """;
        var d = Assert.Single(GoveeCloudClient.ParseDevices(json));
        Assert.Equal(10, d.Segments);
        Assert.True(d.CanSetColor);
    }

    [Theory]
    [InlineData("""{"data":[{"device":"A","sku":"H1","capabilities":[]}]}""")]
    [InlineData("""{"data":[{"device":"A","sku":"H1","capabilities":[{"type":"devices.capabilities.segment_color_setting","parameters":{"fields":[{"fieldName":"segment"}]}}]}]}""")]
    [InlineData("""{"data":[{"device":"A","sku":"H1","capabilities":[{"type":"devices.capabilities.segment_color_setting","parameters":{"fields":[{"fieldName":"segment","elementRange":{"max":99999}}]}}]}]}""")]
    [InlineData("""{"data":[{"device":"A","sku":"H1","capabilities":[{"type":"devices.capabilities.segment_color_setting","parameters":{"fields":[{"fieldName":"segment","elementRange":{"max":"many"}}]}}]}]}""")]
    [InlineData("""{"data":[{"device":"A","sku":"H1","capabilities":"nope"}]}""")]
    public void A_missing_or_odd_capability_means_no_segment_count(string json) =>
        Assert.Null(Assert.Single(GoveeCloudClient.ParseDevices(json)).Segments);

    [Fact]
    public void A_known_model_wins_over_the_cloud_range_and_unknown_ones_use_the_cloud()
    {
        Assert.Equal(12, GoveeSegments.ForModel("H6056", 15));
        Assert.Equal(12, GoveeSegments.ForModel("h6056", null));
        Assert.Equal(10, GoveeSegments.ForModel("H6099", 10));
        Assert.Null(GoveeSegments.ForModel("H6099", null));
        Assert.Null(GoveeSegments.ForModel("H6099", 1));
        Assert.Null(GoveeSegments.ForModel(null, 400));
    }

    [Fact]
    public async Task The_manager_applies_what_the_source_detects()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-seg-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var manager = new LightingManager(new LightingStore(path), TimeProvider.System, [new FakeDetector(12)]);
            Assert.Equal(1, manager.SegmentsOf("govee:X"));
            Assert.Equal(12, await manager.DetectSegmentsAsync("govee:X"));
            Assert.Equal(12, manager.SegmentsOf("govee:X"));
            await manager.DisposeAsync();
            var none = new LightingManager(new LightingStore(path), TimeProvider.System, [new FakeDetector(null)]);
            Assert.Null(await none.DetectSegmentsAsync("govee:Y"));
            Assert.Equal(1, none.SegmentsOf("govee:Y"));
            await none.DisposeAsync();
        }
        finally { File.Delete(path); }
    }

    sealed class FakeDetector(int? answer) : IDeviceSource, ISegmentDetector
    {
        public string Name => "fake";
        public string Status => "";
        public Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<IDeviceSink>>([]);
        public void ReportFailure(string sinkId) { }
        public Task<int?> DetectSegmentsAsync(string deviceId, CancellationToken ct) => Task.FromResult(answer);
    }
}

public class KeyPulseTests
{
    [Fact]
    public async Task Key_presses_are_remembered_briefly_and_only_for_keys_on_the_map()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-keys-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var state = new LightingState(true, "k", [new Scene("k", [new Layer("1", "keys", new ReactiveKeysEffect(Color.White, Color.White))], [])]);
            Assert.True(new LightingStore(path).TrySave(state));
            var m = new LightingManager(new LightingStore(path), TimeProvider.System, []);
            Assert.True(m.WantsKeys);
            m.KeyPressed(0x41);
            m.KeyPressed(0xAF);   // not on the map
            for (var i = 0; i < 200; i++) m.KeyPressed(0x42);   // held down and repeating: the list stays small
            await m.DisposeAsync();
            m.SetOn(false);
            Assert.False(m.WantsKeys);
        }
        finally { File.Delete(path); }
    }
}
