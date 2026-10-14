using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core.Tests;

public class PluginManifestTests
{
    static string PluginDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ember.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Ember.StreamDeck", "plugin");
    }

    static JsonElement Manifest() => JsonDocument.Parse(File.ReadAllText(Path.Combine(PluginDir(), "manifest.json"))).RootElement.Clone();

    [Fact]
    public void Every_action_uuid_starts_with_the_prefix_and_matches_the_code()
    {
        var uuids = Manifest().GetProperty("Actions").EnumerateArray().Select(a => a.GetProperty("UUID").GetString()!).ToList();
        Assert.All(uuids, u => Assert.StartsWith(StreamDeckActions.Prefix, u));
        string[] expected = [StreamDeckActions.Scene, StreamDeckActions.Power, StreamDeckActions.Brightness, StreamDeckActions.FanMode,
            StreamDeckActions.CycleMode, StreamDeckActions.Open, StreamDeckActions.Readout];
        Assert.Equal(expected.OrderBy(x => x), uuids.OrderBy(x => x));
    }

    [Fact]
    public void Code_path_points_at_the_plugin_exe() =>
        Assert.Equal("EmberStreamDeck.exe", Manifest().GetProperty("CodePath").GetString());

    [Fact]
    public void Plugin_and_actions_say_beta_in_their_names()
    {
        Assert.Contains("(beta)", Manifest().GetProperty("Name").GetString());
        Assert.Contains("(beta)", Manifest().GetProperty("Category").GetString());
    }

    [Fact]
    public void Version_is_four_numeric_parts() =>
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+\.\d+$"), Manifest().GetProperty("Version").GetString()!);

    [Fact]
    public void Every_icon_the_manifest_names_exists_as_a_png()
    {
        var m = Manifest();
        var icons = new List<string> { m.GetProperty("Icon").GetString()!, m.GetProperty("CategoryIcon").GetString()! };
        foreach (var a in m.GetProperty("Actions").EnumerateArray())
        {
            icons.Add(a.GetProperty("Icon").GetString()!);
            foreach (var s in a.GetProperty("States").EnumerateArray()) icons.Add(s.GetProperty("Image").GetString()!);
        }
        foreach (var icon in icons.Distinct())
        {
            Assert.True(File.Exists(Path.Combine(PluginDir(), icon + ".png")), icon + ".png is missing");
            Assert.True(File.Exists(Path.Combine(PluginDir(), icon + "@2x.png")), icon + "@2x.png is missing");
        }
    }

    [Fact]
    public void Property_inspector_page_exists() =>
        Assert.True(File.Exists(Path.Combine(PluginDir(), Manifest().GetProperty("PropertyInspectorPath").GetString()!)));

    [Fact]
    public void Plugin_runs_on_windows_only_and_every_action_is_a_key()
    {
        Assert.Equal("windows", Manifest().GetProperty("OS")[0].GetProperty("Platform").GetString());
        Assert.All(Manifest().GetProperty("Actions").EnumerateArray(), a => Assert.Equal("Keypad", a.GetProperty("Controllers")[0].GetString()));
    }
}
