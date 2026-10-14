using System.Globalization;
using System.Text.Json;

namespace Ember.Core;

/// <summary>An event from the Stream Deck app. <see cref="Settings"/> is the key's saved settings object, when the event carries one.</summary>
public sealed record SdEvent(string Event, string? Action, string? Context, JsonElement? Settings)
{
    public string? SettingString(string key) =>
        Settings is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public double? SettingNumber(string key) =>
        Settings is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    /// <summary>Every string or number setting as text (numbers in invariant form), for the key face code.</summary>
    public Dictionary<string, string> SettingsAsText()
    {
        var map = new Dictionary<string, string>();
        if (Settings is not { ValueKind: JsonValueKind.Object } s) return map;
        foreach (var p in s.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() is { } text) map[p.Name] = text;
            else if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var d)) map[p.Name] = d.ToString("R", CultureInfo.InvariantCulture);
        }
        return map;
    }
}

/// <summary>The few Stream Deck WebSocket messages the plugin sends and receives. Parsing never throws.</summary>
public static class StreamDeckMessages
{
    public static string RegisterPlugin(string uuid) => JsonSerializer.Serialize(new { @event = "registerPlugin", uuid });

    public static string SetTitle(string context, string title) =>
        JsonSerializer.Serialize(new { @event = "setTitle", context, payload = new { title, target = 0 } });

    public static string SetImage(string context, string svg) =>
        JsonSerializer.Serialize(new { @event = "setImage", context, payload = new { image = "data:image/svg+xml;charset=utf8," + Uri.EscapeDataString(svg), target = 0 } });

    public static string ShowAlert(string context) => JsonSerializer.Serialize(new { @event = "showAlert", context });

    public static string SendToPropertyInspector(string action, string context, object payload) =>
        JsonSerializer.Serialize(new { @event = "sendToPropertyInspector", action, context, payload });

    public static bool TryParseEvent(string json, out SdEvent? e)
    {
        e = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String) return false;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            JsonElement? settings = null;
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object)
                settings = s.Clone();
            e = new SdEvent(ev.GetString()!, Str("action"), Str("context"), settings);
            return true;
        }
        catch (Exception)
        {
            e = null;
            return false;
        }
    }
}
