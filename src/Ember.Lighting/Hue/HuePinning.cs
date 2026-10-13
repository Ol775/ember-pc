using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Ember.Lighting.Hue;

/// <summary>
/// A Hue bridge shows a certificate signed by Signify's own authority, which Windows does not know. Instead of switching
/// certificate checks off, Ember remembers the bridge's certificate when the bridge is linked (the button press proves it is the
/// bridge you meant) and accepts only that certificate afterwards.
/// </summary>
public static class HuePinning
{
    /// <param name="pinned">The thumbprint remembered at linking, or empty while linking (then <paramref name="learned"/> is told the one seen).</param>
    public static HttpClient CreateClient(Func<string?> pinned, Action<string>? learned = null, TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, _) => Accept(cert, pinned(), learned),
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
    }

    public static bool Accept(X509Certificate2? cert, string? pinned, Action<string>? learned)
    {
        if (cert is null) return false;
        var seen = cert.Thumbprint;
        if (string.IsNullOrEmpty(pinned)) { learned?.Invoke(seen); return learned is not null; }
        return string.Equals(seen, pinned, StringComparison.OrdinalIgnoreCase);
    }
}
