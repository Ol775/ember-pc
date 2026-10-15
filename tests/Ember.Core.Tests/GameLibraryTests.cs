using Ember.Core;

namespace Ember.Core.Tests;

public class GameLibraryTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "ember-games-" + Path.GetRandomFileName());

    public GameLibraryTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    string Make(string rel, int size = 10)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    [Fact]
    public void Steam_games_are_read_from_every_library_folder()
    {
        var second = Path.Combine(_root, "Second");
        Make(@"Steam\steamapps\common\Big Game\bg.exe", 500);
        Make(@"Steam\steamapps\common\Big Game\unins000.exe");
        Make(@"Second\steamapps\common\Other\other.exe");
        Make(@"Steam\steamapps\common\Steamworks Shared\x.exe");
        File.WriteAllText(Make(@"Steam\steamapps\appmanifest_1.acf"), "\"AppState\"\n{\n\t\"name\"\t\t\"Big Game\"\n\t\"installdir\"\t\t\"Big Game\"\n}");
        File.WriteAllText(Make(@"Second\steamapps\appmanifest_2.acf"), "\"AppState\"\n{\n\t\"name\"\t\t\"Other\"\n\t\"installdir\"\t\t\"Other\"\n}");
        File.WriteAllText(Make(@"Steam\steamapps\appmanifest_3.acf"), "\"AppState\"\n{\n\t\"name\"\t\t\"Steamworks Shared\"\n\t\"installdir\"\t\t\"Steamworks Shared\"\n}");
        File.WriteAllText(Make(@"Steam\steamapps\libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + second.Replace(@"\", @"\\") + "\"\n\t}\n}");

        var games = GameScanner.ScanSteam(Path.Combine(_root, "Steam"));

        Assert.Equal(["Big Game", "Other"], games.Select(g => g.Name).OrderBy(n => n));
        Assert.Equal(["bg.exe"], games.Single(g => g.Name == "Big Game").Exes);   // the uninstaller is not the game
        Assert.All(games, g => Assert.Equal("Steam", g.Launcher));
    }

    [Fact]
    public void Epic_games_are_read_from_manifests_and_tools_are_skipped()
    {
        Make(@"Epic\Fortnite\Bin\fn.exe");
        Make(@"Epic\Tool\tool.exe");
        var manifests = Path.Combine(_root, "Manifests");
        Directory.CreateDirectory(manifests);
        File.WriteAllText(Path.Combine(manifests, "a.item"), $$"""{"DisplayName":"Fortnite","InstallLocation":{{System.Text.Json.JsonSerializer.Serialize(Path.Combine(_root, "Epic", "Fortnite"))}},"LaunchExecutable":"Bin\\fn.exe","AppCategories":["public","games"]}""");
        File.WriteAllText(Path.Combine(manifests, "b.item"), $$"""{"DisplayName":"Tool","InstallLocation":{{System.Text.Json.JsonSerializer.Serialize(Path.Combine(_root, "Epic", "Tool"))}},"AppCategories":["tools"]}""");
        File.WriteAllText(Path.Combine(manifests, "c.item"), "not json");

        var games = GameScanner.ScanEpic(manifests);

        var g = Assert.Single(games);
        Assert.Equal("Fortnite", g.Name);
        Assert.Contains("fn.exe", g.Exes);
    }

    [Fact]
    public void A_missing_folder_or_launcher_is_no_games_not_an_error()
    {
        Assert.Empty(GameScanner.ScanSteam(Path.Combine(_root, "nope")));
        Assert.Empty(GameScanner.ScanEpic(Path.Combine(_root, "nope")));
        Assert.Null(GameScanner.FromFolder("x", "GOG", Path.Combine(_root, "nope")));
    }

    [Fact]
    public void The_same_game_from_two_places_counts_once_and_known_exes_are_case_free()
    {
        var a = new LibraryGame("A", "Steam", @"C:\g\a", ["a.exe"]);
        var again = new LibraryGame("A", "Epic Games", @"C:\g\a\", ["a.exe"]);
        var lib = new GameLibrary(DateTimeOffset.Now, GameScanner.Merge([a, again]));
        Assert.Single(lib.Games);
        Assert.Contains("A.EXE", lib.KnownExes);
    }

    [Fact]
    public void Library_survives_a_save_and_a_broken_file_is_empty()
    {
        var path = Path.Combine(_root, "games.json");
        var store = new GameLibraryStore(path);
        Assert.True(store.TrySave(new GameLibrary(DateTimeOffset.UnixEpoch, [new LibraryGame("A", "GOG", @"C:\a", ["a.exe"])])));
        Assert.Equal("A", store.Load().Games.Single().Name);
        File.WriteAllText(path, "{oops");
        Assert.Empty(store.Load().Games);
    }

    // ---- detection ----

    static ForegroundInfo Fg(string exe, bool full) => new(exe, full);

    [Fact]
    public void A_known_game_is_found_sooner_and_in_a_window()
    {
        var d = new GameDetection([]);
        d.SetKnownGames(["game.exe"]);
        var t = DateTimeOffset.UnixEpoch;
        Assert.Null(d.Update(t, Fg("game.exe", false), 30));
        var started = d.Update(t + GameDetection.KnownLoadFor, Fg("game.exe", false), 30);
        Assert.Equal(new GameEvent.Started("game.exe"), started);
    }

    [Fact]
    public void An_unknown_program_still_needs_full_screen_and_a_busy_gpu_for_ten_seconds()
    {
        var d = new GameDetection([]);
        var t = DateTimeOffset.UnixEpoch;
        d.Update(t, Fg("other.exe", false), 90);
        Assert.Null(d.Update(t + GameDetection.LoadFor, Fg("other.exe", false), 90));
        d.Update(t, Fg("full.exe", true), 30);                                     // too little GPU load
        Assert.Null(d.Update(t + GameDetection.LoadFor, Fg("full.exe", true), 30));
        d.Update(t, Fg("full.exe", true), 90);
        Assert.IsType<GameEvent.Started>(d.Update(t + GameDetection.LoadFor, Fg("full.exe", true), 90));
    }

    [Fact]
    public void A_known_game_marked_not_a_game_is_ignored()
    {
        var d = new GameDetection(["game.exe"]);
        d.SetKnownGames(["game.exe"]);
        var t = DateTimeOffset.UnixEpoch;
        d.Update(t, Fg("game.exe", true), 90);
        Assert.Null(d.Update(t + TimeSpan.FromSeconds(30), Fg("game.exe", true), 90));
    }

    // ---- profiles ----

    [Fact]
    public void Profiles_are_off_by_default_and_ignore_the_ai_mode()
    {
        var p = GameProfiles.Default.With("Big Game", FanMode.Performance);
        Assert.Null(p.ModeFor("Big Game"));                                   // off until switched on
        Assert.Equal(FanMode.Performance, p.WithEnabled(true).ModeFor("Big Game"));
        Assert.Null(p.WithEnabled(true).With("Big Game", FanMode.Ai).ModeFor("Big Game"));
        Assert.Null(p.WithEnabled(true).With("Big Game", null).ModeFor("Big Game"));
        Assert.Null(p.WithEnabled(true).ModeFor("Other"));
    }

    [Fact]
    public void Profiles_survive_a_save_and_bad_entries_are_dropped()
    {
        var path = Path.Combine(_root, "profiles.json");
        var store = new GameProfileStore(path);
        Assert.True(store.TrySave(GameProfiles.Default.WithEnabled(true).With("A", FanMode.Quiet)));
        var back = store.Load();
        Assert.True(back.Enabled);
        Assert.Equal(FanMode.Quiet, back.ModeFor("a"));
        File.WriteAllText(path, """{"Enabled":true,"Modes":{"X":"Ai","Y":"Nonsense","Z":"Extreme"}}""");
        var bad = store.Load();
        Assert.Null(bad.ModeFor("X"));
        Assert.Null(bad.ModeFor("Y"));
        Assert.Equal(FanMode.Extreme, bad.ModeFor("Z"));
        File.WriteAllText(path, "nope");
        Assert.False(store.Load().Enabled);
    }

    [Fact]
    public void A_game_is_found_by_any_of_its_program_names()
    {
        var lib = new GameLibrary(DateTimeOffset.Now, [new LibraryGame("A", "Steam", @"C:\a", ["a.exe", "a_launcher.exe"])]);
        Assert.Equal("A", GameLookup.ByExe(lib, "A_LAUNCHER.EXE")!.Name);
        Assert.Null(GameLookup.ByExe(lib, "b.exe"));
        Assert.Null(GameLookup.ByExe(lib, null));
    }
}
