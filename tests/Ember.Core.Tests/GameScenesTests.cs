using Ember.Core;
using Xunit;

namespace Ember.Core.Tests;

public class GameScenesTests
{
    static readonly Func<string, bool> Exists = s => s is "Calm" or "Battle" or "Warm glow";
    static GameProfiles On(string game, string scene) => GameProfiles.Default.WithEnabled(true).WithScene(game, scene);

    [Fact]
    public void A_game_with_a_scene_switches_to_it_and_back_when_it_ends()
    {
        var s = new GameScenes();
        Assert.Equal("Battle", s.Start("Halo", On("Halo", "Battle"), "Warm glow", currentOn: false, Exists));
        var back = s.End("Battle", Exists);
        Assert.Equal(("Warm glow", false), back);
        Assert.Null(s.End("Warm glow", Exists));   // nothing more to undo
    }

    [Fact]
    public void Games_without_a_scene_or_with_profiles_off_change_nothing()
    {
        var s = new GameScenes();
        Assert.Null(s.Start("Halo", GameProfiles.Default.WithEnabled(true), "Calm", true, Exists));
        Assert.Null(s.Start("Halo", GameProfiles.Default.WithScene("Halo", "Battle"), "Calm", true, Exists));   // profiles off
        Assert.Null(s.Start("Halo", On("Halo", "Deleted scene"), "Calm", true, Exists));
        Assert.Null(s.End("Calm", Exists));
    }

    [Fact]
    public void A_scene_the_owner_changed_during_the_game_is_left_alone()
    {
        var s = new GameScenes();
        s.Start("Halo", On("Halo", "Battle"), "Calm", true, Exists);
        Assert.Null(s.End("Warm glow", Exists));
    }

    [Fact]
    public void A_second_game_keeps_the_first_snapshot()
    {
        var s = new GameScenes();
        var p = On("Halo", "Battle").WithScene("Doom", "Calm");
        s.Start("Halo", p, "Warm glow", false, Exists);
        s.Start("Doom", p, "Battle", true, Exists);
        Assert.Equal(("Warm glow", false), s.End("Calm", Exists));
    }

    [Fact]
    public void Scenes_are_saved_and_loaded_and_bad_entries_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-gp-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new GameProfileStore(path);
            Assert.True(store.TrySave(On("Halo", "Battle")));
            Assert.Equal("Battle", store.Load().SceneFor("halo"));
            File.WriteAllText(path, "{\"Enabled\":true,\"Scenes\":{\"A\":\"\",\"B\":5,\"C\":\"Calm\"}}");
            var loaded = store.Load();
            Assert.Equal("Calm", loaded.SceneFor("C"));
            Assert.Null(loaded.SceneFor("A"));
            Assert.Null(loaded.SceneFor("B"));
        }
        finally { File.Delete(path); }
    }
}
