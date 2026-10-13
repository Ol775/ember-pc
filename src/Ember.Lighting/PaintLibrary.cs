using System.Text.Json;

namespace Ember.Lighting;

/// <summary>One saved picture in the paint library.</summary>
public sealed record PaintEntry(string Name, int Leds, int Columns);

/// <summary>
/// A folder of saved paint pictures, one <see cref="PaintFile"/> per name, kept on this PC so they can be reused on any device
/// and copied to another PC. Names are cleaned so they can never point outside the folder, a damaged file is skipped, and the
/// library holds at most <see cref="MaxEntries"/> pictures. Every method is best effort and never throws.
/// </summary>
public sealed class PaintLibrary(string folder)
{
    public const int MaxEntries = 200;
    public const int MaxNameLength = 40;

    static readonly string[] Reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "paints");

    /// <summary>The name as it will be stored, or null if nothing usable is left (empty, only dots, or a reserved Windows name).</summary>
    public static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var bad = Path.GetInvalidFileNameChars();
        var kept = new string(name.Where(c => !bad.Contains(c) && !char.IsControl(c)).ToArray());
        kept = string.Join(' ', kept.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.', ' ');
        if (kept.Length > MaxNameLength) kept = kept[..MaxNameLength].TrimEnd('.', ' ');
        if (kept.Length == 0 || kept.All(c => c == '.')) return null;
        if (Reserved.Contains(kept.Split('.')[0].ToUpperInvariant())) return null;
        return kept;
    }

    string PathOf(string clean) => Path.Combine(folder, clean + ".json");

    public IReadOnlyList<PaintEntry> List()
    {
        try
        {
            if (!Directory.Exists(folder)) return [];
            var entries = new List<PaintEntry>();
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").Take(MaxEntries * 2))
            {
                if (Clean(Path.GetFileNameWithoutExtension(file)) is not { } name || new FileInfo(file).Length > PaintFile.MaxBytes) continue;
                var text = SafeRead(file, out var columns);
                if (PaintFile.TryImport(text) is { } paint) entries.Add(new PaintEntry(name, paint.Count, columns));
                if (entries.Count >= MaxEntries) break;
            }
            return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return []; }
    }

    static string? SafeRead(string file, out int columns)
    {
        columns = 0;
        try
        {
            var text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("columns", out var c) && c.TryGetInt32(out var n)) columns = Math.Clamp(n, 0, PaintTools.MaxLeds);
            return text;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Saves (or replaces) a picture. False for an unusable name, an empty picture, a full library, or a disk problem.</summary>
    public bool Save(string name, IReadOnlyList<string?> paint, int columns)
    {
        try
        {
            if (Clean(name) is not { } clean || paint.Count == 0 || paint.Count > PaintTools.MaxLeds || paint.All(p => p is null)) return false;
            Directory.CreateDirectory(folder);
            var path = PathOf(clean);
            if (!File.Exists(path) && Directory.EnumerateFiles(folder, "*.json").Count() >= MaxEntries) return false;
            var temp = Path.Combine(folder, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, PaintFile.Export(paint, columns, clean));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        catch (Exception) { return false; }
    }

    public IReadOnlyList<string?>? Load(string name)
    {
        try
        {
            if (Clean(name) is not { } clean) return null;
            var path = PathOf(clean);
            if (!File.Exists(path) || new FileInfo(path).Length > PaintFile.MaxBytes) return null;
            return PaintFile.TryImport(File.ReadAllText(path));
        }
        catch (Exception) { return null; }
    }

    public bool Delete(string name)
    {
        try
        {
            if (Clean(name) is not { } clean || !File.Exists(PathOf(clean))) return false;
            File.Delete(PathOf(clean));
            return true;
        }
        catch (Exception) { return false; }
    }

    public bool Rename(string from, string to)
    {
        try
        {
            if (Clean(from) is not { } a || Clean(to) is not { } b || !File.Exists(PathOf(a))) return false;
            if (a == b) return true;
            if (File.Exists(PathOf(b))) return false;
            File.Move(PathOf(a), PathOf(b));
            return true;
        }
        catch (Exception) { return false; }
    }
}
