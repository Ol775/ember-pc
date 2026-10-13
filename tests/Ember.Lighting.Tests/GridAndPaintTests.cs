using Ember.Lighting;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class GridAndPaintTests
{
    static readonly Color Red = new(1, 0, 0), Green = new(0, 1, 0);
    static readonly LightingInputs In = new(0);

    [Fact]
    public void A_grid_device_spreads_its_leds_over_the_whole_rectangle_row_by_row()
    {
        var p = new DevicePlacement("kb", 0.2, 0.4, 0.4, 0.2);
        // 4 columns x 2 rows: LED 0 is top left, LED 5 is row 1 column 1, LED 7 bottom right.
        Assert.Equal((0.25, 0.45), p.LedPosition(0, 8, 4));
        var (x5, y5) = p.LedPosition(5, 8, 4);
        Assert.Equal(0.2 + 1.5 / 4 * 0.4, x5, 6);
        Assert.Equal(0.4 + 1.5 / 2 * 0.2, y5, 6);
        Assert.Equal((0.2 + 3.5 / 4 * 0.4, 0.4 + 1.5 / 2 * 0.2), p.LedPosition(7, 8, 4));
    }

    [Fact]
    public void Without_columns_or_with_a_count_that_does_not_divide_the_leds_stay_in_a_line()
    {
        var p = new DevicePlacement("d", 0, 0, 1, 0.1);
        Assert.Equal(p.LedPosition(2, 5), p.LedPosition(2, 5, 0));
        Assert.Equal(p.LedPosition(2, 5), p.LedPosition(2, 5, 4)); // 5 is not a multiple of 4
    }

    [Fact]
    public void An_effect_that_changes_down_the_canvas_shows_on_the_rows_of_a_keyboard()
    {
        var scene = new Scene("s", [new Layer("a", "a", new WaveEffect(Red, Green, Speed: 0, Wavelength: 4, AngleDegrees: 90))],
            [new DevicePlacement("kb", 0, 0, 1, 1)]);
        var frame = Compositor.Render(scene, new Dictionary<string, int> { ["kb"] = 8 }, In, null, new Dictionary<string, int> { ["kb"] = 4 });
        Assert.NotEqual(frame["kb"][0], frame["kb"][4]); // same column, different row
        Assert.Equal(frame["kb"][0].R, frame["kb"][3].R, 6); // same row, different column
    }

    [Fact]
    public void Painted_leds_keep_their_colour_and_the_rest_follow_the_layers()
    {
        var scene = new Scene("s", [new Layer("a", "a", new StaticEffect(Red))],
            [new DevicePlacement("d", 0, 0, 1, 0.1, Paint: [null, "#00FF00", null])]);
        var leds = Compositor.Render(scene, new Dictionary<string, int> { ["d"] = 4 }, In)["d"];
        Assert.Equal(Red, leds[0]);
        Assert.Equal(Green, leds[1]);
        Assert.Equal(Red, leds[2]);
        Assert.Equal(Red, leds[3]); // beyond the paint list
    }

    [Fact]
    public void Brightness_dims_painted_leds_too()
    {
        var scene = new Scene("s", [], [new DevicePlacement("d", 0, 0, 1, 0.1, Paint: ["#FF0000"])], 0.5);
        Assert.Equal(0.5, Compositor.Render(scene, new Dictionary<string, int> { ["d"] = 1 }, In)["d"][0].R, 3);
    }

    [Fact]
    public void Paint_is_saved_and_a_bad_colour_or_too_long_a_list_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-paint-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new LightingStore(path);
            LightingState With(IReadOnlyList<string?> paint) =>
                new(false, "s", [new Scene("s", [], [new DevicePlacement("d", 0, 0, 1, 0.1, Paint: paint)])]);
            Assert.True(store.TrySave(With([null, "#112233"])));
            Assert.Equal([null, "#112233"], store.Load().Current.Placements[0].Paint);
            Assert.False(store.TrySave(With(["not a colour"])));
            Assert.False(store.TrySave(With(Enumerable.Repeat<string?>("#000000", 1025).ToList())));
        }
        finally { File.Delete(path); }
    }
}
