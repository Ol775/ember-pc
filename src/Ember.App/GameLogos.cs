using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// The picture for a game on the Games page, found on this PC and nowhere else: Steam's own cover in its local picture cache, or the game's
/// program icon. Nothing is downloaded and no picture ships with Ember. Pictures are small, frozen and forgotten when the library is scanned again.
/// </summary>
static class GameLogos
{
    public sealed record Logo(ImageSource Image, bool IsIcon);

    const long MaxFileBytes = 3_000_000;
    static readonly Dictionary<string, Logo?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Forget() => Cache.Clear();

    public static Logo? For(LibraryGame game)
    {
        var key = game.Launcher + "|" + game.Name;
        if (GameScanner.IsNetworkPath(game.InstallDir)) return null;
        if (Cache.TryGetValue(key, out var hit)) return hit;
        Logo? logo = null;
        try { logo = SteamCover(game) ?? ExeIcon(game); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        Cache[key] = logo;
        return logo;
    }

    /// <summary>Steam keeps a cover for every game it has shown in appcache\librarycache, in the old layout (one file per picture) or a folder per game.</summary>
    static Logo? SteamCover(LibraryGame game)
    {
        if (game.SteamAppId is not { } id || GameLibraryScan.SteamRoot() is not { } root) return null;
        var cache = Path.Combine(root, "appcache", "librarycache");
        foreach (var file in new[] { $"{id}_library_600x900.jpg", $"{id}_header.jpg", $"{id}_icon.jpg" })
            if (Load(Path.Combine(cache, file)) is { } image) return new Logo(image, false);
        var folder = Path.Combine(cache, id);
        if (!Directory.Exists(folder)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var name in new[] { "library_600x900.jpg", "header.jpg", "logo.png" })
            foreach (var file in Directory.EnumerateFiles(folder, name, options).Take(1))
                if (Load(file) is { } image) return new Logo(image, false);
        return null;
    }

    static BitmapImage? Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is 0 or > MaxFileBytes) return null;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.DecodePixelWidth = 96;   // the tile is 38 points wide: do not keep a full-size picture in memory
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The icon of the main program of the game, found inside its install folder.</summary>
    static Logo? ExeIcon(LibraryGame game)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var exe in game.Exes.Take(3))
            foreach (var path in Directory.EnumerateFiles(game.InstallDir, exe, options).Take(1))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) continue;
                var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
                source.Freeze();
                return new Logo(source, true);
            }
        return null;
    }
}
