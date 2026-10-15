namespace Ember.Core;

public sealed record ForegroundInfo(string? Exe, bool FullScreen);

public abstract record GameEvent
{
    public sealed record Started(string Exe) : GameEvent;
    public sealed record Ended : GameEvent;
    public sealed record Heartbeat : GameEvent;
}

/// <summary>
/// Decides when a game is running. A game is a full-screen (or borderless full-screen) foreground program that is
/// not excluded or marked "not a game" and keeps the GPU at or above 50% for 10 s. It ends after 5 s without being
/// the full-screen foreground program. While a game is active a heartbeat is due every 10 s. It is a heuristic: the
/// not-a-game list and the manual override are the corrections. Not thread-safe.
/// </summary>
public sealed class GameDetection
{
    public static readonly TimeSpan LoadFor = TimeSpan.FromSeconds(10), EndAfter = TimeSpan.FromSeconds(5),
        HeartbeatEvery = TimeSpan.FromSeconds(10);
    public const double LoadThreshold = 50;

    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vlc.exe", "mpv.exe", "mpc-hc64.exe",
        "wmplayer.exe", "explorer.exe", "ApplicationFrameHost.exe", "Teams.exe", "Zoom.exe",
    };

    /// <summary>A game from a launcher library needs less proof: it counts when windowed too, with a lower GPU load, and sooner.</summary>
    public const double KnownLoadThreshold = 20;
    public static readonly TimeSpan KnownLoadFor = TimeSpan.FromSeconds(4);

    HashSet<string> _notGames;
    HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    string? _active, _candidate;
    DateTimeOffset _candidateSince, _lastHeartbeat;
    DateTimeOffset? _awaySince;

    public GameDetection(IEnumerable<string> notGames) => _notGames = ToSet(notGames);

    public string? ActiveGame => _active;

    public void SetNotGames(IEnumerable<string> exes) => _notGames = ToSet(exes);

    /// <summary>The program names of games found in the launcher libraries.</summary>
    public void SetKnownGames(IEnumerable<string> exes) => _known = ToSet(exes);

    static HashSet<string> ToSet(IEnumerable<string> exes) =>
        new(exes.Select(e => e.Trim()), StringComparer.OrdinalIgnoreCase);

    public GameEvent? Update(DateTimeOffset now, ForegroundInfo fg, double? gpuLoad)
    {
        if (_active is not null && _notGames.Contains(_active)) return End();

        var known = fg.Exe is { Length: > 0 } k && _known.Contains(k);
        var eligible = fg.Exe is { Length: > 0 } exe && !Excluded.Contains(exe) && !_notGames.Contains(exe)
            && (known ? gpuLoad is >= KnownLoadThreshold : fg.FullScreen && gpuLoad is >= LoadThreshold);

        if (eligible)
        {
            if (!string.Equals(_candidate, fg.Exe, StringComparison.OrdinalIgnoreCase))
            {
                _candidate = fg.Exe;
                _candidateSince = now;
            }
        }
        else
        {
            _candidate = null;
        }

        if (_active is not null)
        {
            var stillFront = (fg.FullScreen || _known.Contains(_active)) && string.Equals(fg.Exe, _active, StringComparison.OrdinalIgnoreCase);
            if (stillFront) _awaySince = null;
            else _awaySince ??= now;
        }

        // A different program that stayed eligible long enough replaces the current game.
        if (_candidate is not null && now - _candidateSince >= (_known.Contains(_candidate) ? KnownLoadFor : LoadFor)
            && !string.Equals(_candidate, _active, StringComparison.OrdinalIgnoreCase))
        {
            _active = _candidate;
            _awaySince = null;
            _lastHeartbeat = now;
            return new GameEvent.Started(_active);
        }

        if (_active is null) return null;
        if (_awaySince is { } away && now - away >= EndAfter) return End();
        if (now - _lastHeartbeat >= HeartbeatEvery)
        {
            _lastHeartbeat = now;
            return new GameEvent.Heartbeat();
        }
        return null;
    }

    GameEvent End()
    {
        _active = null;
        _awaySince = null;
        return new GameEvent.Ended();
    }
}
