using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Ember.Core;

/// <summary>A release as the checker understands it: a version, whether it is a pre-release and where to read about it.</summary>
public sealed record ReleaseInfo(AppVersion Version, string Tag, bool Prerelease, string Url, string? Notes);

/// <summary>Major.minor.patch with an optional pre-release label ("beta.2"). A release sorts above any pre-release of the same numbers.</summary>
public sealed record AppVersion(int Major, int Minor, int Patch, string? Pre) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = new AppVersion(0, 0, 0, null);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
        var s = text.Trim();
        if (s[0] is 'v' or 'V') s = s[1..];
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; if (pre.Length == 0) return false; }
        var parts = s.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        var n = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]) || n[i] > 100_000) return false;
        version = new AppVersion(n[0], n[1], n[2], pre);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;
        var c = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (c != 0) return c;
        if (Pre is null) return other.Pre is null ? 0 : 1;
        if (other.Pre is null) return -1;
        var (a, b) = (Pre.Split('.'), other.Pre.Split('.'));
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var (x, y) = (int.TryParse(a[i], out var xi), int.TryParse(b[i], out var yi));
            c = x && y ? xi.CompareTo(yi) : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Pre is null ? "" : "-" + Pre);
}

public enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

public sealed record UpdateResult(UpdateStatus Status, ReleaseInfo? Release, string Message);

public static class UpdateRules
{
    /// <summary>Releases from a GitHub "list releases" reply. Drafts and anything malformed are skipped; never throws.</summary>
    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        var list = new List<ReleaseInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (Bool(e, "draft")) continue;
                if (Str(e, "tag_name") is not { } tag || !AppVersion.TryParse(tag, out var v)) continue;
                var url = Str(e, "html_url");
                if (url is null || !IsGitHubUrl(url)) continue;
                var notes = Str(e, "body");
                list.Add(new ReleaseInfo(v, tag, Bool(e, "prerelease") || v.Pre is not null, url, notes is { Length: > 600 } ? notes[..600] : notes));
            }
        }
        catch (JsonException) { /* an unreadable reply is "no releases" */ }
        return list;
    }

    /// <summary>The newest release newer than <paramref name="current"/> that the channel allows (Stable ignores pre-releases).</summary>
    public static ReleaseInfo? Newer(IEnumerable<ReleaseInfo> releases, AppVersion current, UpdateChannel channel) =>
        releases.Where(r => channel == UpdateChannel.Beta || !r.Prerelease)
            .Where(r => r.Version.CompareTo(current) > 0)
            .OrderByDescending(r => r.Version).FirstOrDefault();

    // The link is opened in the user's browser on request: only ever a github.com page.
    static bool IsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com";

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}

/// <summary>
/// Asks GitHub whether a newer release exists. It only reads release metadata and never downloads or installs anything:
/// updating is a manual step the user takes from the release page. Nothing is sent but the request itself.
/// </summary>
public sealed class UpdateChecker(HttpClient http, string releasesUrl = UpdateChecker.DefaultUrl)
{
    public const string DefaultUrl = "https://api.github.com/repos/Ol775/ember-pc/releases?per_page=20";
    const int MaxBytes = 1_000_000;

    public async Task<UpdateResult> CheckAsync(AppVersion current, UpdateChannel channel, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
            req.Headers.UserAgent.ParseAdd("Ember-update-check");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NotFound)
                return new UpdateResult(UpdateStatus.NoReleases, null, "No releases are published yet.");
            if (!res.IsSuccessStatusCode)
                return new UpdateResult(UpdateStatus.Failed, null, $"GitHub answered {(int)res.StatusCode}. Try again later.");
            if (res.Content.Headers.ContentLength > MaxBytes)
                return new UpdateResult(UpdateStatus.Failed, null, "The reply was too large to read.");
            var body = await ReadLimitedAsync(res, ct).ConfigureAwait(false);
            var releases = UpdateRules.Parse(body);
            if (releases.Count == 0) return new UpdateResult(UpdateStatus.NoReleases, null, "No releases are published yet.");
            return UpdateRules.Newer(releases, current, channel) is { } r
                ? new UpdateResult(UpdateStatus.Available, r, $"Ember {r.Version} is available.")
                : new UpdateResult(UpdateStatus.UpToDate, null, "You have the latest version.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return new UpdateResult(UpdateStatus.Failed, null, "Could not reach GitHub. Check your connection.");
        }
    }

    static async Task<string> ReadLimitedAsync(HttpResponseMessage res, CancellationToken ct)
    {
        await using var s = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[16_384];
        int n;
        while ((n = await s.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > MaxBytes) throw new InvalidDataException("reply too large");
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
