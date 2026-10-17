using System.Text.Json;

namespace Ember.Lighting;

/// <summary>
/// A lighting scene as a file, to share or keep. The layers and effects go in; the devices do not: the placements (which carry device
/// ids, paths and addresses) and each layer's "applies to" list are left out, so a shared scene never names a device of the sender, and the
/// importing PC lays it out on its own devices.
/// </summary>
public static class SceneFile
{
    public const int MaxBytes = 262144;
    const string Kind = "ember-scene";

    public static string Export(Scene scene)
    {
        var clean = scene with { Placements = [], Layers = scene.Layers.Select(l => l with { Targets = null }).ToList() };
        return JsonSerializer.Serialize(new { Kind, Version = 1, Scene = clean }, LightingStore.JsonOptions);
    }

    /// <summary>The scene in an exported file, or null when the text is anything else, too big, or not a valid scene.</summary>
    public static Scene? TryImport(string text)
    {
        try
        {
            if (text.Length > MaxBytes) return null;
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Kind", out var kind) || kind.GetString() != Kind) return null;
            if (!root.TryGetProperty("Scene", out var element)) return null;
            var scene = element.Deserialize<Scene>(LightingStore.JsonOptions);
            if (scene is null) return null;
            scene = scene with { Placements = [], Layers = scene.Layers.Select(l => l with { Targets = null }).ToList() };
            return LightingState.IsValidScene(scene) ? scene : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException) { return null; }
    }
}
