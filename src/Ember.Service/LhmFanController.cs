using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

/// <summary>
/// Windows implementation over LibreHardwareMonitorLib. This class and nothing else calls <c>SetSoftware</c> and
/// <c>SetDefault</c>; sensor reads never write.
/// </summary>
internal sealed class LhmFanController(LhmHardware hardware) : IFanController
{
    public IReadOnlyList<FanHeader> Enumerate()
    {
        var result = new List<FanHeader>();
        lock (hardware.Gate)
            foreach (var hw in hardware.Computer.Hardware) Collect(hw, result);
        return result;
    }

    static void Collect(IHardware hw, List<FanHeader> into)
    {
        var ids = hw.Sensors.Select(s => s.Identifier.ToString()).ToHashSet();
        foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Control))
        {
            var id = s.Identifier.ToString();
            var rpmId = id.Replace("/control/", "/fan/", StringComparison.Ordinal);
            into.Add(new FanHeader(id, s.Name, s.Control is not null, ids.Contains(rpmId),
                s.Control?.ControlMode == ControlMode.Software));
        }
        foreach (var sub in hw.SubHardware) Collect(sub, into);
    }

    public void SetPercent(string headerId, double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        lock (hardware.Gate) Find(headerId).SetSoftware((float)percent);
    }

    public void ReleaseToAuto(string headerId)
    {
        lock (hardware.Gate) Find(headerId).SetDefault();
    }

    IControl Find(string headerId)
    {
        IControl? found = null;
        foreach (var hw in hardware.Computer.Hardware) found ??= Search(hw, headerId);
        return found ?? throw new InvalidOperationException($"Unknown fan header {headerId}.");
    }

    static IControl? Search(IHardware hw, string id)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == SensorType.Control && s.Control is not null && s.Identifier.ToString() == id) return s.Control;
        foreach (var sub in hw.SubHardware)
            if (Search(sub, id) is { } c) return c;
        return null;
    }

    public void Dispose() { } // the shared LhmHardware owns the Computer
}
