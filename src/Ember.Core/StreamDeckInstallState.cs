using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ember.Core;

public enum StreamDeckState { NotFound, NotInstalled, Installed, NeedsUpdate }

/// <summary>Works out what the Stream Deck card in Ember should offer, from what is on the PC.</summary>
public static partial class StreamDeckInstallState
{
    [GeneratedRegex(@"^\d+\.\d+\.\d+\.\d+$")]
    private static partial Regex FourParts();

    /// <summary>
    /// <paramref name="appFound"/>: the Stream Deck app is on this PC. <paramref name="installedVersion"/>: the plugin version in its
    /// Stream Deck folder, or null when it is not there. A newer installed plugin counts as installed; one that cannot be read needs an update.
    /// </summary>
    public static StreamDeckState Evaluate(bool appFound, string? installedVersion, string bundledVersion)
    {
        if (!appFound) return StreamDeckState.NotFound;
        if (installedVersion is null) return StreamDeckState.NotInstalled;
        if (!TryParse(installedVersion, out var installed)) return StreamDeckState.NeedsUpdate;
        if (!TryParse(bundledVersion, out var bundled)) return StreamDeckState.Installed;
        return installed >= bundled ? StreamDeckState.Installed : StreamDeckState.NeedsUpdate;
    }

    static bool TryParse(string s, out Version version)
    {
        version = new Version();
        return FourParts().IsMatch(s) && Version.TryParse(s, out version!);
    }

    /// <summary>The "Version" of a plugin manifest, or null when the text is not a manifest.</summary>
    public static string? ReadManifestVersion(string manifestJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(manifestJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
