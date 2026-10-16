using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>A game found in a launcher's library: its name, which launcher owns it, where it is installed and the programs that run it.</summary>
public sealed record LibraryGame(string Name, string Launcher, string InstallDir, IReadOnlyList<string> Exes, string? SteamAppId = null);

/// <summary>What the last scan found. Only reads folders and files; nothing is changed or sent anywhere.</summary>
public sealed record GameLibrary(DateTimeOffset ScannedAt, IReadOnlyList<LibraryGame> Games)
{
    public static GameLibrary Empty { get; } = new(DateTimeOffset.MinValue, []);

    /// <summary>Every program name that belongs to a library game, for game detection.</summary>
    public IReadOnlySet<string> KnownExes => Games.SelectMany(g => g.Exes).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Reads the game libraries that launchers keep on disk (Steam, Epic) and the install folders other launchers record.</summary>
public static partial class GameScanner
{
    const int MaxExesPerGame = 12;

    // Redistributables, installers, crash reporters, anti-cheat services and web helpers are not the game itself.
    [GeneratedRegex(@"(unins|uninst|setup|install|redist|dxsetup|dotnet|crashpad|crashreport|crashhandler|easyanticheat|beservice|battleye|oalinst|notification_helper|cefprocess|webhelper|updater|vc_?redist|physx|editor|sdk|elementviewer|vbsp|vrad|vvis|vtex|studiomdl|hammer|bspzip|dmxconvert|vpk|shadercompile|mksheet|height2normal|captioncompiler|glview|demoinfo|phonemeextractor|motionmapper|tgadiff|vtf2tga)", RegexOptions.IgnoreCase)]
    private static partial Regex NotTheGame();

    // Steam tools and runtimes that sit in the library folder but are not games.
    static readonly HashSet<string> SteamTools = new(StringComparer.OrdinalIgnoreCase)
    { "Steamworks Shared", "Steamworks Common Redistributables", "SteamVR", "Steam Linux Runtime", "Proton", "Wallpaper Engine" };

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPath();

    [GeneratedRegex("\"(name|installdir)\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfField();

    /// <summary>The library folders listed in Steam's libraryfolders.vdf, plus Steam's own folder.</summary>
    public static IReadOnlyList<string> SteamLibraries(string steamRoot)
    {
        var libs = new List<string> { steamRoot };
        try
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf) && new FileInfo(vdf).Length < 1_000_000)
                foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                {
                    var p = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (p.Length > 0 && !libs.Contains(p, StringComparer.OrdinalIgnoreCase)) libs.Add(p);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return libs;
    }

    public static IReadOnlyList<LibraryGame> ScanSteam(string steamRoot)
    {
        var games = new List<LibraryGame>();
        foreach (var lib in SteamLibraries(steamRoot))
        {
            string[] manifests;
            try { manifests = Directory.GetFiles(Path.Combine(lib, "steamapps"), "appmanifest_*.acf"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in manifests)
            {
                try
                {
                    string? name = null, dir = null;
                    foreach (Match m in AcfField().Matches(File.ReadAllText(file)))
                    {
                        if (m.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase)) name ??= m.Groups[2].Value;
                        else dir ??= m.Groups[2].Value;
                    }
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir) || SteamTools.Contains(name) || name.StartsWith("Proton ", StringComparison.OrdinalIgnoreCase)) continue;
                    var game = FromFolder(name, "Steam", Path.Combine(lib, "steamapps", "common", dir));
                    if (game is not null) games.Add(game with { SteamAppId = AppIdOf(file) });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return games;
    }

    /// <summary>The number in "appmanifest_<id>.acf", or null when the name has no such number.</summary>
    static string? AppIdOf(string manifest)
    {
        var n = Path.GetFileNameWithoutExtension(manifest);
        var id = n.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) ? n[12..] : null;
        return id is { Length: > 0 and <= 12 } && id.All(char.IsDigit) ? id : null;
    }

    /// <summary>Epic keeps one .item file per installed app in its manifests folder.</summary>
    public static IReadOnlyList<LibraryGame> ScanEpic(string manifestsDir)
    {
        var games = new List<LibraryGame>();
        string[] items;
        try { items = Directory.GetFiles(manifestsDir, "*.item"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return games; }
        foreach (var file in items)
        {
            try
            {
                if (new FileInfo(file).Length > 1_000_000) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                string? Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                // Epic lists its own tools and engines here too: keep only entries filed as games when it says what they are.
                if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
                    && !cats.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase))) continue;
                var name = Str("DisplayName");
                var dir = Str("InstallLocation");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) continue;
                var game = FromFolder(name, "Epic Games", dir, Str("LaunchExecutable"));
                if (game is not null) games.Add(game);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return games;
    }

    /// <summary>A game from a folder a launcher recorded. Null when the folder is gone or holds no program.</summary>
    public static LibraryGame? FromFolder(string name, string launcher, string installDir, string? launchExe = null)
    {
        try
        {
            if (IsNetworkPath(installDir) || !Directory.Exists(installDir)) return null;
            var exes = FindExes(installDir).ToList();
            if (launchExe is { Length: > 0 } && Path.GetFileName(launchExe) is { Length: > 0 } l && !exes.Contains(l, StringComparer.OrdinalIgnoreCase)) exes.Add(l);
            return exes.Count == 0 ? null : new LibraryGame(name.Trim(), launcher, installDir, exes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A share on the network (a server share, written with two backslashes): never read, so a bad entry cannot make this PC contact another one.</summary>
    public static bool IsNetworkPath(string path) => path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>The program file names in a game folder (a few levels deep), biggest first, without installers and helpers.</summary>
    public static IEnumerable<string> FindExes(string dir)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var root = new DirectoryInfo(dir);
        // The programs nearest the game's folder come first (a launcher stub at the top, the real game a few levels down), the biggest first within a level.
        return root.EnumerateFiles("*.exe", options)
            .Where(f => !NotTheGame().IsMatch(f.Name))
            .OrderBy(f => Path.GetRelativePath(root.FullName, f.FullName).Count(c => c is '\\' or '/'))
            .ThenByDescending(f => f.Length)
            .Select(f => f.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxExesPerGame);
    }

    /// <summary>The same game found by two launchers, or twice, counts once.</summary>
    public static IReadOnlyList<LibraryGame> Merge(IEnumerable<LibraryGame> games) =>
        games.GroupBy(g => g.InstallDir.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(g => g.Launcher, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Remembers the last scan so game detection knows the library straight away when Ember starts.</summary>
public sealed class GameLibraryStore(string path)
{
    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "games.json");

    public GameLibrary Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 8_000_000) return GameLibrary.Empty;
            var lib = JsonSerializer.Deserialize<GameLibrary>(File.ReadAllText(path));
            return lib?.Games is null || lib.Games.Any(g => g is null || g.Exes is null || g.Name is null) ? GameLibrary.Empty : lib;
        }
        catch (Exception) { return GameLibrary.Empty; }
    }

    public bool TrySave(GameLibrary library)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(library));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}
