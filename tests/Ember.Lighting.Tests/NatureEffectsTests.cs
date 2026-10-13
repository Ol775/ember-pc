using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class FireTests
{
    static readonly FireEffect Fire = new(new Color(1, 0.2, 0), new Color(1, 0.9, 0.3));

    [Fact]
    public void Same_moment_gives_the_same_picture()
    {
        var a = Fire.Sample(0.3, 0.7, new LightingInputs(4.2));
        var b = Fire.Sample(0.3, 0.7, new LightingInputs(4.2));
        Assert.Equal(a, b);
    }

    [Fact]
    public void The_flames_move_with_time()
    {
        var seen = new HashSet<string>();
        for (var t = 0.0; t < 3; t += 0.25) seen.Add(Fire.Sample(0.4, 0.8, new LightingInputs(t)).ToHex());
        Assert.True(seen.Count > 4);
    }

    [Fact]
    public void The_bottom_is_hotter_than_the_top()
    {
        double Mean(double y) => Enumerable.Range(0, 200).Average(i =>
        {
            var c = Fire.Sample(i / 200.0, y, new LightingInputs(i * 0.1));
            return c.R + c.G + c.B;
        });
        Assert.True(Mean(0.95) > Mean(0.1) * 2);
    }

    [Fact]
    public void Colours_stay_in_range_and_cover_fully_even_with_wild_settings()
    {
        var wild = new FireEffect(Color.White, Color.White, Speed: -5, Intensity: 99);
        foreach (var t in new[] { -10.0, 0, 1e6 })
        {
            var c = wild.Sample(2, -3, new LightingInputs(t));
            Assert.InRange(c.R, 0, 1); Assert.InRange(c.G, 0, 1); Assert.InRange(c.B, 0, 1);
            Assert.Equal(1, c.A);
            Assert.False(double.IsNaN(c.R));
        }
    }

    [Fact]
    public void A_single_row_of_leds_still_flickers()
    {
        var seen = new HashSet<string>();
        for (var t = 0.0; t < 4; t += 0.3) seen.Add(Fire.Sample(0.5, 0.5, new LightingInputs(t, Local: new LedLocal(0.5, 0.5, 1))).ToHex());
        Assert.True(seen.Count > 3);
    }
}

public class RainTests
{
    static readonly RainEffect Rain = new(new Color(0.2, 0.6, 1), Density: 1);

    [Fact]
    public void Drops_leave_most_of_the_picture_uncovered()
    {
        var covered = Enumerable.Range(0, 20).SelectMany(t => Enumerable.Range(0, 20).Select(i => Rain.Sample(i / 20.0, 0.5, new LightingInputs(t * 0.3)).A)).Average();
        Assert.InRange(covered, 0.01, 0.6);
    }

    [Fact]
    public void Drops_move_down_over_time()
    {
        var found = false;
        for (var i = 0; i < 14 && !found; i++)
        {
            var x = (i + 0.5) / 14;
            var rowsA = Enumerable.Range(0, 100).Where(r => Rain.Sample(x, r / 100.0, new LightingInputs(0.1)).A > 0.9).ToList();
            var rowsB = Enumerable.Range(0, 100).Where(r => Rain.Sample(x, r / 100.0, new LightingInputs(0.2)).A > 0.9).ToList();
            if (rowsA.Count > 0 && rowsB.Count > 0 && rowsB.Max() > rowsA.Max()) found = true;
        }
        Assert.True(found);
    }

    [Fact]
    public void Zero_density_means_no_rain()
    {
        var none = Rain with { Density = 0 };
        Assert.Equal(0, Enumerable.Range(0, 140).Average(i => none.Sample(i / 140.0, 0.5, new LightingInputs(1.3)).A));
    }

    [Fact]
    public void Wild_settings_cannot_break_it()
    {
        var wild = new RainEffect(Color.White, Density: 50, Speed: -1, Length: 0);
        var c = wild.Sample(-4, 9, new LightingInputs(-3));
        Assert.InRange(c.A, 0, 1);
        Assert.False(double.IsNaN(c.A));
    }

    [Fact]
    public void On_one_row_the_drops_run_along_it()
    {
        var local = new LedLocal(0, 0.5, 1);
        var seen = Enumerable.Range(0, 60).Select(i => Rain.Sample(i / 60.0, 0.5, new LightingInputs(1.0, Local: local with { X = i / 60.0 })).A).ToList();
        Assert.Contains(seen, a => a > 0.5);
        Assert.Contains(seen, a => a < 0.05);
    }
}

public class KeyGroupTests
{
    [Theory]
    [InlineData(0.05, 0.05, KeyGroup.FunctionRow)]
    [InlineData(0.05, 0.25, KeyGroup.NumberRow)]
    [InlineData(0.25, 0.5, KeyGroup.Letters)]
    [InlineData(0.3, 0.95, KeyGroup.BottomRow)]
    [InlineData(0.72, 0.5, KeyGroup.Navigation)]
    [InlineData(0.72, 0.95, KeyGroup.Navigation)]
    [InlineData(0.95, 0.3, KeyGroup.Numpad)]
    public void A_spot_belongs_to_the_right_group(double x, double y, KeyGroup expected) => Assert.Equal(expected, KeyGroups.Of(x, y));

    [Fact]
    public void Real_keys_land_in_the_group_a_person_expects()
    {
        KeyGroup Of(int vk) { var p = KeyboardLayout.Position(vk)!.Value; return KeyGroups.Of(p.X, p.Y); }
        Assert.Equal(KeyGroup.FunctionRow, Of(0x70));      // F1
        Assert.Equal(KeyGroup.FunctionRow, Of(0x1B));      // Esc
        Assert.Equal(KeyGroup.NumberRow, Of(0x31));        // 1
        Assert.Equal(KeyGroup.Letters, Of(0x41));          // A
        Assert.Equal(KeyGroup.BottomRow, Of(0x20));        // Space
        Assert.Equal(KeyGroup.Navigation, Of(0x25));       // Left arrow
        Assert.Equal(KeyGroup.Numpad, Of(0x65));           // Numpad 5
    }

    [Fact]
    public void The_inner_effect_shows_only_inside_its_group()
    {
        var e = new KeyGroupEffect(KeyGroup.Numpad, new StaticEffect(new Color(1, 0, 0)));
        Assert.Equal(1, e.Sample(0, 0, new LightingInputs(0, Local: new LedLocal(0.95, 0.3, 6))).A);
        Assert.Equal(0, e.Sample(0, 0, new LightingInputs(0, Local: new LedLocal(0.2, 0.5, 6))).A);
    }

    [Fact]
    public void A_device_that_is_not_a_keyboard_shows_nothing()
    {
        var e = new KeyGroupEffect(KeyGroup.Letters, new StaticEffect(Color.White));
        Assert.Equal(0, e.Sample(0.5, 0.5, new LightingInputs(0)).A);
        Assert.Equal(0, e.Sample(0.5, 0.5, new LightingInputs(0, Local: new LedLocal(0.25, 0.5, 1))).A);
    }

    [Fact]
    public void Every_group_is_reachable_on_a_keyboard_grid()
    {
        var hit = new HashSet<KeyGroup>();
        for (var r = 0; r < 6; r++) for (var c = 0; c < 22; c++) hit.Add(KeyGroups.Of((c + 0.5) / 22, (r + 0.5) / 6));
        Assert.Equal(Enum.GetValues<KeyGroup>().Length, hit.Count);
    }
}

public class NatureEffectsSaveTests
{
    [Fact]
    public void The_new_effects_survive_saving_and_loading_including_the_nested_one()
    {
        var scene = new Scene("n", [
            new Layer("1", "f", new FireEffect(new Color(1, 0.2, 0), new Color(1, 0.9, 0.2), 1.5, 0.7, EffectScope.Canvas)),
            new Layer("2", "r", new RainEffect(new Color(0.2, 0.5, 1), 0.4, 0.8, 0.5, EffectScope.Canvas)),
            new Layer("3", "g", new KeyGroupEffect(KeyGroup.Navigation, new WaveEffect(Color.White, Color.Black))),
        ], []);
        var path = Path.Combine(Path.GetTempPath(), "ember-nat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LightingStore(path);
            Assert.True(store.TrySave(new LightingState(false, "n", [scene])));
            Assert.Equal(scene.Layers.Select(l => l.Effect), store.Load().Current.Layers.Select(l => l.Effect));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_scene_with_groups_nested_inside_groups_does_not_crash_the_loader()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-nest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Effect deep = new StaticEffect(Color.White);
            for (var i = 0; i < 20; i++) deep = new KeyGroupEffect(KeyGroup.Letters, deep);
            var store = new LightingStore(path);
            store.TrySave(new LightingState(false, "d", [new Scene("d", [new Layer("1", "x", deep)], [])]));
            _ = store.Load();   // may load or fall back to defaults, but must not throw
        }
        finally { File.Delete(path); }
    }
}
