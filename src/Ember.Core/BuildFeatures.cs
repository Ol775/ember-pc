namespace Ember.Core;

/// <summary>
/// What this build includes. This is the beta build: game detection and GPU fan control are beta features and are
/// switched on here. Setting <see cref="Channel"/> back to "alpha" switches both off again (the service then refuses
/// their messages and the app hides them).
/// </summary>
public sealed record BuildFeatures(bool GameDetection, bool GpuFan)
{
    public const string Channel = "beta";

    public static readonly BuildFeatures All = new(true, true);
    public static readonly BuildFeatures Alpha = new(false, false);

    public static BuildFeatures Current => Channel == "beta" ? All : Alpha;
}
