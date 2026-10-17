using Ember.Lighting;

namespace Ember.Lighting.Tests;

public class SceneFileTests
{
    static Scene Sample() => new("Sunset",
        [new Layer("a", "Sky", new StaticEffect(new Color(1, 0.4, 0)), Targets: ["openrgb:MSI|HID:secret-path"]),
         new Layer("b", "Glow", new GradientEffect([new(0, new Color(1, 0, 0)), new(1, new Color(0, 0, 1))], 0.2, 0.7, GradientDirection.Right))],
        [new DevicePlacement("govee:76:6A:D6:F4:04:C6:63:35", 0.1, 0.1, 0.5, 0.2)], 0.8);

    [Fact]
    public void A_scene_survives_a_round_trip_without_naming_any_device()
    {
        var text = SceneFile.Export(Sample());
        Assert.DoesNotContain("govee:", text);
        Assert.DoesNotContain("secret-path", text);
        var back = SceneFile.TryImport(text)!;
        Assert.Equal("Sunset", back.Name);
        Assert.Equal(2, back.Layers.Count);
        Assert.IsType<GradientEffect>(back.Layers[1].Effect);
        Assert.Empty(back.Placements);
        Assert.All(back.Layers, l => Assert.Null(l.Targets));
        Assert.Equal(0.8, back.Brightness);
    }

    [Fact]
    public void Anything_else_is_refused()
    {
        Assert.Null(SceneFile.TryImport("not json"));
        Assert.Null(SceneFile.TryImport("{}"));
        Assert.Null(SceneFile.TryImport("{\"Kind\":\"other\",\"Scene\":{}}"));
        Assert.Null(SceneFile.TryImport(new string('x', SceneFile.MaxBytes + 1)));
        var badOpacity = SceneFile.Export(Sample()).Replace("\"Opacity\": 1", "\"Opacity\": 7");
        Assert.Null(SceneFile.TryImport(badOpacity));
    }

    [Fact]
    public void An_imported_scene_gets_a_unique_name_and_this_pcs_layout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ember-scenefile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var manager = new LightingManager(new LightingStore(Path.Combine(dir, "lighting.json")), TimeProvider.System, []);
            var own = manager.State.Current.Placements;
            var scene = SceneFile.TryImport(SceneFile.Export(Sample()))!;
            Assert.True(manager.ImportScene(scene));
            Assert.True(manager.ImportScene(scene));
            Assert.Equal(["Warm glow", "Sunset", "Sunset (2)"], manager.State.Scenes.Select(s => s.Name));
            Assert.Equal("Sunset (2)", manager.State.CurrentScene);
            Assert.Equal(own, manager.State.Current.Placements);
        }
        finally { Directory.Delete(dir, true); }
    }
}
