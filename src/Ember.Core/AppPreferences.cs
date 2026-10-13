using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public enum StartPage { Dashboard, Fans }

/// <summary>What the app itself remembers for one Windows user (the service's own settings live elsewhere).</summary>
public enum UpdateChannel { Stable, Beta }

public sealed record AppPreferences(TemperatureUnit Unit, StartPage StartPage, ThemeMode Theme = ThemeMode.Dark,
    AccentChoice Accent = AccentChoice.Orange, UpdateChannel Channel = UpdateChannel.Stable, bool AutoCheckUpdates = false, int AcceptedTerms = 0)
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
                Field(doc.RootElement, "StartPage", AppPreferences.Default.StartPage),
                Field(doc.RootElement, "Theme", AppPreferences.Default.Theme),
                Field(doc.RootElement, "Accent", AppPreferences.Default.Accent),
                Field(doc.RootElement, "Channel", AppPreferences.Default.Channel),
                Field(doc.RootElement, "AutoCheckUpdates", AppPreferences.Default.AutoCheckUpdates),
                Math.Clamp(Field(doc.RootElement, "AcceptedTerms", AppPreferences.Default.AcceptedTerms), 0, 1000));
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
