using Ember.Core;
using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

// Strictly read-only: this source has no write access to any hardware control.
internal sealed class LhmSensorSource : ISensorSource
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
    };

    public LhmSensorSource() => _computer.Open();

    public IReadOnlyList<SensorReading> Read()
    {
        var result = new List<SensorReading>();
        foreach (var hw in _computer.Hardware) Collect(hw, result);
        return result;
    }

    private static void Collect(IHardware hw, List<SensorReading> into)
    {
        hw.Update();
        foreach (var s in hw.Sensors)
        {
            var r = SensorMapper.Map(hw.HardwareType, s.SensorType, s.Identifier.ToString(), s.Name, s.Value);
            if (r is not null) into.Add(r);
        }
        foreach (var sub in hw.SubHardware) Collect(sub, into);
    }

    public void Dispose() => _computer.Close();
}
