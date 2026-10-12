using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public enum StartPage { Dashboard, Fans }

/// <summary>What the app itself remembers for one Windows user (the service's own settings live elsewhere).</summary>
public sealed record AppPreferences(TemperatureUnit Unit, StartPage StartPage)
{
    public static readonly AppPreferences Default = new(TemperatureUnit.Celsius, StartPage.Dashboard);
}

/// <summary>JSON file of <see cref="AppPreferences"/>. A missing, oversized or damaged file loads as the defaults, field by field, and never throws.</summary>
public sealed class PreferencesStore(string path)
{
    const int MaxBytes = 65_536;
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "app.json");

    public AppPreferences Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return AppPreferences.Default;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return AppPreferences.Default;
            return new AppPreferences(
                Field(doc.RootElement, "Unit", AppPreferences.Default.Unit),
                Field(doc.RootElement, "StartPage", AppPreferences.Default.StartPage));
        }
        catch (Exception)
        {
            return AppPreferences.Default;
        }
    }

    static T Field<T>(JsonElement root, string name, T fallback)
    {
        try { return root.TryGetProperty(name, out var e) ? e.Deserialize<T>(Options) ?? fallback : fallback; }
        catch (JsonException) { return fallback; }
    }

    /// <summary>Best effort: a read-only profile must not stop the app from working.</summary>
    public bool TrySave(AppPreferences prefs)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(prefs, Options));
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
