using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Lighting;

/// <summary>What the Lights page remembers. Lights start switched off, so Ember never takes over a device until asked.</summary>
public sealed record LightingState(
    bool On, string CurrentScene, IReadOnlyList<Scene> Scenes, string? OpenRgbPath = null, bool? StartOpenRgb = null)
{
    /// <summary>Null means automatic: start OpenRGB when a program is known (the bundled one, or the chosen path). False turns that off.</summary>
    [JsonIgnore] public bool ShouldStartOpenRgb => StartOpenRgb != false;

    public const int MaxScenes = 50, MaxLayers = 20, MaxPlacements = 100;

    public static readonly Scene DefaultScene = new("Warm glow",
        [new Layer("base", "Warm colour", new StaticEffect(new Color(1.0, 0.45, 0.1)))], []);

    public static LightingState Default => new(false, DefaultScene.Name, [DefaultScene]);

    public Scene Current => Scenes.FirstOrDefault(s => s.Name == CurrentScene) ?? Scenes[0];

    /// <summary>True when every list is within its limit and every number is finite and in range.</summary>
    public static bool IsValid(LightingState s)
    {
        if (s.Scenes is not { Count: > 0 and <= MaxScenes } scenes || s.CurrentScene is null) return false;
        if (scenes.Select(x => x?.Name).Distinct().Count() != scenes.Count) return false;
        return scenes.All(Valid);
    }

    static bool Valid(Scene? s) =>
        s is { Name: { Length: > 0 and <= 60 }, Layers: { Count: <= MaxLayers }, Placements: { Count: <= MaxPlacements } }
        && double.IsFinite(s.Brightness) && s.Brightness is >= 0 and <= 1
        && s.Layers.All(l => l is { Effect: not null, Id: not null, Name: not null } && double.IsFinite(l.Opacity) && l.Opacity is >= 0 and <= 1)
        && s.Placements.All(p => p is { DeviceId: not null }
            && (p.Profile is null || p.Profile.Length <= 60)
            && new[] { p.X, p.Y, p.Width, p.Height }.All(v => double.IsFinite(v) && v is >= -1 and <= 2));
}

/// <summary>JSON file of <see cref="LightingState"/>. Anything missing, oversized or invalid loads as the defaults and never throws.</summary>
public sealed class LightingStore(string path)
{
    const int MaxBytes = 1_048_576;
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "lighting.json");

    public LightingState Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return LightingState.Default;
            var state = JsonSerializer.Deserialize<LightingState>(File.ReadAllText(path), Options);
            return state is not null && LightingState.IsValid(state) ? state : LightingState.Default;
        }
        catch (Exception)
        {
            return LightingState.Default;
        }
    }

    /// <summary>Best effort: false when the file could not be written.</summary>
    public bool TrySave(LightingState state)
    {
        try
        {
            if (!LightingState.IsValid(state)) return false;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
