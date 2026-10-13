namespace Ember.Core.Tests;

public class AppPreferencesTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-prefs-" + Guid.NewGuid().ToString("N"));
    string PathFor(string name) => Path.Combine(_dir, name);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(AppPreferences.Default, new PreferencesStore(PathFor("none.json")).Load());

    [Fact]
    public void Saved_values_come_back_and_the_folder_is_created()
    {
        var store = new PreferencesStore(PathFor("sub/app.json"));
        var prefs = new AppPreferences(TemperatureUnit.Fahrenheit, StartPage.Fans);
        Assert.True(store.TrySave(prefs));
        Assert.Equal(prefs, store.Load());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"Unit\":7,\"StartPage\":\"Nowhere\"}")]
    [InlineData("{\"Unit\":null}")]
    public void Damaged_file_gives_defaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("bad.json"), content);
        Assert.Equal(AppPreferences.Default, new PreferencesStore(PathFor("bad.json")).Load());
    }

    [Fact]
    public void One_bad_field_keeps_the_other()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("half.json"), "{\"Unit\":\"Fahrenheit\",\"StartPage\":\"Elsewhere\"}");
        var loaded = new PreferencesStore(PathFor("half.json")).Load();
        Assert.Equal(TemperatureUnit.Fahrenheit, loaded.Unit);
        Assert.Equal(StartPage.Dashboard, loaded.StartPage);
    }

    [Fact]
    public void Unwritable_location_reports_false_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("file"), "x"); // a file where a folder is needed
        Assert.False(new PreferencesStore(PathFor("file/app.json")).TrySave(AppPreferences.Default));
    }

    [Fact]
    public void Appearance_and_update_choices_round_trip()
    {
        var store = new PreferencesStore(PathFor("look.json"));
        var prefs = new AppPreferences(TemperatureUnit.Celsius, StartPage.Dashboard, ThemeMode.Light, AccentChoice.Purple, UpdateChannel.Beta, true);
        Assert.True(store.TrySave(prefs));
        Assert.Equal(prefs, store.Load());
    }

    [Fact]
    public void An_old_file_without_the_new_fields_gets_their_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("old.json"), "{\"Unit\":\"Fahrenheit\",\"StartPage\":\"Fans\"}");
        var loaded = new PreferencesStore(PathFor("old.json")).Load();
        Assert.Equal(ThemeMode.Dark, loaded.Theme);
        Assert.Equal(AccentChoice.Orange, loaded.Accent);
        Assert.False(loaded.AutoCheckUpdates);
    }

    [Fact]
    public void Tray_settings_default_to_off_and_survive_a_save()
    {
        Assert.False(AppPreferences.Default.KeepInTray);
        Assert.False(AppPreferences.Default.StartMinimised);
        var store = new PreferencesStore(PathFor("tray.json"));
        var prefs = AppPreferences.Default with { KeepInTray = true, StartMinimised = true };
        Assert.True(store.TrySave(prefs));
        Assert.Equal(prefs, store.Load());
    }
}
