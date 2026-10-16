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
        Assert.True(loaded.AutoCheckUpdates); // a file from before the setting existed gets the default, which is on
        Assert.Equal("", loaded.SkippedVersion);
    }

    [Fact]
    public void Ai_settings_default_to_off_and_balanced_and_survive_a_save()
    {
        Assert.False(AppPreferences.Default.AiLearning);
        Assert.Equal(0.35, AppPreferences.Default.AiPreference);
        var store = new PreferencesStore(PathFor("ai.json"));
        var prefs = AppPreferences.Default with { AiLearning = true, AiPreference = 0.8 };
        Assert.True(store.TrySave(prefs));
        Assert.Equal(prefs, store.Load());
    }

    [Theory]
    [InlineData("7", 1.0)]
    [InlineData("-3", 0.0)]
    [InlineData("\"loud\"", 0.35)]
    public void A_wild_ai_preference_is_clamped_or_defaulted(string value, double expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor("p.json"), "{\"AiPreference\":" + value + "}");
        Assert.Equal(expected, new PreferencesStore(PathFor("p.json")).Load().AiPreference);
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

public class AppearancePreferencesTests
{
    [Fact]
    public void The_new_look_choices_round_trip_and_default_to_the_original_look()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-look-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(SurfaceTone.Warm, AppPreferences.Default.Tone);
            Assert.Equal(HighlightStrength.Normal, AppPreferences.Default.Highlight);
            var prefs = AppPreferences.Default with { Theme = ThemeMode.Oled, Tone = SurfaceTone.Cool, Highlight = HighlightStrength.Vivid, Accent = AccentChoice.Custom, CustomAccent = "#1e90ff" };
            var store = new PreferencesStore(path);
            Assert.True(store.TrySave(prefs));
            Assert.Equal(prefs, store.Load());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void A_damaged_custom_colour_in_the_file_loads_as_the_default()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-look-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{\"CustomAccent\":\"red\",\"Tone\":\"Sideways\"}");
            var loaded = new PreferencesStore(path).Load();
            Assert.Equal(ThemePalette.DefaultCustomAccent, loaded.CustomAccent);
            Assert.Equal(SurfaceTone.Warm, loaded.Tone);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class SkippedVersionTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-skip-" + Guid.NewGuid().ToString("N"));
    public SkippedVersionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void A_skipped_version_is_remembered_and_junk_is_dropped()
    {
        var store = new PreferencesStore(Path.Combine(_dir, "a.json"));
        Assert.True(store.TrySave(AppPreferences.Default with { SkippedVersion = "1.1.0-beta.7" }));
        Assert.Equal("1.1.0-beta.7", store.Load().SkippedVersion);
        File.WriteAllText(Path.Combine(_dir, "b.json"), "{\"SkippedVersion\":\"not a version at all\"}");
        Assert.Equal("", new PreferencesStore(Path.Combine(_dir, "b.json")).Load().SkippedVersion);
    }

    [Fact]
    public void Automatic_checks_are_on_for_a_new_install_and_stay_off_when_turned_off()
    {
        Assert.True(AppPreferences.Default.AutoCheckUpdates);
        var store = new PreferencesStore(Path.Combine(_dir, "c.json"));
        Assert.True(store.TrySave(AppPreferences.Default with { AutoCheckUpdates = false }));
        Assert.False(store.Load().AutoCheckUpdates);
    }
}

public class PricePreferenceTests
{
    static AppPreferences RoundTrip(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-prefs-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, json); return new PreferencesStore(path).Load(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Price_is_kept_when_sensible_and_defaulted_otherwise()
    {
        Assert.Equal(0.31, RoundTrip("{\"PricePerKwh\":0.31}").PricePerKwh);
        Assert.Equal(0.28, RoundTrip("{\"PricePerKwh\":-4}").PricePerKwh);
        Assert.Equal(0.28, RoundTrip("{\"PricePerKwh\":1000}").PricePerKwh);
        Assert.Equal(0.28, RoundTrip("{}").PricePerKwh);
    }
}
