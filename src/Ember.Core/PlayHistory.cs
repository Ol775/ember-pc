using System.Text.Json;

namespace Ember.Core;

/// <summary>When each game was last in front of you and for how long in all, learned from the games Ember is watching. Kept on this PC only.</summary>
public sealed class PlayHistory
{
    public sealed record Entry(DateTimeOffset Last, double Seconds);

    public const int MaxGames = 2000;
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    readonly Dictionary<string, Entry> _games = new(StringComparer.OrdinalIgnoreCase);

    public bool Dirty { get; private set; }

    /// <summary>Goes up with every change, so a page knows when to redraw.</summary>
    public int Version { get; private set; }

    /// <summary>The game was in front for <paramref name="seconds"/> up to <paramref name="now"/>.</summary>
    public void Played(string game, DateTimeOffset now, double seconds)
    {
        if (string.IsNullOrWhiteSpace(game) || game.Length > 200 || !double.IsFinite(seconds) || seconds <= 0) return;
        if (!_games.ContainsKey(game) && _games.Count >= MaxGames) return;
        var before = _games.GetValueOrDefault(game);
        _games[game] = new Entry(now, (before?.Seconds ?? 0) + seconds);
        Dirty = true;
        Version++;
    }

    public Entry? Of(string game) => _games.GetValueOrDefault(game);

    /// <summary>"Last played yesterday", "Last played 3 weeks ago", or "Never played" when Ember has not seen it.</summary>
    public string Describe(string game, DateTimeOffset now)
    {
        if (Of(game) is not { } e) return "Not played yet";
        var days = (DateOnly.FromDateTime(now.LocalDateTime).DayNumber - DateOnly.FromDateTime(e.Last.LocalDateTime).DayNumber);
        return "Last played " + days switch
        {
            <= 0 => "today",
            1 => "yesterday",
            < 7 => days + " days ago",
            < 14 => "last week",
            < 30 => days / 7 + " weeks ago",
            < 60 => "last month",
            < 365 => days / 30 + " months ago",
            _ => "over a year ago",
        };
    }

    // ---- file ----

    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "play-history.json");

    /// <summary>A missing or damaged file is an empty history; this never throws.</summary>
    public static PlayHistory Load(string path)
    {
        var h = new PlayHistory();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 2_000_000) return h;
            foreach (var (name, e) in JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path), Options) ?? [])
                if (name is { Length: > 0 and <= 200 } && e is not null && double.IsFinite(e.Seconds) && e.Seconds >= 0 && h._games.Count < MaxGames) h._games[name] = e;
        }
        catch (Exception) { /* start again */ }
        h.Dirty = false;
        return h;
    }

    public bool TrySave(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(_games, Options));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            Dirty = false;
            return true;
        }
        catch (Exception) { return false; }
    }
}
