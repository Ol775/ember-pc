using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public enum StartPage { Dashboard, Fans, Lights, Devices }

/// <summary>What the app itself remembers for one Windows user (the service's own settings live elsewhere).</summary>
public enum UpdateChannel { Stable, Beta }

public sealed record AppPreferences(TemperatureUnit Unit, StartPage StartPage, ThemeMode Theme = ThemeMode.Dark,
    AccentChoice Accent = AccentChoice.Orange, UpdateChannel Channel = UpdateChannel.Stable, bool AutoCheckUpdates = true, int AcceptedTerms = 0,
    bool KeepInTray = false, bool StartMinimised = false, bool AiLearning = false, double AiPreference = 0.35,
    SurfaceTone Tone = SurfaceTone.Warm, HighlightStrength Highlight = HighlightStrength.Normal, string CustomAccent = ThemePalette.DefaultCustomAccent,
    string SkippedVersion = "", UiSize Size = UiSize.Normal, string GpuColour = "Auto", bool RememberWindow = false, string Window = "", double PricePerKwh = 0.28, double StandingCharge = 0, string Currency = "Auto")
{
    public static readonly AppPreferences Default = new(TemperatureUnit.Celsius, StartPage.Dashboard);

    /// <summary>The accent colour chosen for the GPU line, or null for Auto (a partner to the accent).</summary>
    [JsonIgnore]
    public AccentChoice? GpuAccent => Enum.TryParse<AccentChoice>(GpuColour, out var a) && a != AccentChoice.Custom ? a : null;
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
        Ember.Core.AppData.Roaming, "Ember", "app.json");

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
                Math.Clamp(Field(doc.RootElement, "AcceptedTerms", AppPreferences.Default.AcceptedTerms), 0, 1000),
                Field(doc.RootElement, "KeepInTray", AppPreferences.Default.KeepInTray),
                Field(doc.RootElement, "StartMinimised", AppPreferences.Default.StartMinimised),
                Field(doc.RootElement, "AiLearning", AppPreferences.Default.AiLearning),
                ClampUnit(Field(doc.RootElement, "AiPreference", AppPreferences.Default.AiPreference)),
                Field(doc.RootElement, "Tone", AppPreferences.Default.Tone),
                Field(doc.RootElement, "Highlight", AppPreferences.Default.Highlight),
                CustomColour(Field(doc.RootElement, "CustomAccent", AppPreferences.Default.CustomAccent)),
                VersionText(Field(doc.RootElement, "SkippedVersion", AppPreferences.Default.SkippedVersion)),
                Field(doc.RootElement, "Size", AppPreferences.Default.Size),
                GpuChoice(Field(doc.RootElement, "GpuColour", AppPreferences.Default.GpuColour)),
                Field(doc.RootElement, "RememberWindow", AppPreferences.Default.RememberWindow),
                WindowText(Field(doc.RootElement, "Window", AppPreferences.Default.Window)),
                Price(Field(doc.RootElement, "PricePerKwh", AppPreferences.Default.PricePerKwh)),
                Standing(Field(doc.RootElement, "StandingCharge", AppPreferences.Default.StandingCharge)),
                PowerCurrency.IsValid(Field(doc.RootElement, "Currency", AppPreferences.Default.Currency)) ? Field(doc.RootElement, "Currency", AppPreferences.Default.Currency) : PowerCurrency.Auto);
        }
        catch (Exception)
        {
            return AppPreferences.Default;
        }
    }

    /// <summary>The version the person chose to skip: a short plain version text, or nothing.</summary>
    static string VersionText(string v) => v is { Length: > 0 and <= 40 } && AppVersion.TryParse(v, out _) ? v : "";

    /// <summary>"Auto" (a partner to the accent) or the name of one of the accent colours; anything else is Auto.</summary>
    static string GpuChoice(string v) => v == "Auto" || (Enum.TryParse<AccentChoice>(v, out var a) && Enum.IsDefined(a) && a != AccentChoice.Custom && !int.TryParse(v, out _)) ? v : "Auto";

    /// <summary>The saved window position, or nothing when the text is not a valid one.</summary>
    static string WindowText(string v) => WindowPlacement.TryParse(v, out _) ? v : "";

    static string CustomColour(string v) => ThemePalette.IsHexColour(v) ? v.ToLowerInvariant() : AppPreferences.Default.CustomAccent;

    /// <summary>The price of a kWh the person typed: a sensible positive number, else the default.</summary>
    static double Price(double v) => double.IsFinite(v) && v is >= 0 and <= 1000 ? v : AppPreferences.Default.PricePerKwh;

    /// <summary>The daily standing charge the person typed: zero or more, a sensible amount, else none.</summary>
    static double Standing(double v) => double.IsFinite(v) && v is >= 0 and <= 1000 ? v : 0;

    static double ClampUnit(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : AppPreferences.Default.AiPreference;

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
