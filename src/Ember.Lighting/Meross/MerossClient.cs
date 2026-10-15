using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Meross;

public sealed record MerossAccount(string Token, string Key, string Domain)
{
    /// <summary>The session as one string for Windows Credential Manager (it holds no password).</summary>
    public string Serialise() => JsonSerializer.Serialize(this);

    public static MerossAccount? Restore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var a = JsonSerializer.Deserialize<MerossAccount>(text);
            return a is { Token.Length: > 0, Key.Length: > 0, Domain.Length: > 0 } && a.Domain.StartsWith("https://", StringComparison.Ordinal) ? a : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed record MerossDevice(string Uuid, string Name, string Type, bool Online)
{
    /// <summary>Meross bulbs and light strips are the "msl" models. Ember uses nothing else (no plugs, hubs or thermostats).</summary>
    public bool IsLight => Type.StartsWith("msl", StringComparison.OrdinalIgnoreCase);
}

public sealed class MerossException(string message) : Exception(message);

/// <summary>
/// Meross's account service, used only to sign in and list the devices on the account (and to get the key each device checks
/// signed commands against). Lights are then controlled on the local network, never through the cloud. The password is sent
/// to Meross as an MD5 hash, the way the Meross app does, and is never kept: only the session Meross returns is saved.
/// </summary>
public sealed class MerossCloudClient(HttpClient http)
{
    public const string DefaultDomain = "https://iotx-eu.meross.com";
    const string Secret = "23x17ahWarFH6w29";
    const int RedirectRegion = 1030, WrongCredentials = 1004, UnknownAccount = 1002, TooManyTokens = 1301, WrongMfaCode = 1032, MfaRequired = 1033,
        TokenInvalid = 1019, TokenError = 1022, TokenExpired = 1200;

    public static string Md5Hex(string text) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>The signature the account service wants: MD5 of the secret, timestamp, nonce and the encoded parameters.</summary>
    public static string Sign(long timestampMs, string nonce, string encodedParams) => Md5Hex(Secret + timestampMs + nonce + encodedParams);

    /// <summary>Signs in. An account with two-step verification needs the current code from its authenticator app as <paramref name="mfaCode"/>.</summary>
    public async Task<MerossAccount> SignInAsync(string email, string password, string? mfaCode = null, CancellationToken ct = default)
    {
        var domain = DefaultDomain;
        for (var hop = 0; hop < 3; hop++)
        {
            var (status, data) = await PostAsync(domain, "v1/Auth/signIn", null, SignInParams(email, password, mfaCode), ct).ConfigureAwait(false);
            if (status == RedirectRegion && data.TryGetProperty("domain", out var d) && d.GetString() is { } next && IsMerossHost(next)) { domain = next.StartsWith("http", StringComparison.Ordinal) ? next : "https://" + next; continue; }
            RejectOnStatus(status);
            return ParseAccount(data, domain);
        }
        throw new MerossException("Meross kept sending Ember to another region.");
    }

    public static Dictionary<string, object> SignInParams(string email, string password, string? mfaCode)
    {
        var p = new Dictionary<string, object>
        {
            ["email"] = email,
            ["password"] = Md5Hex(password),
            ["accountCountryCode"] = "us",
            ["encryption"] = 1,
            ["agree"] = 1,
            ["mobileInfo"] = new { deviceModel = "Ember", mobileOsVersion = "11", mobileOs = "Windows", uuid = "ember", carrier = "" },
        };
        if (!string.IsNullOrWhiteSpace(mfaCode)) p["mfaCode"] = mfaCode.Trim();
        return p;
    }

    public async Task<IReadOnlyList<MerossDevice>> GetDevicesAsync(MerossAccount account, CancellationToken ct = default)
    {
        var (status, data) = await PostAsync(account.Domain, "v1/Device/devList", account.Token, new { }, ct).ConfigureAwait(false);
        RejectOnStatus(status);
        return ParseDevices(data);
    }

    static bool IsMerossHost(string domain)
    {
        var host = domain.Replace("https://", "", StringComparison.Ordinal).TrimEnd('/');
        return host.EndsWith(".meross.com", StringComparison.OrdinalIgnoreCase) && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
    }

    static void RejectOnStatus(int status)
    {
        if (status == 0) return;
        throw new MerossException(status switch
        {
            WrongCredentials or UnknownAccount => "Meross did not accept that email and password.",
            TooManyTokens => "Meross says too many sign-ins were made. Wait a while and try again.",
            MfaRequired => "This Meross account uses two-step verification. Type the current code from your authenticator app in the code box and sign in again.",
            WrongMfaCode => "Meross did not accept that two-step code. Codes change every 30 seconds: type the current one.",
            TokenInvalid or TokenError or TokenExpired => "Meross no longer accepts the saved sign-in. Sign in again in Settings.",
            _ => $"Meross answered with error {status}.",
        });
    }

    async Task<(int Status, JsonElement Data)> PostAsync(string domain, string path, string? token, object parameters, CancellationToken ct)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(parameters));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var body = JsonSerializer.Serialize(new { @params = encoded, sign = Sign(timestamp, nonce, encoded), timestamp, nonce });
        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(domain), path))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", token is null ? "Basic" : "Basic " + token);
        req.Headers.TryAddWithoutValidation("vender", "meross");
        req.Headers.TryAddWithoutValidation("AppVersion", "3.20.0");
        req.Headers.TryAddWithoutValidation("AppLanguage", "EN");
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new MerossException($"Meross answered {(int)res.StatusCode}.");
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > 1_000_000) throw new MerossException("Meross sent an unexpectedly large answer.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var status = doc.RootElement.TryGetProperty("apiStatus", out var s) && s.TryGetInt32(out var n) ? n : -1;
            var data = doc.RootElement.TryGetProperty("data", out var dd) ? dd.Clone() : default;
            return (status, data);
        }
        catch (JsonException) { throw new MerossException("Meross sent an answer Ember could not read."); }
    }

    public static MerossAccount ParseAccount(JsonElement data, string domain)
    {
        if (data.ValueKind != JsonValueKind.Object) throw new MerossException("Meross sent no sign-in details.");
        var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
        var key = data.TryGetProperty("key", out var k) ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(key) || token.Length > 200 || key.Length > 100) throw new MerossException("Meross sent no sign-in details.");
        return new MerossAccount(token, key, domain);
    }

    public static IReadOnlyList<MerossDevice> ParseDevices(JsonElement data)
    {
        var list = new List<MerossDevice>();
        if (data.ValueKind != JsonValueKind.Array) return list;
        foreach (var d in data.EnumerateArray().Take(100))
        {
            if (d.ValueKind != JsonValueKind.Object) continue;
            var uuid = d.TryGetProperty("uuid", out var u) ? u.GetString() : null;
            var type = d.TryGetProperty("deviceType", out var ty) ? ty.GetString() : null;
            if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(type) || uuid.Length > 64 || type.Length > 32) continue;
            var name = d.TryGetProperty("devName", out var n) ? n.GetString() : null;
            var online = d.TryGetProperty("onlineStatus", out var o) && o.ValueKind == JsonValueKind.Number && o.GetInt32() == 1;
            list.Add(new MerossDevice(uuid, string.IsNullOrWhiteSpace(name) ? "Meross " + type : name!, type, online));
        }
        return list;
    }
}

/// <summary>Talks to one Meross device on the local network: a signed JSON message posted to http://address/config.</summary>
public sealed class MerossLocalClient(HttpClient http)
{
    /// <summary>The message the device expects. The signature is MD5 of the message id, the account key and the timestamp.</summary>
    public static string BuildMessage(string ns, string method, object payload, string key, string address, DateTimeOffset now, string? messageId = null)
    {
        messageId ??= Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var seconds = now.ToUnixTimeSeconds();
        return JsonSerializer.Serialize(new
        {
            header = new
            {
                messageId,
                @namespace = ns,
                method,
                payloadVersion = 1,
                from = $"http://{address}/config",
                timestamp = seconds,
                timestampMs = now.Millisecond,
                sign = MerossCloudClient.Md5Hex(messageId + key + seconds),
            },
            payload,
        });
    }

    public async Task<JsonElement> SendAsync(string address, string key, string ns, string method, object payload, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{address}/config")
        {
            Content = new StringContent(BuildMessage(ns, method, payload, key, address, DateTimeOffset.UtcNow), Encoding.UTF8, "application/json"),
        };
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new MerossException($"The device answered {(int)res.StatusCode}.");
        var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > 200_000) throw new MerossException("The device sent an unexpectedly large answer.");
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("header", out var h) && h.TryGetProperty("method", out var m) && m.GetString() == "ERROR")
                throw new MerossException("The device refused the command (is the account key right?).");
            return root.TryGetProperty("payload", out var p) ? p.Clone() : default;
        }
        catch (JsonException) { throw new MerossException("The device sent an answer Ember could not read."); }
    }

    public Task<JsonElement> SetPowerAsync(string address, string key, bool on, CancellationToken ct = default) =>
        SendAsync(address, key, "Appliance.Control.ToggleX", "SET", new { togglex = new { channel = 0, onoff = on ? 1 : 0 } }, ct);

    public Task<JsonElement> SetColourAsync(string address, string key, byte r, byte g, byte b, CancellationToken ct = default) =>
        SendAsync(address, key, "Appliance.Control.Light", "SET", new { light = new { channel = 0, capacity = 5, rgb = (r << 16) | (g << 8) | b, luminance = 100 } }, ct);

    // Meross devices on Wi-Fi power-save take 2 s or more to answer the first message after being idle.
    /// <summary>Read only: asks a plug what it is drawing. Nothing is ever switched.</summary>
    public Task<JsonElement> GetElectricityAsync(string address, string key, CancellationToken ct = default) =>
        SendAsync(address, key, "Appliance.Control.Electricity", "GET", new { electricity = new { channel = 0 } }, ct);

    /// <summary>The draw in watts from an Electricity answer (Meross sends milliwatts), or null when the plug does not report power.</summary>
    public static double? WattsOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("electricity", out var e) && e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty("power", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var mw) && mw >= 0 ? mw / 1000 : null;

    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    /// <summary>The uuid a device at this address reports, or null when nothing there answers like a Meross device.</summary>
    public async Task<string?> ProbeAsync(string address, string key, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            var payload = await SendAsync(address, key, "Appliance.System.All", "GET", new { }, timeout.Token).ConfigureAwait(false);
            return UuidOf(payload);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is MerossException or HttpRequestException or OperationCanceledException) { return null; }
    }

    public static string? UuidOf(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("all", out var all) && all.TryGetProperty("system", out var sys)
        && sys.TryGetProperty("hardware", out var hw) && hw.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
}
