namespace Ember.Core.Tests;

public class ThemePaletteTests
{
    public static IEnumerable<object[]> All() =>
        from dark in new[] { true, false }
        from accent in Enum.GetValues<AccentChoice>()
        select new object[] { dark, accent };

    [Theory, MemberData(nameof(All))]
    public void Text_is_readable_on_every_surface(bool dark, AccentChoice accent)
    {
        var p = ThemePalette.For(dark, accent);
        foreach (var surface in new[] { "WindowBrush", "SidebarBrush", "PanelBrush" })
        {
            Assert.True(ThemePalette.Contrast(p["TextBrush"], p[surface]) >= 7, $"text on {surface}");
            Assert.True(ThemePalette.Contrast(p["MutedBrush"], p[surface]) >= 4.5, $"muted text on {surface}");
        }
        Assert.True(ThemePalette.Contrast(p["TextBrush"], p["SegmentOnBrush"]) >= 7);
        Assert.True(ThemePalette.Contrast(p["TextBrush"], p["ButtonBrush"]) >= 4.5);
    }

    [Theory, MemberData(nameof(All))]
    public void The_active_menu_item_and_the_accent_are_readable(bool dark, AccentChoice accent)
    {
        var p = ThemePalette.For(dark, accent);
        Assert.True(ThemePalette.Contrast(p["AccentTextBrush"], p["AccentSoftBrush"]) >= 4.5, "active nav text");
        Assert.True(ThemePalette.Contrast(p["AccentBrush"], p["PanelBrush"]) >= 3, "accent on a panel (large text and lines)");
        Assert.NotEqual(p["AccentBrush"], p["GpuBrush"]);
    }

    [Fact]
    public void Dark_orange_keeps_the_original_colours()
    {
        var p = ThemePalette.For(true, AccentChoice.Orange);
        Assert.Equal("#ff7a2f", p["AccentBrush"]);
        Assert.Equal("#1d1916", p["PanelBrush"]);
    }

    [Fact]
    public void Contrast_of_black_on_white_is_21() => Assert.Equal(21, ThemePalette.Contrast("#000000", "#ffffff"), 3);
}
