namespace Ember.Core.Tests;

public class ThemePaletteTests
{
    static readonly string[] Customs = ["#ff7a2f", "#ffffff", "#000000", "#ffee00", "#101040", "#808080", "#00ffff"];

    public static IEnumerable<object[]> All() =>
        from dark in new[] { true, false }
        from oled in new[] { false, true }
        from tone in Enum.GetValues<SurfaceTone>()
        from strength in Enum.GetValues<HighlightStrength>()
        from accent in Enum.GetValues<AccentChoice>()
        where dark || !oled
        select new object[] { dark, oled, tone, strength, accent };

    static Look Make(bool dark, bool oled, SurfaceTone tone, HighlightStrength strength, AccentChoice accent, string? custom = "#ffee00") =>
        new(dark, accent, oled, tone, strength, custom);

    [Theory, MemberData(nameof(All))]
    public void Text_is_readable_on_every_surface(bool dark, bool oled, SurfaceTone tone, HighlightStrength strength, AccentChoice accent)
    {
        var p = ThemePalette.For(Make(dark, oled, tone, strength, accent));
        foreach (var surface in new[] { "WindowBrush", "SidebarBrush", "PanelBrush" })
        {
            Assert.True(ThemePalette.Contrast(p["TextBrush"], p[surface]) >= 7, $"text on {surface}");
            Assert.True(ThemePalette.Contrast(p["MutedBrush"], p[surface]) >= 4.5, $"muted text on {surface}");
        }
        Assert.True(ThemePalette.Contrast(p["TextBrush"], p["SegmentOnBrush"]) >= 7);
        Assert.True(ThemePalette.Contrast(p["TextBrush"], p["ButtonBrush"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["TextBrush"], p["BannerBrush"]) >= 4.5, "text on a banner");
    }

    [Theory, MemberData(nameof(All))]
    public void The_active_menu_item_and_the_accent_are_readable(bool dark, bool oled, SurfaceTone tone, HighlightStrength strength, AccentChoice accent)
    {
        var p = ThemePalette.For(Make(dark, oled, tone, strength, accent));
        Assert.True(ThemePalette.Contrast(p["AccentTextBrush"], p["AccentSoftBrush"]) >= 4.5, "active nav text");
        Assert.True(ThemePalette.Contrast(p["AccentBrush"], p["PanelBrush"]) >= 3, "accent on a panel (large text and lines)");
        Assert.NotEqual(p["AccentBrush"], p["GpuBrush"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Any_custom_colour_ends_up_readable(bool dark)
    {
        foreach (var c in Customs)
            foreach (var strength in Enum.GetValues<HighlightStrength>())
            {
                var p = ThemePalette.For(new Look(dark, AccentChoice.Custom, dark, SurfaceTone.Warm, strength, c));
                Assert.True(ThemePalette.Contrast(p["AccentBrush"], p["PanelBrush"]) >= 3, $"accent {c} on a panel");
                Assert.True(ThemePalette.Contrast(p["AccentTextBrush"], p["AccentSoftBrush"]) >= 4.5, $"accent text {c}");
                Assert.True(ThemePalette.Contrast(p["TextBrush"], p["BannerBrush"]) >= 4.5, $"banner {c}");
            }
    }

    [Fact]
    public void Dark_orange_keeps_the_original_colours()
    {
        var p = ThemePalette.For(true, AccentChoice.Orange);
        Assert.Equal("#ff7a2f", p["AccentBrush"]);
        Assert.Equal("#1d1916", p["PanelBrush"]);
        Assert.Equal("#141110", p["WindowBrush"]);
    }

    [Fact]
    public void Oled_is_pure_black_behind_everything_and_stays_dark()
    {
        var p = ThemePalette.For(new Look(true, Oled: true));
        Assert.Equal("#000000", p["WindowBrush"]);
        Assert.True(ThemePalette.Contrast(p["PanelBrush"], "#000000") < 1.3, "panels stay close to black");
        Assert.NotEqual(p["PanelBrush"], p["PanelBorderBrush"]);
        Assert.Equal(ThemePalette.For(true, AccentChoice.Orange)["TextBrush"], p["TextBrush"]);
    }

    [Fact]
    public void Oled_means_nothing_on_a_light_theme()
    {
        Assert.Equal(ThemePalette.For(new Look(false)), ThemePalette.For(new Look(false, Oled: true)));
    }

    [Fact]
    public void A_stronger_highlight_gives_a_stronger_fill()
    {
        string Soft(HighlightStrength h) => ThemePalette.For(new Look(true, Highlight: h))["AccentSoftBrush"];
        double Lum(string c) => ThemePalette.Contrast(c, "#000000");
        Assert.True(Lum(Soft(HighlightStrength.Subtle)) < Lum(Soft(HighlightStrength.Normal)));
        Assert.True(Lum(Soft(HighlightStrength.Normal)) < Lum(Soft(HighlightStrength.Vivid)));
    }

    [Fact]
    public void The_tones_differ_but_only_in_the_surfaces()
    {
        var warm = ThemePalette.For(new Look(true, Tone: SurfaceTone.Warm));
        var cool = ThemePalette.For(new Look(true, Tone: SurfaceTone.Cool));
        Assert.NotEqual(warm["PanelBrush"], cool["PanelBrush"]);
        Assert.Equal(warm["AccentBrush"], cool["AccentBrush"]);
    }

    [Theory]
    [InlineData("#abcdef", true)]
    [InlineData("#ABCDEF", true)]
    [InlineData("abcdef", false)]
    [InlineData("#abcde", false)]
    [InlineData("#abcdeg", false)]
    [InlineData(null, false)]
    public void Only_hex_colours_are_accepted(string? text, bool ok) => Assert.Equal(ok, ThemePalette.IsHexColour(text));

    [Fact]
    public void A_bad_custom_colour_falls_back_to_the_default()
    {
        var p = ThemePalette.For(new Look(true, AccentChoice.Custom, CustomAccent: "red; DROP"));
        Assert.Equal(ThemePalette.For(true, AccentChoice.Orange)["AccentBrush"], p["AccentBrush"]);
    }

    [Fact]
    public void Contrast_of_black_on_white_is_21() => Assert.Equal(21, ThemePalette.Contrast("#000000", "#ffffff"), 3);
}

public class GpuColourTests
{
    static readonly string[] Customs = ["#ff7a2f", "#ffffff", "#000000", "#ffee00", "#101040", "#808080", "#00ffff", "#3b8ef0", "#14b8a6"];

    static IEnumerable<(string Name, Look Look)> Looks()
    {
        foreach (var dark in new[] { true, false })
            foreach (var oled in dark ? new[] { false, true } : new[] { false })
                foreach (var tone in Enum.GetValues<SurfaceTone>())
                {
                    foreach (var a in Enum.GetValues<AccentChoice>().Where(a => a != AccentChoice.Custom))
                        yield return ($"{(dark ? "dark" : "light")} {a} {tone}", new Look(dark, a, oled, tone));
                    foreach (var c in Customs)
                        yield return ($"{(dark ? "dark" : "light")} custom {c} {tone}", new Look(dark, AccentChoice.Custom, oled, tone, HighlightStrength.Normal, c));
                }
    }

    static (int R, int G, int B) Rgb(string hex) => (Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16));

    static double Hue(string hex)
    {
        var (r, g, b) = Rgb(hex);
        double R = r / 255.0, G = g / 255.0, B = b / 255.0, max = Math.Max(R, Math.Max(G, B)), d = max - Math.Min(R, Math.Min(G, B));
        if (d == 0) return 0;
        var h = max == R ? ((G - B) / d) % 6 : max == G ? (B - R) / d + 2 : (R - G) / d + 4;
        return (h * 60 + 360) % 360;
    }

    [Fact]
    public void The_gpu_colour_is_never_the_fixed_brand_orange()
    {
        foreach (var (name, look) in Looks())
            Assert.NotEqual("#ff7a2f", ThemePalette.For(look)["GpuBrush"]);
    }

    [Fact]
    public void The_gpu_colour_stays_clearly_different_from_the_accent()
    {
        foreach (var (name, look) in Looks())
        {
            var p = ThemePalette.For(look);
            var (a, g) = (Rgb(p["AccentBrush"]), Rgb(p["GpuBrush"]));
            var distance = Math.Sqrt(Math.Pow(a.R - g.R, 2) + Math.Pow(a.G - g.G, 2) + Math.Pow(a.B - g.B, 2));
            Assert.True(distance >= 90, $"{name}: accent {p["AccentBrush"]} and GPU {p["GpuBrush"]} are too alike ({distance:F0})");
        }
    }

    [Fact]
    public void The_gpu_line_is_readable_on_a_panel_in_every_theme()
    {
        foreach (var (name, look) in Looks())
        {
            var p = ThemePalette.For(look);
            Assert.True(ThemePalette.Contrast(p["GpuBrush"], p["PanelBrush"]) >= 3, $"{name}: GPU {p["GpuBrush"]} on panel {p["PanelBrush"]}");
        }
    }

    [Fact]
    public void The_default_orange_theme_keeps_its_blue_gpu_line()
    {
        var hue = Hue(ThemePalette.For(new Look(true, AccentChoice.Orange))["GpuBrush"]);
        Assert.InRange(hue, 190, 230);
    }

    [Fact]
    public void The_gpu_colour_follows_the_accent()
    {
        var blue = ThemePalette.For(new Look(true, AccentChoice.Blue))["GpuBrush"];
        var green = ThemePalette.For(new Look(true, AccentChoice.Green))["GpuBrush"];
        var purple = ThemePalette.For(new Look(true, AccentChoice.Purple))["GpuBrush"];
        Assert.Equal(3, new[] { blue, green, purple }.Distinct().Count());
    }

    [Fact]
    public void The_gpu_colour_follows_light_and_dark()
    {
        foreach (var a in new[] { AccentChoice.Blue, AccentChoice.Purple, AccentChoice.Teal })
            Assert.NotEqual(ThemePalette.For(new Look(true, a))["GpuBrush"], ThemePalette.For(new Look(false, a))["GpuBrush"]);
    }

    [Fact]
    public void A_partner_colour_is_not_a_warm_orange_when_the_accent_is_cool()
    {
        foreach (var a in new[] { AccentChoice.Blue, AccentChoice.Purple, AccentChoice.Teal })
            foreach (var dark in new[] { true, false })
            {
                var hue = Hue(ThemePalette.For(new Look(dark, a))["GpuBrush"]);
                Assert.False(hue is >= 10 and <= 50, $"{a} {(dark ? "dark" : "light")} gives an orange GPU (hue {hue:F0})");
            }
    }
}
