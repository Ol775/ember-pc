using System.Reflection;

namespace Ember.Core;

/// <summary>
/// What this build includes. A release without a label (1.0.0) is the production build: it holds only what has been checked on
/// real hardware, and hides the rest. A version with a label (1.1.0-beta.1) is the beta build: game detection and GPU fan control
/// and the Hue, Nanoleaf and Razer lighting are switched on, and the roadmap is shown. The service refuses the messages of a
/// feature the build does not have, and the app hides it.
/// </summary>
public sealed record BuildFeatures(bool GameDetection, bool GpuFan)
{
    /// <summary>"prod" for a version with no label, "beta" for one with a label such as "beta.3".</summary>
    public static string ChannelOf(string? informationalVersion)
    {
        var v = informationalVersion ?? "";
        var plus = v.IndexOf('+');
        if (plus >= 0) v = v[..plus];
        return v.Contains('-') ? "beta" : "prod";
    }

    /// <summary>The channel of this build, read from its own version (set in Directory.Build.props).</summary>
    public static readonly string Channel = ChannelOf(typeof(BuildFeatures).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    public static bool IsProduction => Channel == "prod";

    public static readonly BuildFeatures All = new(true, true);
    public static readonly BuildFeatures Production = new(false, false);

    public static BuildFeatures Current => IsProduction ? Production : All;
}
