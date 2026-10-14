namespace Ember.Core.Tests;

public class StreamDeckFacesTests
{
    static readonly AppStatus Lights = new(true, "Cool", ["Warm glow", "Cool"], 0.8);

    static EmberState Online(Readouts? r = null, FanMode? mode = FanMode.Quiet, AppStatus? lights = null) =>
        new(true, r ?? new Readouts(63, 1240, 51, 0), mode, lights ?? Lights);

    static readonly EmberState Offline = new(false, Readouts.None, null, null);

    [Fact]
    public void Keys_that_need_the_app_say_offline_when_it_is_not_running()
    {
        foreach (var a in new[] { StreamDeckActions.Scene, StreamDeckActions.Power, StreamDeckActions.Brightness })
        {
            var f = StreamDeckFaces.For(a, new Dictionary<string, string>(), Offline);
            Assert.Equal("offline", f.Value);
            Assert.Equal(FaceTint.Dim, f.Tint);
        }
    }

    [Fact]
    public void Fan_keys_need_the_service_not_the_app()
    {
        var serviceOnly = new EmberState(false, Readouts.None, FanMode.Quiet, null);
        Assert.Equal("Quiet", StreamDeckFaces.For(StreamDeckActions.CycleMode, new Dictionary<string, string>(), serviceOnly).Value);
    }

    [Fact]
    public void Open_key_never_says_offline() =>
        Assert.Equal("Open", StreamDeckFaces.For(StreamDeckActions.Open, new Dictionary<string, string>(), Offline).Value);

    [Fact]
    public void Scene_key_shows_its_scene_and_highlights_when_current()
    {
        var current = StreamDeckFaces.For(StreamDeckActions.Scene, new Dictionary<string, string> { ["scene"] = "Cool" }, Online());
        var other = StreamDeckFaces.For(StreamDeckActions.Scene, new Dictionary<string, string> { ["scene"] = "Warm glow" }, Online());
        Assert.Equal("Cool", current.Value);
        Assert.True(current.Highlight);
        Assert.False(other.Highlight);
    }

    [Fact]
    public void Scene_key_with_no_scene_chosen_asks_for_one() =>
        Assert.Equal("Choose", StreamDeckFaces.For(StreamDeckActions.Scene, new Dictionary<string, string>(), Online()).Value);

    [Fact]
    public void Power_key_shows_state()
    {
        Assert.Equal("On", StreamDeckFaces.For(StreamDeckActions.Power, new Dictionary<string, string>(), Online()).Value);
        var off = StreamDeckFaces.For(StreamDeckActions.Power, new Dictionary<string, string>(), Online(lights: Lights with { On = false }));
        Assert.Equal("Off", off.Value);
        Assert.False(off.Highlight);
    }

    [Fact]
    public void Brightness_key_shows_percentage_and_direction()
    {
        var up = StreamDeckFaces.For(StreamDeckActions.Brightness, new Dictionary<string, string> { ["step"] = "0.1" }, Online());
        var down = StreamDeckFaces.For(StreamDeckActions.Brightness, new Dictionary<string, string> { ["step"] = "-0.1" }, Online());
        Assert.Equal("Brighter", up.Label);
        Assert.Equal("Dimmer", down.Label);
        Assert.Equal("80%", up.Value);
    }

    [Fact]
    public void Fan_mode_key_highlights_when_it_is_the_current_mode()
    {
        var quiet = StreamDeckFaces.For(StreamDeckActions.FanMode, new Dictionary<string, string> { ["mode"] = "Quiet" }, Online());
        var perf = StreamDeckFaces.For(StreamDeckActions.FanMode, new Dictionary<string, string> { ["mode"] = "Performance" }, Online());
        Assert.True(quiet.Highlight);
        Assert.False(perf.Highlight);
        Assert.Equal("Performance", perf.Value);
    }

    [Fact]
    public void Fan_mode_key_with_a_mode_outside_the_list_is_not_offered()
    {
        var f = StreamDeckFaces.For(StreamDeckActions.FanMode, new Dictionary<string, string> { ["mode"] = "Manual" }, Online());
        Assert.Equal("Choose", f.Value);
    }

    [Theory]
    [InlineData("cpuTemp", "CPU", "63°")]
    [InlineData("cpuFan", "CPU FAN", "1,240")]
    [InlineData("gpuTemp", "GPU", "51°")]
    [InlineData("gpuFan", "GPU FAN", "0")]
    public void Readout_keys_show_label_and_value(string which, string label, string value)
    {
        var f = StreamDeckFaces.For(StreamDeckActions.Readout, new Dictionary<string, string> { ["value"] = which }, Online());
        Assert.Equal(label, f.Label);
        Assert.Equal(value, f.Value);
    }

    [Fact]
    public void Readout_with_no_reading_shows_a_dash_not_zero() =>
        Assert.Equal("-", StreamDeckFaces.For(StreamDeckActions.Readout, new Dictionary<string, string> { ["value"] = "gpuTemp" }, Online(new Readouts(63, 1240, null, null))).Value);

    [Theory]
    [InlineData(55, FaceTint.Normal)]
    [InlineData(70, FaceTint.Warm)]
    [InlineData(84.9, FaceTint.Warm)]
    [InlineData(85, FaceTint.Hot)]
    public void Temperature_tint_goes_warm_from_70_and_hot_from_85(double c, FaceTint tint) =>
        Assert.Equal(tint, StreamDeckFaces.For(StreamDeckActions.Readout, new Dictionary<string, string> { ["value"] = "cpuTemp" }, Online(new Readouts(c, null, null, null))).Tint);

    [Fact]
    public void Svg_escapes_text_from_scene_names()
    {
        var svg = StreamDeckFaces.Svg(new KeyFace("Scene", "<b>&\"Evil\"</b>", false, FaceTint.Normal));
        Assert.DoesNotContain("<b>", svg);
        Assert.Contains("&lt;b&gt;&amp;", svg);
        Assert.StartsWith("<svg", svg);
    }

    [Fact]
    public void Long_values_are_shortened_to_fit_the_key()
    {
        var f = StreamDeckFaces.For(StreamDeckActions.Scene, new Dictionary<string, string> { ["scene"] = new string('x', 40) }, Online(lights: Lights with { Scenes = [new string('x', 40)] }));
        Assert.True(f.Value.Length <= 12);
    }
}
