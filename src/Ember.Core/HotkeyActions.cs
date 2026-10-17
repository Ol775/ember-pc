namespace Ember.Core;

/// <summary>What the global hotkeys do, as plain choices (the window supplies the current state and carries out the answer).</summary>
public static class HotkeyActions
{
    /// <summary>The next fan mode after this one, wrapping round (the same order as the tray menu and the Stream Deck cycle key); a mode outside the cycle goes to Normal.</summary>
    public static FanMode NextFanMode(FanMode current) => StreamDeckLogic.Next(current);

    /// <summary>The scene after the current one, wrapping round; null when there is nothing to switch to.</summary>
    public static string? NextScene(IReadOnlyList<string> scenes, string current)
    {
        if (scenes.Count < 2) return null;
        var i = scenes.ToList().IndexOf(current);
        return scenes[(i + 1) % scenes.Count];
    }
}
