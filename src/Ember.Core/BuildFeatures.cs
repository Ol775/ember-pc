using System.Reflection;

namespace Ember.Core;

/// <summary>
/// What this build includes. A release without a label (1.0.0) is the production build: it holds only what has been checked on
/// real hardware (plus a few features marked Beta, such as game detection and the Meross and Tapo integrations), and hides the
/// rest. A version with a label (1.2.0-beta.1) is the beta build: GPU fan control and the Stream Deck plugin are switched on too,
/// and the roadmap is shown. The service refuses the messages of a feature the build does not have, and the app hides it.
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

    /// <summary>The redesigned look (rounder cards, icons in the sidebar, ring gauges). Every build has it from 1.4.0; the classic look is gone.</summary>
    public static bool NewLook => true;

    public static readonly BuildFeatures All = new(true, true);
    /// <summary>Game detection is in the production build, marked Beta and behind the Experimental switch. GPU fan control is not.</summary>
    public static readonly BuildFeatures Production = new(true, false);

    public static BuildFeatures Current => IsProduction ? Production : All;
}
