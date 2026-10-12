namespace Ember.Service;

/// <summary>
/// Hands every controllable header back to the board, then checks. A header that still reports software mode
/// afterwards (or whose release failed) is forced to 100%: loud, but never stuck at a low speed with nothing watching.
/// Used by the watchdog, which opens a fresh hardware instance and cannot be sure a release takes effect on it.
/// </summary>
public static class FanRelease
{
    public static void ReleaseAll(IFanController fans, Action<string> log)
    {
        var failed = new HashSet<string>();
        foreach (var header in fans.Enumerate().Where(h => h.HasControl))
        {
            try { fans.ReleaseToAuto(header.Id); }
            catch (Exception ex)
            {
                failed.Add(header.Id);
                log($"Releasing {header.Id} failed: {ex.Message}");
            }
        }

        IReadOnlyList<FanHeader> after;
        try { after = fans.Enumerate(); }
        catch (Exception ex)
        {
            log($"Could not re-check headers after release: {ex.Message}");
            after = [];
        }
        foreach (var header in after.Where(h => h.HasControl && (h.InSoftwareMode || failed.Contains(h.Id))))
        {
            try
            {
                fans.SetPercent(header.Id, 100);
                log($"{header.Id} did not return to automatic control; set to 100% instead.");
            }
            catch (Exception ex) { log($"Forcing {header.Id} to 100% failed: {ex.Message}"); }
        }
        foreach (var id in failed.Except(after.Select(h => h.Id)))
        {
            try { fans.SetPercent(id, 100); } catch (Exception) { /* nothing more can be done */ }
        }
    }
}
