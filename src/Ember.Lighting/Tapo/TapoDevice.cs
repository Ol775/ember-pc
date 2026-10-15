using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ember.Lighting.Tapo;

/// <summary>The TP-Link / Tapo account the devices were set up with. Devices check commands against it; it is only ever sent to devices on this network.</summary>
public sealed record TapoAccount(string Email, string Password)
{
    public string Serialise() => JsonSerializer.Serialize(this);

    public static TapoAccount? Restore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var a = JsonSerializer.Deserialize<TapoAccount>(text);
            return a is { Email.Length: > 0, Password.Length: > 0 } ? a : null;
        }
        catch (JsonException) { return null; }
    }
}

public sealed class TapoException(string message) : Exception(message);

/// <summary>
/// The "KLAP" session Tapo devices use on the local network: two handshake requests prove both sides know the account, then every
/// request is AES-128-CBC encrypted and signed with keys derived from the two random seeds. Pure functions, so they can be tested.
/// </summary>
public sealed class KlapSession
{
    readonly byte[] _key, _iv, _sig;
    int _seq;

    KlapSession(byte[] local, byte[] remote, byte[] authHash)
    {
        _key = Sha256(Tag("lsk"), local, remote, authHash)[..16];
        var iv = Sha256(Tag("iv"), local, remote, authHash);
        _iv = iv[..12];
        _seq = BinaryPrimitives.ReadInt32BigEndian(iv.AsSpan(iv.Length - 4));
        _sig = Sha256(Tag("ldk"), local, remote, authHash)[..28];
    }

    /// <summary>The account hash for each firmware generation: older devices use MD5, newer ones SHA-1 and SHA-256.</summary>
    public static byte[] AuthHashV1(string email, string password) => MD5.HashData([.. MD5.HashData(Encoding.UTF8.GetBytes(email)), .. MD5.HashData(Encoding.UTF8.GetBytes(password))]);
    public static byte[] AuthHashV2(string email, string password) => SHA256.HashData([.. SHA1.HashData(Encoding.UTF8.GetBytes(email)), .. SHA1.HashData(Encoding.UTF8.GetBytes(password))]);

    /// <summary>What the device sends back in handshake 1 to prove it knows the account.</summary>
    public static byte[] ServerProof(bool v2, byte[] local, byte[] remote, byte[] authHash) => v2 ? Sha256(local, remote, authHash) : Sha256(local, authHash);

    /// <summary>What the client sends in handshake 2 to prove the same.</summary>
    public static byte[] ClientProof(bool v2, byte[] local, byte[] remote, byte[] authHash) => v2 ? Sha256(remote, local, authHash) : Sha256(remote, authHash);

    public static KlapSession Create(byte[] local, byte[] remote, byte[] authHash) => new(local, remote, authHash);

    public (byte[] Body, int Seq) Encrypt(byte[] plain)
    {
        _seq = unchecked(_seq + 1);
        var seq = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(seq, _seq);
        var cipher = Aes(seq).EncryptCbc(plain, IvFor(seq), PaddingMode.PKCS7);
        return ([.. Sha256(_sig, seq, cipher), .. cipher], _seq);
    }

    public byte[] Decrypt(int seq, byte[] body)
    {
        if (body.Length <= 32) throw new TapoException("The device sent a reply that is too short.");
        var seqBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(seqBytes, seq);
        try { return Aes(seqBytes).DecryptCbc(body.AsSpan(32), IvFor(seqBytes), PaddingMode.PKCS7); }
        catch (CryptographicException) { throw new TapoException("The device sent a reply Ember could not decrypt."); }
    }

    byte[] IvFor(byte[] seq) => [.. _iv, .. seq];

    Aes Aes(byte[] _)
    {
        var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _key;
        return aes;
    }

    static byte[] Sha256(params byte[][] parts)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var p in parts) h.AppendData(p);
        return h.GetHashAndReset();
    }

    static byte[] Tag(string text) => Encoding.ASCII.GetBytes(text);
}

/// <summary>What the scan learned about one Tapo device.</summary>
public sealed record TapoInfo(string Id, string Name, string Model, string Type, bool On, bool Colour, bool ColourTemperature)
{
    public bool IsLight => Type.Contains("BULB", StringComparison.OrdinalIgnoreCase) || Type.Contains("LIGHT", StringComparison.OrdinalIgnoreCase) || Model.StartsWith('L');
    public bool IsPlug => Type.Contains("PLUG", StringComparison.OrdinalIgnoreCase) && !IsLight;

    public static TapoInfo? Parse(JsonElement r)
    {
        if (r.ValueKind != JsonValueKind.Object) return null;
        string? Str(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var id = Str("device_id");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 80) return null;
        var model = Str("model") ?? "Tapo";
        var type = Str("type") ?? "";
        string? nick = null;
        try { if (Str("nickname") is { Length: > 0 } b64) nick = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch (FormatException) { }
        var name = string.IsNullOrWhiteSpace(nick) ? "Tapo " + model : nick.Length > 60 ? nick[..60] : nick;
        return new TapoInfo(id, name, model, type, r.TryGetProperty("device_on", out var on) && on.ValueKind == JsonValueKind.True,
            r.TryGetProperty("hue", out _), r.TryGetProperty("color_temp", out _));
    }
}

/// <summary>Talks to one Tapo device over KLAP. Sessions are re-opened when the device drops them.</summary>
public sealed class TapoDevice(HttpClient http, string address, Func<TapoAccount?> account)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    KlapSession? _session;
    string? _cookie;
    DateTime _opened;

    public string Address => address;

    /// <summary>True when something at this address starts a KLAP handshake: no account needed.</summary>
    public static async Task<bool> ProbeAsync(HttpClient http, string address, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{address}/app/handshake1") { Content = new ByteArrayContent(RandomNumberGenerator.GetBytes(16)) };
            using var res = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return false;
            return (await res.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false)).Length == 48;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException) { return false; }
    }

    async Task HandshakeAsync(TapoAccount acct, CancellationToken ct)
    {
        var local = RandomNumberGenerator.GetBytes(16);
        using var r1 = new HttpRequestMessage(HttpMethod.Post, $"http://{address}/app/handshake1") { Content = new ByteArrayContent(local) };
        using var res1 = await http.SendAsync(r1, ct).ConfigureAwait(false);
        if (!res1.IsSuccessStatusCode) throw new TapoException($"The device answered {(int)res1.StatusCode}.");
        var body = await res1.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (body.Length != 48) throw new TapoException("The device sent an unexpected handshake.");
        var remote = body[..16];
        var proof = body[16..];
        var cookie = res1.Headers.TryGetValues("Set-Cookie", out var cs) ? cs.FirstOrDefault()?.Split(';')[0] : null;

        byte[]? hash = null;
        var v2 = false;
        foreach (var useV2 in new[] { false, true })
        {
            var candidate = useV2 ? KlapSession.AuthHashV2(acct.Email, acct.Password) : KlapSession.AuthHashV1(acct.Email, acct.Password);
            if (CryptographicOperations.FixedTimeEquals(KlapSession.ServerProof(useV2, local, remote, candidate), proof)) { hash = candidate; v2 = useV2; break; }
        }
        if (hash is null) throw new TapoException("The device did not accept that Tapo email and password.");

        using var r2 = new HttpRequestMessage(HttpMethod.Post, $"http://{address}/app/handshake2") { Content = new ByteArrayContent(KlapSession.ClientProof(v2, local, remote, hash)) };
        if (cookie is not null) r2.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var res2 = await http.SendAsync(r2, ct).ConfigureAwait(false);
        if (!res2.IsSuccessStatusCode) throw new TapoException($"The device refused the sign-in ({(int)res2.StatusCode}).");
        _session = KlapSession.Create(local, remote, hash);
        _cookie = cookie;
        _opened = DateTime.UtcNow;
    }

    /// <summary>Sends one command and returns its "result". Errors the device reports become a <see cref="TapoException"/> carrying its code.</summary>
    public async Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken ct)
    {
        var acct = account() ?? throw new TapoException("No Tapo account is saved.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                if (_session is null || DateTime.UtcNow - _opened > TimeSpan.FromHours(20)) await HandshakeAsync(acct, ct).ConfigureAwait(false);
                try { return await SendAsync(method, parameters, ct).ConfigureAwait(false); }
                catch (TapoException ex) when (attempt == 0 && ex.Message.StartsWith("SESSION", StringComparison.Ordinal)) { _session = null; }
            }
        }
        finally { _gate.Release(); }
    }

    async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken ct)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new { method, @params = parameters, request_time_milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var (body, seq) = _session!.Encrypt(plain);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{address}/app/request?seq={seq}") { Content = new ByteArrayContent(body) };
        if (_cookie is not null) req.Headers.TryAddWithoutValidation("Cookie", _cookie);
        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (res.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized) throw new TapoException("SESSION expired");
        if (!res.IsSuccessStatusCode) throw new TapoException($"The device answered {(int)res.StatusCode}.");
        var reply = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (reply.Length > 200_000) throw new TapoException("The device sent an unexpectedly large answer.");
        try
        {
            using var doc = JsonDocument.Parse(_session.Decrypt(seq, reply));
            var root = doc.RootElement;
            var code = root.TryGetProperty("error_code", out var c) && c.TryGetInt32(out var n) ? n : -1;
            if (code != 0) throw new TapoException(code is 9999 or 1002 ? "SESSION error " + code : $"The device reported error {code}.");
            return root.TryGetProperty("result", out var r) ? r.Clone() : default;
        }
        catch (JsonException) { throw new TapoException("The device sent an answer Ember could not read."); }
    }

    public async Task<TapoInfo?> InfoAsync(CancellationToken ct) => TapoInfo.Parse(await CallAsync("get_device_info", null, ct).ConfigureAwait(false));

    /// <summary>Read only: the draw in watts of a plug that measures energy (Tapo sends milliwatts), or null when it does not.</summary>
    public async Task<double?> WattsAsync(CancellationToken ct)
    {
        try
        {
            var r = await CallAsync("get_energy_usage", null, ct).ConfigureAwait(false);
            return WattsOf(r);
        }
        catch (TapoException ex) when (ex.Message.StartsWith("The device reported error", StringComparison.Ordinal)) { return null; }
    }

    public static double? WattsOf(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object && result.TryGetProperty("current_power", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var mw) && mw >= 0 ? mw / 1000 : null;

    public Task SetOffAsync(CancellationToken ct) => CallAsync("set_device_info", new { device_on = false }, ct);

    /// <summary>Sets a bulb to one colour. A bulb that cannot show colour gets the brightness of the colour instead.</summary>
    public Task SetColourAsync(byte r, byte g, byte b, bool colour, CancellationToken ct)
    {
        var (h, s, v) = ToHsv(r, g, b);
        return colour
            ? CallAsync("set_device_info", new { device_on = true, hue = h, saturation = s, brightness = Math.Max(1, v), color_temp = 0 }, ct)
            : CallAsync("set_device_info", new { device_on = true, brightness = Math.Max(1, Math.Max(r, Math.Max(g, b)) * 100 / 255) }, ct);
    }

    /// <summary>Hue 0 to 360, saturation and brightness 0 to 100, the way Tapo bulbs take them.</summary>
    public static (int Hue, int Saturation, int Value) ToHsv(byte r, byte g, byte b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var d = max - Math.Min(rf, Math.Min(gf, bf));
        var h = d == 0 ? 0 : max == rf ? 60 * (((gf - bf) / d % 6 + 6) % 6) : max == gf ? 60 * ((bf - rf) / d + 2) : 60 * ((rf - gf) / d + 4);
        return ((int)Math.Round(h) % 360, max == 0 ? 0 : (int)Math.Round(d / max * 100), (int)Math.Round(max * 100));
    }
}
