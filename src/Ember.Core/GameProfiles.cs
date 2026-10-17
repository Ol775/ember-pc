using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// A fan mode for particular games. Off until the owner switches it on, and a game with no mode of its own uses the game mode from
/// the Fans page. The AI mode cannot be picked here (the same rule as the mode buttons during a game).
/// </summary>
public sealed record GameProfiles(bool Enabled, IReadOnlyDictionary<string, FanMode> Modes, IReadOnlyDictionary<string, string>? Scenes = null)
{
    public static GameProfiles Default { get; } = new(false, new Dictionary<string, FanMode>());

    /// <summary>The lighting scene to show while this game runs, or null when profiles are off or the game has none.</summary>
    public string? SceneFor(string? gameName) =>
        Enabled && gameName is not null && Scenes is not null && Scenes.TryGetValue(gameName, out var s) ? s : null;

    public GameProfiles WithScene(string gameName, string? scene)
    {
        var scenes = new Dictionary<string, string>(Scenes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scene) && scene.Length <= 60) scenes[gameName] = scene; else scenes.Remove(gameName);
        return this with { Scenes = scenes.Count == 0 ? null : scenes };
    }

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
    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "game-profiles.json");

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
            var scenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Scenes", out var sc) && sc.ValueKind == JsonValueKind.Object)
                foreach (var p in sc.EnumerateObject().Take(2000))
                    if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { Length: > 0 and <= 60 } scene && p.Name.Length is > 0 and <= 200) scenes[p.Name] = scene;
            return new GameProfiles(enabled, modes, scenes.Count == 0 ? null : scenes);
        }
        catch (Exception) { return GameProfiles.Default; }
    }

    public bool TrySave(GameProfiles profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new { profiles.Enabled, Modes = profiles.Modes.ToDictionary(kv => kv.Key, kv => kv.Value.ToString()), profiles.Scenes }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>
/// Remembers what the lights were doing when a game that has its own lighting scene started, and puts that back when the game ends
/// (unless the owner picked another scene in the meantime). Pure, so it is tested without a window.
/// </summary>
public sealed class GameScenes
{
    string? _before, _applied;
    bool _beforeOn;

    /// <summary>The scene to show (and lights on) for a game that just started, or null when it has none.</summary>
    public string? Start(string? gameName, GameProfiles profiles, string currentScene, bool currentOn, Func<string, bool> sceneExists)
    {
        if (profiles.SceneFor(gameName) is not { } scene || !sceneExists(scene)) return null;
        if (_applied is null) { _before = currentScene; _beforeOn = currentOn; }   // a second game starting keeps the first snapshot
        _applied = scene;
        return scene;
    }

    /// <summary>What to put back when the game ends: the earlier scene and whether the lights were on; null when nothing was changed or the owner chose another scene since.</summary>
    public (string? Scene, bool On)? End(string currentScene, Func<string, bool> sceneExists)
    {
        if (_applied is null) return null;
        var applied = _applied;
        var before = _before;
        var on = _beforeOn;
        (_applied, _before) = (null, null);
        if (!string.Equals(currentScene, applied, StringComparison.Ordinal)) return null;
        return (before is not null && sceneExists(before) ? before : null, on);
    }
}
