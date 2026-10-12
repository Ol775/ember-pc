namespace Ember.Core;

public enum ModeSource { User, Game, GameOverride }

/// <summary>
/// Which mode is in force: the user's normal mode, the game mode while a game runs, or the user's pick for the
/// current game (an override that ends with that game). Game state is never persisted. Not thread-safe.
/// </summary>
public sealed class ModeResolver
{
    public static readonly TimeSpan GameTimeout = TimeSpan.FromSeconds(30);

    FanMode _normal, _game;
    FanMode? _override;
    DateTimeOffset _lastHeard;

    public ModeResolver(FanMode normal, FanMode game)
    {
        if (normal == FanMode.Ai || game == FanMode.Ai) throw new ArgumentException("Ai mode is not available yet.");
        _normal = normal;
        _game = game;
    }

    public string? GameExe { get; private set; }

    public FanMode NormalMode => _normal;
    public FanMode GameMode => _game;

    public FanMode Effective => GameExe is null ? _normal : _override ?? _game;

    public ModeSource Source => GameExe is null ? ModeSource.User : _override is null ? ModeSource.Game : ModeSource.GameOverride;

    /// <summary>False (and no change) for Ai.</summary>
    public bool PickMode(FanMode mode)
    {
        if (mode == FanMode.Ai) return false;
        if (GameExe is null) _normal = mode; else _override = mode;
        return true;
    }

    public void SetNormalMode(FanMode mode) { if (mode != FanMode.Ai) _normal = mode; }

    public void SetGameMode(FanMode mode) { if (mode != FanMode.Ai) _game = mode; }

    public void GameStarted(string exe, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(exe)) throw new ArgumentException("Game executable is required.", nameof(exe));
        GameExe = exe;
        _override = null;
        _lastHeard = now;
    }

    public void GameEnded()
    {
        GameExe = null;
        _override = null;
    }

    public void Heartbeat(DateTimeOffset now) { if (GameExe is not null) _lastHeard = now; }

    /// <summary>Ends the game when the detector has been silent for more than 30 s. True if it ended one.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (GameExe is null || now - _lastHeard <= GameTimeout) return false;
        GameEnded();
        return true;
    }
}
