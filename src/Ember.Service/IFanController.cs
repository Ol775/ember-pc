namespace Ember.Service;

/// <summary>A fan header the board lets software read; <c>HasControl</c> says LibreHardwareMonitor exposes a control for it.</summary>
public sealed record FanHeader(string Id, string Name, bool HasControl, bool HasRpm, bool InSoftwareMode);

/// <summary>
/// The only way Ember writes fan values. Writes must stay within 0 to 100 and target a known header; releasing hands
/// the header back to the board's own automatic control.
/// </summary>
public interface IFanController : IDisposable
{
    IReadOnlyList<FanHeader> Enumerate();

    /// <exception cref="ArgumentOutOfRangeException">percent is NaN or outside 0..100.</exception>
    /// <exception cref="InvalidOperationException">unknown header.</exception>
    void SetPercent(string headerId, double percent);

    void ReleaseToAuto(string headerId);
}
