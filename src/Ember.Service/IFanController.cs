namespace Ember.Service;

/// <summary>A fan header the board lets software read; <c>HasControl</c> says LibreHardwareMonitor exposes a control for it.</summary>
public sealed record FanHeader(string Id, string Name, bool HasControl, bool HasRpm, bool InSoftwareMode);

/// <summary>
/// The only way Ember writes fan values. Writes must stay within 0 to 100 and target a known header.
/// <para>
/// There is no "return to automatic control": on the owner's board (NCT6687D) the library's restore call
/// (<c>SetDefault</c>, and the restore that <c>Computer.Close</c> runs) writes back a stale register value and left a
/// fan stuck at a low speed, so Ember never calls either. <see cref="Release"/> only ends Ember's management of a
/// header; the caller must already have written the safe speed (<c>ReleaseRules</c>). The fan then holds that speed
/// until Ember drives it again or the PC restarts, when the board takes over.
/// </para>
/// </summary>
public interface IFanController : IDisposable
{
    IReadOnlyList<FanHeader> Enumerate();

    /// <exception cref="ArgumentOutOfRangeException">percent is NaN or outside 0..100.</exception>
    /// <exception cref="InvalidOperationException">unknown header.</exception>
    void SetPercent(string headerId, double percent);

    /// <summary>Ends Ember's management of the header. Does not change the fan speed; write the safe speed first.</summary>
    void Release(string headerId);
}
