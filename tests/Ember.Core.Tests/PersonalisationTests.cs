namespace Ember.Core.Tests;

public class PersonalisationPrefsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-pers-" + Guid.NewGuid().ToString("N"));
    string PathFor(string name) => Path.Combine(_dir, name);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    AppPreferences LoadText(string json)
    {
        Directory.CreateDirectory(_dir);
        var file = PathFor(Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, json);
        return new PreferencesStore(file).Load();
    }

    [Fact]
    public void Defaults_keep_the_app_as_it_was()
    {
        var d = AppPreferences.Default;
        Assert.Equal(UiSize.Normal, d.Size);
        Assert.Equal("Auto", d.GpuColour);
        Assert.False(d.RememberWindow);
        Assert.Equal("", d.Window);
    }

    [Fact]
    public void New_settings_survive_a_save_and_load()
    {
        var store = new PreferencesStore(PathFor("app.json"));
        var prefs = AppPreferences.Default with { Size = UiSize.Large, GpuColour = "Teal", RememberWindow = true, Window = "10,20,900,700,0", StartPage = StartPage.Lights };
        Assert.True(store.TrySave(prefs));
        Assert.Equal(prefs, store.Load());
    }

    [Theory]
    [InlineData(StartPage.Lights)]
    [InlineData(StartPage.Devices)]
    public void The_new_start_pages_load_back(StartPage page) =>
        Assert.Equal(page, LoadText("{\"StartPage\":\"" + page + "\"}").StartPage);

    [Theory]
    [InlineData("{\"Size\":\"Huge\"}")]
    [InlineData("{\"Size\":7}")]
    [InlineData("{\"Size\":null}")]
    public void A_bad_size_falls_back_to_normal(string json) => Assert.Equal(UiSize.Normal, LoadText(json).Size);

    [Theory]
    [InlineData("Mauve")]
    [InlineData("Custom")]
    [InlineData("")]
    [InlineData("#ff0000")]
    public void A_bad_gpu_colour_falls_back_to_auto(string value) =>
        Assert.Equal("Auto", LoadText("{\"GpuColour\":\"" + value + "\"}").GpuColour);

    [Fact]
    public void A_named_gpu_colour_is_kept() => Assert.Equal("Gold", LoadText("{\"GpuColour\":\"Gold\"}").GpuColour);

    [Theory]
    [InlineData("junk")]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,9")]
    public void A_damaged_window_position_is_dropped(string text) =>
        Assert.Equal("", LoadText("{\"Window\":\"" + text + "\"}").Window);

    [Fact]
    public void One_bad_new_field_keeps_the_others()
    {
        var p = LoadText("{\"Size\":\"Large\",\"GpuColour\":\"Mauve\",\"RememberWindow\":true}");
        Assert.Equal(UiSize.Large, p.Size);
        Assert.Equal("Auto", p.GpuColour);
        Assert.True(p.RememberWindow);
    }
}

public class GpuColourChoiceTests
{
    [Fact]
    public void Auto_is_the_partner_colour_as_before()
    {
        var look = new Look(true, AccentChoice.Blue);
        Assert.Equal(ThemePalette.For(look)["GpuBrush"], ThemePalette.For(look with { Gpu = null })["GpuBrush"]);
    }

    [Fact]
    public void A_chosen_colour_replaces_the_partner()
    {
        var auto = ThemePalette.For(new Look(true, AccentChoice.Blue))["GpuBrush"];
        var chosen = ThemePalette.For(new Look(true, AccentChoice.Blue, Gpu: AccentChoice.Teal))["GpuBrush"];
        Assert.NotEqual(auto, chosen);
    }

    [Fact]
    public void A_chosen_colour_is_that_colour_give_or_take_a_readability_nudge()
    {
        foreach (var gpu in new[] { AccentChoice.Orange, AccentChoice.Teal, AccentChoice.Pink, AccentChoice.Gold })
        {
            var want = ThemePalette.AccentColour(gpu);
            var got = ThemePalette.For(new Look(true, AccentChoice.Blue, Gpu: gpu))["GpuBrush"];
            int Part(string h, int i) => Convert.ToInt32(h.Substring(1 + i * 2, 2), 16);
            var d = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(Part(want, i) - Part(got, i), 2)));
            Assert.True(d < 70, $"{gpu}: wanted {want}, got {got}");
        }
    }

    [Fact]
    public void A_chosen_colour_is_readable_on_a_panel_in_every_theme()
    {
        foreach (var dark in new[] { true, false })
            foreach (var tone in Enum.GetValues<SurfaceTone>())
                foreach (var gpu in Enum.GetValues<AccentChoice>().Where(a => a != AccentChoice.Custom))
                {
                    var p = ThemePalette.For(new Look(dark, AccentChoice.Orange, false, tone, Gpu: gpu));
                    Assert.True(ThemePalette.Contrast(p["GpuBrush"], p["PanelBrush"]) >= 3, $"{(dark ? "dark" : "light")} {tone} {gpu}: {p["GpuBrush"]}");
                }
    }

    [Fact]
    public void Custom_is_not_a_gpu_choice_and_falls_back_to_the_partner()
    {
        var look = new Look(true, AccentChoice.Blue);
        Assert.Equal(ThemePalette.For(look)["GpuBrush"], ThemePalette.For(look with { Gpu = AccentChoice.Custom })["GpuBrush"]);
    }
}

public class UiSizeTests
{
    [Fact]
    public void Sizes_scale_the_interface_in_order()
    {
        Assert.Equal(1.0, UiSizes.Factor(UiSize.Normal));
        Assert.True(UiSizes.Factor(UiSize.Small) < 1.0);
        Assert.True(UiSizes.Factor(UiSize.Large) > 1.0);
        Assert.InRange(UiSizes.Factor(UiSize.Small), 0.8, 0.95);
        Assert.InRange(UiSizes.Factor(UiSize.Large), 1.1, 1.3);
    }
}

public class WindowPlacementTests
{
    [Fact]
    public void Placement_round_trips_through_its_text()
    {
        var p = new WindowPlacement(120.5, 80, 1000, 700, true);
        Assert.True(WindowPlacement.TryParse(p.Format(), out var back));
        Assert.Equal(p, back);
    }

    [Theory]
    [InlineData("")]
    [InlineData("junk")]
    [InlineData("1,2,3,4")]
    [InlineData("1,2,3,4,2")]
    [InlineData("a,b,c,d,0")]
    [InlineData("1,2,-5,400,0")]
    [InlineData("1,2,400,0,0")]
    [InlineData("1,2,99999,400,0")]
    [InlineData("NaN,2,400,400,0")]
    [InlineData("1,2,400,400,0,7")]
    public void Garbage_is_rejected(string text) => Assert.False(WindowPlacement.TryParse(text, out _));

    [Fact]
    public void A_window_on_the_screen_is_left_alone()
    {
        var p = new WindowPlacement(100, 100, 900, 600, false);
        Assert.Equal(p, p.Fit(0, 0, 1920, 1080, 820, 560));
    }

    [Fact]
    public void A_window_left_on_a_monitor_that_is_gone_is_brought_back_in()
    {
        var p = new WindowPlacement(3000, 200, 900, 600, false).Fit(0, 0, 1920, 1080, 820, 560);
        Assert.InRange(p.Left, 0, 1920 - p.Width);
        Assert.InRange(p.Top, 0, 1080 - p.Height);
    }

    [Fact]
    public void A_window_bigger_than_the_screen_is_shrunk_to_fit()
    {
        var p = new WindowPlacement(0, 0, 4000, 3000, false).Fit(0, 0, 1366, 768, 820, 560);
        Assert.True(p.Width <= 1366 && p.Height <= 768);
    }

    [Fact]
    public void A_window_smaller_than_the_minimum_is_raised_to_it()
    {
        var p = new WindowPlacement(10, 10, 300, 200, false).Fit(0, 0, 1920, 1080, 820, 560);
        Assert.Equal(820, p.Width);
        Assert.Equal(560, p.Height);
    }

    [Fact]
    public void Maximised_is_kept()
    {
        Assert.True(new WindowPlacement(10, 10, 900, 600, true).Fit(0, 0, 1920, 1080, 820, 560).Maximised);
    }

    [Fact]
    public void A_screen_with_a_negative_origin_works()
    {
        var p = new WindowPlacement(-1800, 50, 900, 600, false).Fit(-1920, 0, 1920, 1080, 820, 560);
        Assert.InRange(p.Left, -1920, -900);
    }
}
