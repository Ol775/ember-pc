namespace Ember.Core;

/// <summary>
/// What "hand a fan back" means on this hardware. LibreHardwareMonitor's restore call on the NCT6687D does not make the
/// board resume automatic control (checked on the owner's MSI B550-A PRO: the fan stays at its last speed), so Ember
/// writes a known-safe speed first. System fans go back to the speed they had before Ember's first write (the board
/// holds them at a flat value). The CPU fan's board speed follows temperature and cannot be restored by a number, so
/// it is left at 100%, which is loud but safe. With no saved speed the answer is also 100%.
/// </summary>
public static class ReleaseRules
{
    public const double SystemFloor = 20, CpuFloor = 30;

    public static double ValueFor(string headerName, double? originalPercent)
    {
        if (headerName == "CPU Fan") return 100;
        if (originalPercent is not { } p || !double.IsFinite(p) || p is < 0 or > 100) return 100;
        return Math.Max(SystemFloor, p);
    }
}
