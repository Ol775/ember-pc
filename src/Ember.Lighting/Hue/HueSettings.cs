using System.Net;
using System.Text.Json;

namespace Ember.Lighting.Hue;

/// <summary>
/// Which bridge Ember talks to and the certificate it showed when it was linked. The application key is not here: it is kept in
/// Windows Credential Manager (see <see cref="ISecretStore"/>).
/// </summary>
public sealed record HueSettings(string Host, string? CertThumbprint)
{
    public const string KeyName = "hue-app-key";

    /// <summary>A bridge is reached by IP address or a plain host name, nothing that could carry a path or a port.</summary>
    public static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) && host.Length <= 253 && (IPAddress.TryParse(host, out _) || host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'));
}

public sealed class HueSettingsStore(string path)
{
    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "hue.json");

    public HueSettings? Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4096) return null;
            var s = JsonSerializer.Deserialize<HueSettings>(File.ReadAllText(path));
            return s is not null && HueSettings.IsValidHost(s.Host) ? s : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Saves the settings, or removes the file when given null. False if the folder cannot be written.</summary>
    public bool TrySave(HueSettings? settings)
    {
        try
        {
            if (settings is null) { if (File.Exists(path)) File.Delete(path); return true; }
            if (!HueSettings.IsValidHost(settings.Host)) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(settings));
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Finds bridges through Signify's discovery service. This is the one call that leaves the home network, so it only happens when asked.</summary>
public static class HueDiscovery
{
    public const string Url = "https://discovery.meethue.com/";

    public static async Task<IReadOnlyList<string>> FindAsync(HttpClient http, CancellationToken ct = default)
    {
        using var res = await http.GetAsync(Url, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return ParseAddresses(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    public static IReadOnlyList<string> ParseAddresses(string body)
    {
        var found = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var item in doc.RootElement.EnumerateArray())
                if (item.TryGetProperty("internalipaddress", out var ip) && IPAddress.TryParse(ip.GetString(), out var addr) && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    found.Add(addr.ToString());
        }
        catch (Exception) { /* not a list of bridges */ }
        return found.Distinct().Take(8).ToList();
    }
}
