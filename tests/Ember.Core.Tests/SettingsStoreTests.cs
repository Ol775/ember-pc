namespace Ember.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-settings-" + Guid.NewGuid().ToString("N"));
    private string PathOf => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void Missing_file_gives_defaults() => Assert.Equal(EmberSettings.Default, new SettingsStore(PathOf).Load());

    [Fact]
    public void Default_values()
    {
        var d = EmberSettings.Default;
        Assert.Equal(FanMode.Normal, d.NormalMode);
        Assert.Equal(FanMode.Performance, d.GameMode);
        Assert.Empty(d.NotGames);
        Assert.Empty(d.Headers);
        Assert.False(d.GpuControlEnabled);
    }

    [Fact]
    public void Round_trip_preserves_values()
    {
        var s = new EmberSettings(FanMode.Quiet, FanMode.Extreme, new[] { "chrome.exe" },
            new[] { new HeaderRecord("/lpc/nct6687d/0/control/2", true, "Case", "MS-7C56", "0.9.6", 60) }, true);
        var store = new SettingsStore(PathOf);
        store.Save(s);
        var back = store.Load();
        Assert.Equal(s.NormalMode, back.NormalMode);
        Assert.Equal(s.GameMode, back.GameMode);
        Assert.Equal(s.NotGames, back.NotGames);
        Assert.Equal(s.Headers, back.Headers);
        Assert.True(back.GpuControlEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"NormalMode\":\"Quie")]
    [InlineData("{\"NormalMode\":\"Turbo\"}")]
    [InlineData("{\"NormalMode\":1}")]
    [InlineData("{\"NormalMode\":\"Ai\"}")]
    [InlineData("{\"GameMode\":\"Ai\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Bad_content_gives_defaults(string content)
    {
        File.WriteAllText(PathOf, content);
        Assert.Equal(EmberSettings.Default, new SettingsStore(PathOf).Load());
    }

    [Fact]
    public void NotGames_lowercased_and_deduplicated()
    {
        var store = new SettingsStore(PathOf);
        store.Save(EmberSettings.Default with { NotGames = new[] { "Chrome.EXE", "chrome.exe", "VLC.exe" } });
        Assert.Equal(new[] { "chrome.exe", "vlc.exe" }, store.Load().NotGames);
    }

    [Fact]
    public void Missing_fields_fall_back_one_by_one()
    {
        File.WriteAllText(PathOf, "{\"NormalMode\":\"Quiet\"}");
        var s = new SettingsStore(PathOf).Load();
        Assert.Equal(FanMode.Quiet, s.NormalMode);
        Assert.Equal(FanMode.Performance, s.GameMode);
        Assert.Empty(s.Headers);
        File.WriteAllText(PathOf, "{}");
        Assert.Equal(EmberSettings.Default, new SettingsStore(PathOf).Load());
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        new SettingsStore(PathOf).Save(EmberSettings.Default);
        Assert.Equal(new[] { PathOf }, Directory.GetFiles(_dir));
    }

    [Fact]
    public void Oversized_file_gives_defaults()
    {
        File.WriteAllText(PathOf, new string(' ', 1_048_577));
        Assert.Equal(EmberSettings.Default, new SettingsStore(PathOf).Load());
    }
}
