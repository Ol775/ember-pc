using System.Net;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Hue;

public sealed record HueLight(string Id, string Name, bool CanColour);

/// <summary>The bridge answered, but the button on top of it has not been pressed yet.</summary>
public sealed class HueLinkButtonException() : Exception("Press the round button on top of the Hue bridge, then try again within 30 seconds.");

/// <summary>
/// A Philips Hue bridge over its local network API (CLIP v2): no account and no cloud. The bridge has a certificate of its own,
/// so the HttpClient given here pins it by thumbprint (see <see cref="HuePinning"/>). The key is sent to that bridge only.
/// </summary>
public sealed class HueClient(HttpClient http, string host, string? appKey)
{
    public string Host => host;

    /// <summary>Asks the bridge for a new application key. It only answers while its link button has just been pressed.</summary>
    public static async Task<string> PairAsync(HttpClient http, string host, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri($"https://{host}/api"))
        {
            Content = new StringContent("{\"devicetype\":\"ember#pc\"}", Encoding.UTF8, "application/json"),
        };
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParsePairing(body);
    }

    public static string ParsePairing(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("success", out var ok) && ok.TryGetProperty("username", out var u) && u.GetString() is { Length: > 0 } key) return key;
                if (item.TryGetProperty("error", out var err))
                {
                    if (err.TryGetProperty("type", out var t) && t.GetInt32() == 101) throw new HueLinkButtonException();
                    throw new InvalidOperationException("The bridge refused: " + (err.TryGetProperty("description", out var d) ? d.GetString() : "unknown reason"));
                }
            }
        }
        catch (HueLinkButtonException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception) { /* not the answer of a Hue bridge */ }
        throw new InvalidOperationException("That did not look like a Hue bridge.");
    }

    public async Task<IReadOnlyList<HueLight>> GetLightsAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "light");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new InvalidOperationException("The bridge no longer accepts the saved key. Link it again in Settings.");
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The bridge answered {(int)res.StatusCode}.");
        return ParseLights(body);
    }

    public static IReadOnlyList<HueLight> ParseLights(string body)
    {
        var lights = new List<HueLight>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 64) continue;
                var name = item.TryGetProperty("metadata", out var m) && m.TryGetProperty("name", out var n) ? n.GetString() : null;
                lights.Add(new HueLight(id, string.IsNullOrWhiteSpace(name) ? "Hue light" : name.Length > 60 ? name[..60] : name, item.TryGetProperty("color", out _)));
            }
        }
        catch (Exception) { /* an answer that is not a light list: no lights */ }
        return lights;
    }

    public async Task SetAsync(string lightId, bool on, double x, double y, double brightness, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Put, "light/" + Uri.EscapeDataString(lightId));
        req.Content = new StringContent(Payload(on, x, y, brightness), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The bridge answered {(int)res.StatusCode}.");
    }

    public static string Payload(bool on, double x, double y, double brightness) => !on
        ? "{\"on\":{\"on\":false},\"dynamics\":{\"duration\":0}}"
        : JsonSerializer.Serialize(new
        {
            on = new { on = true },
            dimming = new { brightness = Math.Clamp(brightness, 1, 100) },
            color = new { xy = new { x, y } },
            dynamics = new { duration = 0 },
        });

    HttpRequestMessage Request(HttpMethod method, string path)
    {
        if (string.IsNullOrWhiteSpace(appKey)) throw new InvalidOperationException("The Hue bridge is not linked yet.");
        var req = new HttpRequestMessage(method, new Uri($"https://{host}/clip/v2/resource/{path}"));
        req.Headers.Add("hue-application-key", appKey);
        return req;
    }
}
