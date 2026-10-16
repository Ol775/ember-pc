namespace Ember.Core;

/// <summary>
/// Where Ember keeps its per-user files, and whether it runs as a simulation. Both come from the environment so tests can run the whole
/// app on a scratch folder with no devices, no network and no service: EMBER_DATA_DIR replaces the roaming folder, EMBER_SIMULATE=1 leaves
/// out everything that talks to hardware, the network or the Ember service.
/// </summary>
public static class AppData
{
    public static string Roaming =>
        Environment.GetEnvironmentVariable("EMBER_DATA_DIR") is { Length: > 0 } dir ? dir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static bool Simulated => Environment.GetEnvironmentVariable("EMBER_SIMULATE") == "1";
}
