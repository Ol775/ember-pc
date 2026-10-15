using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// A fan mode for particular games. Off until the owner switches it on, and a game with no mode of its own uses the game mode from
/// the Fans page. The AI mode cannot be picked here (the same rule as the mode buttons during a game).
/// </summary>
public sealed record GameProfiles(bool Enabled, IReadOnlyDictionary<string, FanMode> Modes)
{
    public static GameProfiles Default { get; } = new(false, new Dictionary<string, FanMode>());

    /// <summary>The mode to use for this game, or null when profiles are off or the game has none.</summary>
    public FanMode? ModeFor(string? gameName) =>
        Enabled && gameName is not null && Modes.TryGetValue(gameName, out var m) && m != FanMode.Ai ? m : null;

    public GameProfiles With(string gameName, FanMode? mode)
    {
        var modes = new Dictionary<string, FanMode>(Modes, StringComparer.OrdinalIgnoreCase);
        if (mode is { } m && m != FanMode.Ai) modes[gameName] = m; else modes.Remove(gameName);
        return this with { Modes = modes };
    }

    public GameProfiles WithEnabled(bool on) => this with { Enabled = on };
}

/// <summary>The game whose program is this one, found by the program names recorded in the library scan.</summary>
public static class GameLookup
{
    public static LibraryGame? ByExe(GameLibrary library, string? exe) =>
        exe is null ? null : library.Games.FirstOrDefault(g => g.Exes.Contains(exe, StringComparer.OrdinalIgnoreCase));
}

public sealed class GameProfileStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "game-profiles.json");

    public GameProfiles Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1_000_000) return GameProfiles.Default;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var enabled = root.TryGetProperty("Enabled", out var e) && e.ValueKind == JsonValueKind.True;
            var modes = new Dictionary<string, FanMode>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Modes", out var m) && m.ValueKind == JsonValueKind.Object)
                foreach (var p in m.EnumerateObject().Take(2000))
                    if (p.Value.ValueKind == JsonValueKind.String && Enum.TryParse<FanMode>(p.Value.GetString(), out var mode) && Enum.IsDefined(mode) && mode != FanMode.Ai && p.Name.Length is > 0 and < 200)
                        modes[p.Name] = mode;
            return new GameProfiles(enabled, modes);
        }
        catch (Exception) { return GameProfiles.Default; }
    }

    public bool TrySave(GameProfiles profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { profiles.Enabled, Modes = profiles.Modes.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}
