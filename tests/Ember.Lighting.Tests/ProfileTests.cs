using Ember.Lighting;
using Color = Ember.Lighting.Color;

namespace Ember.Lighting.Tests;

public class ProfileTests
{
    static readonly Color Red = new(1, 0, 0), Blue = new(0, 0, 1);
    static readonly LightingInputs In = new(0, null);
    static readonly Dictionary<string, int> Counts = new() { ["kb"] = 3, ["fan"] = 3 };

    static Scene Solid(string name, Color c, params DevicePlacement[] placements) =>
        new(name, [new Layer("a", "a", new StaticEffect(c))], placements);

    [Fact]
    public void A_device_with_its_own_profile_shows_that_scenes_layers_while_the_others_follow_the_scene()
    {
        var blue = Solid("Blue keys", Blue);
        var main = Solid("Main", Red, new DevicePlacement("kb", 0, 0, 1, 0.1, Profile: "Blue keys"), new DevicePlacement("fan", 0, 0.5, 1, 0.1));
        var frame = Compositor.Render(main, Counts, In, [main, blue]);
        Assert.All(frame["kb"], c => Assert.Equal(Blue, c));
        Assert.All(frame["fan"], c => Assert.Equal(Red, c));
    }

    [Fact]
    public void A_profile_that_no_longer_exists_falls_back_to_the_scene()
    {
        var main = Solid("Main", Red, new DevicePlacement("kb", 0, 0, 1, 0.1, Profile: "Deleted"));
        Assert.All(Compositor.Render(main, Counts, In, [main])["kb"], c => Assert.Equal(Red, c));
    }

    [Fact]
    public void Without_the_scene_list_a_profile_is_ignored()
    {
        var main = Solid("Main", Red, new DevicePlacement("kb", 0, 0, 1, 0.1, Profile: "Blue keys"));
        Assert.All(Compositor.Render(main, Counts, In)["kb"], c => Assert.Equal(Red, c));
    }

    [Fact]
    public void A_profile_that_names_the_current_scene_changes_nothing()
    {
        var main = Solid("Main", Red, new DevicePlacement("kb", 0, 0, 1, 0.1, Profile: "Main"));
        Assert.All(Compositor.Render(main, Counts, In, [main])["kb"], c => Assert.Equal(Red, c));
    }

    [Fact]
    public void The_profile_is_saved_with_the_scene()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var main = Solid("Main", Red, new DevicePlacement("kb", 0, 0, 1, 0.1, true, "Blue keys"));
            var state = new LightingState(false, "Main", [main, Solid("Blue keys", Blue)]);
            var store = new LightingStore(path);
            Assert.True(store.TrySave(state));
            Assert.Equal("Blue keys", store.Load().Current.Placements[0].Profile);
            Assert.False(store.TrySave(state with { Scenes = [main with { Placements = [new DevicePlacement("kb", 0, 0, 1, 0.1, true, new string('x', 61))] }] }));
        }
        finally { File.Delete(path); }
    }
}
