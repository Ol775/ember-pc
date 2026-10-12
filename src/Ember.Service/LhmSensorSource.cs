using Ember.Core;
using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

// Reading sensors never writes: the library and the PawnIO driver only probe chip registers here.
// Fan writes happen only in LhmFanController.
internal sealed class LhmSensorSource(LhmHardware hardware) : ISensorSource
{
    public IReadOnlyList<SensorReading> Read()
    {
        var result = new List<SensorReading>();
        lock (hardware.Gate)
            foreach (var hw in hardware.Computer.Hardware) Collect(hw, result);
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

    public void Dispose() { } // the shared LhmHardware owns the Computer
}
