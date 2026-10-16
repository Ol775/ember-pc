using Ember.Lighting;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class GradientTests
{
    static readonly Color Red = new(1, 0, 0), Blue = new(0, 0, 1);
    static GradientEffect Two(double soft = 1, GradientDirection d = GradientDirection.Right) => new([new(0, Red), new(1, Blue)], 0, soft, d);

    [Fact]
    public void Colours_follow_the_stops_and_the_softness()
    {
        Assert.Equal(Red, Two().At(0));
        Assert.Equal(Blue, Two().At(1));
        Assert.Equal(0.5, Two().At(0.5).R, 3);
        Assert.Equal(Red, Two(soft: 0).At(0.3));   // hard edge: the nearer stop only
        Assert.Equal(Blue, Two(soft: 0).At(0.7));
    }

    [Fact]
    public void Direction_decides_where_the_start_sits()
    {
        var inputs = new LightingInputs(0);
        Assert.Equal(Red, Two().Sample(0, 0.5, inputs));
        Assert.Equal(Blue, Two(d: GradientDirection.Left).Sample(0, 0.5, inputs));
        Assert.Equal(Red, Two(d: GradientDirection.Down).Sample(0.5, 0, inputs));
        Assert.Equal(Blue, Two(d: GradientDirection.Up).Sample(0.5, 0, inputs));
        Assert.Equal(Red, Two(d: GradientDirection.Out).Sample(0.5, 0.5, inputs));
    }

    [Fact]
    public void A_layer_with_targets_is_drawn_only_on_those_devices()
    {
        var layer = new Layer("a", "a", new StaticEffect(Red), Targets: ["one"]);
        var scene = new Scene("t", [layer], [new DevicePlacement("one", 0, 0, 1, 0.1), new DevicePlacement("two", 0, 0.5, 1, 0.1)]);
        var frame = Compositor.Render(scene, new Dictionary<string, int> { ["one"] = 2, ["two"] = 2 }, new LightingInputs(0));
        Assert.All(frame["one"], c => Assert.Equal(Red, c));
        Assert.All(frame["two"], c => Assert.Equal(Color.Black, c));
    }

    [Fact]
    public void Gradients_and_targets_survive_saving()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ember-grad-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LightingStore(Path.Combine(dir, "l.json"));
            var scene = new Scene("S", [new Layer("g", "g", Two(0.4, GradientDirection.Out), 0.8, Targets: ["x"])], [new DevicePlacement("x", 0, 0, 1, 0.1)]);
            store.TrySave(LightingState.Default with { Scenes = [scene], CurrentScene = "S" });
            var back = store.Load().Current.Layers[0];
            Assert.Equal(["x"], back.Targets);
            var g = Assert.IsType<GradientEffect>(back.Effect);
            Assert.Equal(GradientDirection.Out, g.Direction);
            Assert.Equal(2, g.Stops.Count);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

public class GradientValidationTests
{
    static LightingState With(Layer layer) => LightingState.Default with { Scenes = [new Scene("S", [layer], []) ], CurrentScene = "S" };
    static readonly Color Red = new(1, 0, 0);

    [Fact]
    public void A_sensible_gradient_and_targets_are_valid()
    {
        Assert.True(LightingState.IsValid(With(new Layer("a", "a", GradientEffect.Default, Targets: ["x"]))));
    }

    [Theory]
    [InlineData(double.NaN, 0.5, 0.5)]
    [InlineData(0.1, double.PositiveInfinity, 0.5)]
    [InlineData(0.1, 0.5, 4)]
    [InlineData(99, 0.5, 0.5)]
    public void Bad_numbers_in_a_gradient_are_refused(double speed, double softness, double stopAt)
    {
        var bad = new GradientEffect([new ColorStop(stopAt > 1 ? stopAt : 0.2, Red)], speed, softness);
        Assert.False(LightingState.IsValid(With(new Layer("a", "a", bad))));
    }

    [Fact]
    public void Too_many_stops_or_targets_are_refused()
    {
        var many = new GradientEffect(Enumerable.Range(0, 9).Select(i => new ColorStop(i / 10.0, Red)).ToList());
        Assert.False(LightingState.IsValid(With(new Layer("a", "a", many))));
        Assert.False(LightingState.IsValid(With(new Layer("a", "a", GradientEffect.Default, Targets: Enumerable.Range(0, 101).Select(i => "d" + i).ToList()))));
        Assert.False(LightingState.IsValid(With(new Layer("a", "a", GradientEffect.Default, Targets: [new string('x', 401)]))));
    }
}
