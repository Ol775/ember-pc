namespace Ember.Service;

/// <summary>
/// Hands headers back, the way this hardware needs it: write the given safe speed, then call the library's restore.
/// (On the owner's board the restore alone leaves the fan at its last speed.) If either step fails, force 100% so a
/// fan is never left low with nothing watching it. Used by the watchdog, which only touches headers Ember enabled.
/// </summary>
public static class FanRelease
{
    public static void ReleaseAll(IFanController fans, IReadOnlyDictionary<string, double> releaseValues, Action<string> log)
    {
        foreach (var (id, value) in releaseValues)
        {
            try
            {
                fans.SetPercent(id, value);
                fans.ReleaseToAuto(id);
            }
            catch (Exception ex)
            {
                log($"Releasing {id} failed ({ex.Message}); setting it to 100% instead.");
                try { fans.SetPercent(id, 100); }
                catch (Exception ex2) { log($"Forcing {id} to 100% failed: {ex2.Message}"); }
            }
        }
    }
}
