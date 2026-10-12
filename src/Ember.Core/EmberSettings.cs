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
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return EmberSettings.Default;
            var root = doc.RootElement;
            var d = EmberSettings.Default;
            // Each missing field falls back on its own; a present but invalid one (unknown mode, Ai, wrong type) rejects the file.
            var normal = root.TryGetProperty("NormalMode", out var n) ? n.Deserialize<FanMode>(Options) : d.NormalMode;
            var game = root.TryGetProperty("GameMode", out var g) ? g.Deserialize<FanMode>(Options) : d.GameMode;
            if (normal == FanMode.Ai || game == FanMode.Ai) return d;
            var notGames = root.TryGetProperty("NotGames", out var ng) ? ng.Deserialize<List<string>>(Options) : [];
            var headers = root.TryGetProperty("Headers", out var h) ? h.Deserialize<List<HeaderRecord>>(Options) : [];
            var gpu = root.TryGetProperty("GpuControlEnabled", out var gc) ? gc.Deserialize<bool>(Options) : d.GpuControlEnabled;
            if (notGames is null || headers is null || notGames.Any(e => e is null)
                || headers.Any(r => r is null || r.Id is null || r.Group is null))
                return d;
            return new EmberSettings(normal, game, Normalize(notGames), headers, gpu);
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
