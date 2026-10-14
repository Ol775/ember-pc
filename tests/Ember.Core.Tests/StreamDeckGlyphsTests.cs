namespace Ember.Core.Tests;

public class StreamDeckGlyphsTests
{
    static readonly Dictionary<string, string> NoSettings = [];
    static readonly EmberState Live = new(true, new Readouts(63, 1240, 51, 900), FanMode.Quiet, new AppStatus(true, "Cool", ["Cool"], 0.8));

    [Fact]
    public void Every_glyph_has_at_least_one_path()
    {
        Assert.NotEmpty(StreamDeckGlyphs.Names);
        Assert.All(StreamDeckGlyphs.Names, n => Assert.All(StreamDeckGlyphs.Paths(n), p => Assert.False(string.IsNullOrWhiteSpace(p))));
    }

    [Theory]
    [InlineData(StreamDeckActions.Scene, "scene")]
    [InlineData(StreamDeckActions.Power, "bulb")]
    [InlineData(StreamDeckActions.Brightness, "sun")]
    [InlineData(StreamDeckActions.FanMode, "fan")]
    [InlineData(StreamDeckActions.CycleMode, "cycle")]
    [InlineData(StreamDeckActions.Open, "open")]
    public void Each_action_key_shows_its_own_glyph(string action, string glyph)
    {
        var settings = action == StreamDeckActions.FanMode ? new Dictionary<string, string> { ["mode"] = "Quiet" } : NoSettings;
        Assert.Equal(glyph, StreamDeckFaces.For(action, settings, Live).Glyph);
    }

    [Theory]
    [InlineData("cpuTemp", "thermometer")]
    [InlineData("gpuTemp", "thermometer")]
    [InlineData("cpuFan", "fan")]
    [InlineData("gpuFan", "fan")]
    public void Readout_keys_show_a_thermometer_for_temperatures_and_a_fan_for_fan_speeds(string which, string glyph) =>
        Assert.Equal(glyph, StreamDeckFaces.For(StreamDeckActions.Readout, new Dictionary<string, string> { ["value"] = which }, Live).Glyph);

    [Fact]
    public void Every_glyph_a_face_can_ask_for_exists_in_the_catalogue()
    {
        var actions = new[] { StreamDeckActions.Scene, StreamDeckActions.Power, StreamDeckActions.Brightness, StreamDeckActions.FanMode,
            StreamDeckActions.CycleMode, StreamDeckActions.Open, StreamDeckActions.Readout };
        foreach (var a in actions)
            foreach (var state in new[] { Live, new EmberState(false, Readouts.None, null, null) })
                foreach (var which in new[] { "cpuTemp", "cpuFan", "gpuTemp", "gpuFan", "" })
                {
                    var glyph = StreamDeckFaces.For(a, new Dictionary<string, string> { ["value"] = which, ["mode"] = "Quiet" }, state).Glyph;
                    Assert.True(glyph is null || StreamDeckGlyphs.Names.Contains(glyph), $"{a} asks for missing glyph '{glyph}'");
                }
    }

    [Fact]
    public void Svg_draws_the_glyph_as_stroked_paths_in_the_face_colour()
    {
        var svg = StreamDeckFaces.Svg(new KeyFace("Lights", "On", true, FaceTint.Normal, "bulb"));
        Assert.Contains("<path d=", svg);
        Assert.Contains("stroke=\"#1c1c1e\"", svg); // highlighted face: dark ink on Ember orange
    }

    [Fact]
    public void Svg_without_a_glyph_has_no_paths() =>
        Assert.DoesNotContain("<path", StreamDeckFaces.Svg(new KeyFace("Scene", "Cool", false, FaceTint.Normal)));

    [Fact]
    public void Unknown_glyph_name_is_ignored_not_a_crash() =>
        Assert.DoesNotContain("<path", StreamDeckFaces.Svg(new KeyFace("Scene", "Cool", false, FaceTint.Normal, "nope")));
}
