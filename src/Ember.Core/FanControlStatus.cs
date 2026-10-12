namespace Ember.Core;

public enum HeaderState { Unknown, ReadOnly, Controllable, Enabled, Failed }

public sealed record HeaderStatus(string Id, string Name, HeaderState State, bool Tested);

/// <summary>What the service tells every client about fan control. Published as the optional "c" field of a snapshot.</summary>
public sealed record FanControlStatus(
    FanMode Mode, ModeSource Source, bool SafetyOverride, string? GameExe,
    IReadOnlyList<HeaderStatus> Headers, IReadOnlyList<string> Competitors, string? TestMessage,
    IReadOnlyList<string>? NotGamesList = null, bool GpuControl = false)
{
    /// <summary>Programs the user marked "not a game" (lower case).</summary>
    public IReadOnlyList<string> NotGames => NotGamesList ?? Array.Empty<string>();
}
