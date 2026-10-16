using System.IO;
using Ember.Core;
using Microsoft.Win32;

namespace Ember.App;

/// <summary>
/// Finds installed games from the launchers on this PC. Reads each launcher's own records (Steam's library folders, Epic's
/// manifests, and the install folders GOG, Ubisoft Connect and the EA app write to the registry) and looks inside the game
/// folders for program names. Nothing is changed, started or sent anywhere.
/// </summary>
public static class GameLibraryScan
{
    /// <summary>The launchers Ember knows how to read, in the order they are shown.</summary>
    public static readonly string[] Launchers = ["Steam", "Epic Games", "GOG", "Ubisoft Connect", "EA app"];

    public static GameLibrary ScanAll()
    {
        var games = new List<LibraryGame>();
        void Run(Func<IEnumerable<LibraryGame>> source)
        {
            try { games.AddRange(source()); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { DiagLog.Changed("Games", "scan", ex.GetType().Name); }
        }

        Run(() => SteamRoot() is { } root ? GameScanner.ScanSteam(root) : []);
        Run(() => GameScanner.ScanEpic(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests")));
        Run(() => FromRegistry(@"SOFTWARE\WOW6432Node\GOG.com\Games", "GOG", "gameName", "path", null));
        Run(() => FromRegistry(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", "Ubisoft Connect", null, "InstallDir", null));
        Run(() => FromRegistry(@"SOFTWARE\WOW6432Node\EA Games", "EA app", null, "Install Dir", null));
        var merged = GameScanner.Merge(games);
        DiagLog.Changed("Games", "library", merged.Count + " games");
        return new GameLibrary(DateTimeOffset.Now, merged);
    }

    internal static string? SteamRoot()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        using var machine = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
        var path = (user?.GetValue("SteamPath") ?? machine?.GetValue("InstallPath")) as string;
        path = path?.Replace('/', '\\');
        return path is { Length: > 0 } && Directory.Exists(path) ? path : null;
    }

    /// <summary>One game per sub-key: the folder is in <paramref name="dirValue"/>, the name in <paramref name="nameValue"/> or the folder name.</summary>
    static IEnumerable<LibraryGame> FromRegistry(string keyPath, string launcher, string? nameValue, string dirValue, string? _)
    {
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        if (key is null) yield break;
        foreach (var sub in key.GetSubKeyNames().Take(500))
        {
            using var k = key.OpenSubKey(sub);
            var dir = (k?.GetValue(dirValue) as string)?.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var name = (nameValue is null ? null : k?.GetValue(nameValue) as string) ?? (launcher == "EA app" ? sub : new DirectoryInfo(dir.TrimEnd('\\', '/')).Name);
            if (GameScanner.FromFolder(name, launcher, dir) is { } game) yield return game;
        }
    }
}
