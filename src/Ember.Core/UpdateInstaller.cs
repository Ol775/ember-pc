using System.Security.Cryptography;

namespace Ember.Core;

public sealed record DownloadResult(bool Ok, string? Path, string Message, string? Sha256 = null);

/// <summary>
/// Downloads the setup program of a release the checker found, only when the person asks, and checks it against the release's
/// published SHA-256 before it is handed back. The checksum proves the file is the one that was released, not who made it.
/// </summary>
public sealed class UpdateInstaller(HttpClient http)
{
    public const long MaxInstallerBytes = 300_000_000;

    public async Task<DownloadResult> DownloadAsync(ReleaseInfo release, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.InstallerUrl is not { } url || release.HashUrl is not { } hashUrl)
            return new(false, null, "This release has no installer with a checksum. Open the release page instead.");
        if (!UpdateRules.IsDownloadUrl(url) || !UpdateRules.IsDownloadUrl(hashUrl))
            return new(false, null, "The download address is not an Ember release.");
        try
        {
            var expected = ParseHash(await GetTextAsync(hashUrl, ct).ConfigureAwait(false));
            if (expected is null) return new(false, null, "The checksum file could not be read.");

            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Ember-Setup-" + release.Version + ".exe");
            var part = path + ".part";
            using (var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!res.IsSuccessStatusCode) { DiagLog.Warn("Update", $"Installer download answered {(int)res.StatusCode}: {url}"); return new(false, null, $"GitHub answered {(int)res.StatusCode} for the installer. Try again later, or download it from the release page."); }
                var total = res.Content.Headers.ContentLength;
                if (total > MaxInstallerBytes) return new(false, null, "The installer is larger than expected, so it was not downloaded.");
                await using var src = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81_920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    read += n;
                    if (read > MaxInstallerBytes) { dst.Close(); File.Delete(part); return new(false, null, "The installer is larger than expected, so it was stopped."); }
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    if (total > 0) progress?.Report((double)read / total.Value);
                }
            }
            string actual;
            await using (var f = File.OpenRead(part)) actual = Convert.ToHexString(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(part);
                return new(false, null, "The download does not match its checksum, so it was thrown away.");
            }
            File.Move(part, path, true);
            return new(true, path, "Downloaded and checked.", expected);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { DiagLog.Warn("Update", "Installer download failed", ex); return new(false, null, "Could not download the update (" + ex.Message + "). Check your connection."); }
    }

    async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength > 4096) throw new InvalidDataException("checksum file too large");
        return await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the downloaded setup program so that nothing can change or replace it while it is open, and checks it against the published hash once more.
    /// Keep the stream open until the installer has started: the file is then the one that was checked. Null when the file is gone or no longer matches.
    /// </summary>
    public static FileStream? OpenChecked(string path, string sha256)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            stream.Position = 0;
            if (string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase)) return stream;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        stream?.Dispose();
        return null;
    }

    /// <summary>The 64-digit hash at the start of a "hash  filename" line, upper-cased; null when it is not one.</summary>
    public static string? ParseHash(string text)
    {
        var first = text.Split([' ', '\t', '\r', '\n', '*'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToUpperInvariant() : null;
    }
}
