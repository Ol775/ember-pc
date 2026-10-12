using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public sealed record HeaderRecord(string Id, bool Enabled, string Group, string? PassedBoard, string? PassedLhmVersion);

public sealed record EmberSettings(
    FanMode NormalMode, FanMode GameMode, IReadOnlyList<string> NotGames, IReadOnlyList<HeaderRecord> Headers,
    bool GpuControlEnabled)
{
    public static readonly EmberSettings Default =
        new(FanMode.Normal, FanMode.Performance, Array.Empty<string>(), Array.Empty<HeaderRecord>(), false);

    // Record value equality compares list references, so Default equality needs content comparison in tests/callers.
    public bool Equals(EmberSettings? other) =>
        other is not null && NormalMode == other.NormalMode && GameMode == other.GameMode
        && GpuControlEnabled == other.GpuControlEnabled && NotGames.SequenceEqual(other.NotGames)
        && Headers.SequenceEqual(other.Headers);

    public override int GetHashCode() => HashCode.Combine(NormalMode, GameMode, GpuControlEnabled, NotGames.Count, Headers.Count);
}

/// <summary>Atomic JSON settings file. A missing, corrupt, oversized or invalid file loads as defaults and never throws.</summary>
public sealed class SettingsStore(string path)
{
    const int MaxBytes = 1_048_576;

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public EmberSettings Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return EmberSettings.Default;
            var s = JsonSerializer.Deserialize<EmberSettings>(File.ReadAllText(path), Options);
            if (s is null || s.NormalMode == FanMode.Ai || s.GameMode == FanMode.Ai) return EmberSettings.Default;
            if (s.NotGames is null || s.Headers is null || s.Headers.Any(h => h is null || h.Id is null || h.Group is null))
                return EmberSettings.Default;
            return s with { NotGames = Normalize(s.NotGames) };
        }
        catch (Exception)
        {
            return EmberSettings.Default;
        }
    }

    public void Save(EmberSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetRandomFileName());
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(s with { NotGames = Normalize(s.NotGames) }, Options));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    static IReadOnlyList<string> Normalize(IEnumerable<string> exes) =>
        exes.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim().ToLowerInvariant()).Distinct().ToList();
}
