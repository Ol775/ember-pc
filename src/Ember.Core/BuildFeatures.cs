namespace Ember.Core;

/// <summary>
/// What this build includes. This is the alpha build: game detection and GPU fan control are beta features, so they
/// are switched off here (the service refuses their messages and the app hides them). Changing <see cref="Channel"/>
/// to "beta" switches both on.
/// </summary>
public sealed record BuildFeatures(bool GameDetection, bool GpuFan)
{
    public const string Channel = "alpha";

    public static readonly BuildFeatures All = new(true, true);
    public static readonly BuildFeatures Alpha = new(false, false);

    public static BuildFeatures Current => Channel == "beta" ? All : Alpha;
}
