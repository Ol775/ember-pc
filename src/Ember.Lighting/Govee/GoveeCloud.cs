using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Govee;

public sealed record GoveeCloudDevice(string Id, string Sku, string Name, bool CanSetColor, int? Segments = null);

/// <summary>
/// Govee's cloud API, for lamps that do not offer LAN control. Slow and rate limited by Govee, so it only ever carries the
/// scene colour as it changes, never real-time frames. The key is sent in a header to Govee and nowhere else.
/// </summary>
public sealed class GoveeCloudClient(HttpClient http, Func<string?> apiKey)
{
    public const string BaseUrl = "https://openapi.api.govee.com/router/api/v1/";

    public async Task<IReadOnlyList<GoveeCloudDevice>> GetDevicesAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "user/devices");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await ReadAsync(res, ct).ConfigureAwait(false);
        return ParseDevices(body);
    }

    public async Task SetColorAsync(GoveeCloudDevice device, byte r, byte g, byte b, CancellationToken ct = default)
    {
        var rgb = (r << 16) | (g << 8) | b;
        var json = JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid().ToString(),
            payload = new
            {
                sku = device.Sku,
                device = device.Id,
                capability = new { type = "devices.capabilities.color_setting", instance = "colorRgb", value = rgb },
            },
        });
        using var req = Request(HttpMethod.Post, "device/control");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        await ReadAsync(res, ct).ConfigureAwait(false);
    }

    public async Task SetPowerAsync(GoveeCloudDevice device, bool on, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(new
        {
            requestId = Guid.NewGuid().ToString(),
            payload = new
            {
                sku = device.Sku,
                device = device.Id,
                capability = new { type = "devices.capabilities.on_off", instance = "powerSwitch", value = on ? 1 : 0 },
            },
        });
        using var req = Request(HttpMethod.Post, "device/control");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        await ReadAsync(res, ct).ConfigureAwait(false);
    }

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        var key = apiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No Govee API key is saved.");
        var req = new HttpRequestMessage(method, new Uri(new Uri(BaseUrl), path));
        req.Headers.Add("Govee-API-Key", key.Trim());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    static async Task<string> ReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (body.Length > 1_000_000) throw new IOException("Govee sent an unexpectedly large answer.");
        if ((int)res.StatusCode == 401) throw new UnauthorizedAccessException("Govee did not accept the API key.");
        if ((int)res.StatusCode == 429) throw new IOException("Govee is limiting requests; Ember will try again later.");
        if (!res.IsSuccessStatusCode) throw new IOException($"Govee answered {(int)res.StatusCode}.");
        return body;
    }

    /// <summary>
    /// How many parts the lamp says it can colour separately: the top of the range of its segment capability plus one
    /// (the cloud counts from 0). Null when the lamp has no such capability or the answer is not in the expected shape.
    /// </summary>
    public static int? SegmentCountOf(JsonElement capabilities)
    {
        if (capabilities.ValueKind != JsonValueKind.Array) return null;
        foreach (var cap in capabilities.EnumerateArray())
        {
            if (cap.ValueKind != JsonValueKind.Object || !cap.TryGetProperty("type", out var type) || type.GetString() != "devices.capabilities.segment_color_setting") continue;
            if (!cap.TryGetProperty("parameters", out var ps) || !ps.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array) continue;
            foreach (var f in fields.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object || !f.TryGetProperty("fieldName", out var fn) || fn.GetString() != "segment") continue;
                if (f.TryGetProperty("elementRange", out var range) && range.TryGetProperty("max", out var max) && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var top) && top is >= 0 and < 255)
                    return top + 1;
            }
        }
        return null;
    }

    /// <summary>Reads the device list; an answer in an unexpected shape gives an empty list.</summary>
    public static IReadOnlyList<GoveeCloudDevice> ParseDevices(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<GoveeCloudDevice>();
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
            foreach (var d in data.EnumerateArray().Take(100))
            {
                if (d.ValueKind != JsonValueKind.Object) continue;
                var id = d.TryGetProperty("device", out var i) ? i.GetString() : null;
                var sku = d.TryGetProperty("sku", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sku) || id.Length > 64 || sku.Length > 32) continue;
                var name = d.TryGetProperty("deviceName", out var n) ? n.GetString() : null;
                var canColor = d.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array
                    && caps.EnumerateArray().Any(c => c.TryGetProperty("instance", out var inst) && inst.GetString() == "colorRgb");
                var segments = d.TryGetProperty("capabilities", out var capList) ? SegmentCountOf(capList) : null;
                list.Add(new GoveeCloudDevice(id, sku, string.IsNullOrWhiteSpace(name) ? "Govee " + sku : name!, canColor, segments));
            }
            return list;
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>A lamp reached through the Govee cloud: only changes of colour, and not more often than every few seconds.</summary>
public sealed class GoveeCloudSink(GoveeCloudClient client, GoveeCloudDevice device) : IDeviceSink
{
    bool? _on;

    public string Id => "govee:" + device.Id;
    public string Name => device.Name + " (cloud)";
    public string Kind => "Light";
    public int LedCount => 1;
    public TimeSpan MinInterval => TimeSpan.FromSeconds(6);
    public bool Keepalive => false;
    public bool Realtime => false;

    public async Task SendAsync(IReadOnlyList<Color> leds, CancellationToken ct)
    {
        var (r, g, b) = Compositor.Average(leds).ToBytes();
        if (r == 0 && g == 0 && b == 0)
        {
            if (_on != false) { await client.SetPowerAsync(device, false, ct).ConfigureAwait(false); _on = false; }
            return;
        }
        if (_on != true) { await client.SetPowerAsync(device, true, ct).ConfigureAwait(false); _on = true; }
        await client.SetColorAsync(device, r, g, b, ct).ConfigureAwait(false);
    }
}
