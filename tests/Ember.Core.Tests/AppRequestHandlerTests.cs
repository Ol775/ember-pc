namespace Ember.Core.Tests;

public class AppRequestHandlerTests
{
    sealed class FakeLights : IAppLights
    {
        public bool On { get; set; } = true;
        public string CurrentScene { get; set; } = "Warm glow";
        public IReadOnlyList<string> SceneNames { get; set; } = ["Warm glow", "Cool", "Party"];
        public double Brightness { get; set; } = 1;
        public void SelectScene(string name) => CurrentScene = name;
        public void SetOn(bool on) => On = on;
        public void SetBrightness(double value) => Brightness = value;
    }

    static (AppRequestHandler h, FakeLights l, Func<int> shown) Make()
    {
        var l = new FakeLights();
        var shown = 0;
        return (new AppRequestHandler(l, () => shown++), l, () => shown);
    }

    [Fact]
    public void Status_returns_scene_list_and_state()
    {
        var (h, _, _) = Make();
        var reply = h.Handle(new AppStatusRequest());
        Assert.True(reply.Ok);
        Assert.Equal(["Warm glow", "Cool", "Party"], reply.Status!.Scenes);
        Assert.Equal("Warm glow", reply.Status.CurrentScene);
        Assert.True(reply.Status.On);
        Assert.Equal(1, reply.Status.Brightness);
    }

    [Fact]
    public void Select_scene_changes_the_scene_and_returns_the_new_status()
    {
        var (h, l, _) = Make();
        var reply = h.Handle(new SelectSceneRequest("Cool"));
        Assert.True(reply.Ok);
        Assert.Equal("Cool", l.CurrentScene);
        Assert.Equal("Cool", reply.Status!.CurrentScene);
    }

    [Fact]
    public void Select_unknown_scene_is_refused_and_changes_nothing()
    {
        var (h, l, _) = Make();
        var reply = h.Handle(new SelectSceneRequest("Nope"));
        Assert.False(reply.Ok);
        Assert.Equal("That scene is not in Ember.", reply.Error);
        Assert.Equal("Warm glow", l.CurrentScene);
    }

    [Fact]
    public void Set_on_and_toggle()
    {
        var (h, l, _) = Make();
        h.Handle(new SetOnRequest(false));
        Assert.False(l.On);
        h.Handle(new ToggleOnRequest());
        Assert.True(l.On);
        h.Handle(new ToggleOnRequest());
        Assert.False(l.On);
    }

    [Theory]
    [InlineData(0.95, 0.2, 1.0)]
    [InlineData(0.05, -0.2, 0.0)]
    [InlineData(0.5, 0.1, 0.6)]
    public void Brightness_nudge_is_clamped_to_zero_and_one(double start, double delta, double expected)
    {
        var (h, l, _) = Make();
        l.Brightness = start;
        h.Handle(new NudgeBrightnessRequest(delta));
        Assert.Equal(expected, l.Brightness, 3);
    }

    [Fact]
    public void Show_window_calls_the_callback()
    {
        var (h, _, shown) = Make();
        Assert.True(h.Handle(new ShowWindowRequest()).Ok);
        Assert.Equal(1, shown());
    }
}
