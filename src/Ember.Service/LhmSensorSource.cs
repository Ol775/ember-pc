using Ember.Core;
using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

// Ember's own code never writes fan, PWM or control values; the library and PawnIO driver only probe chip registers to read sensors.
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

    // TEMP diagnostic for the GPU-fan-disappears bug: logs fan/control sensor state changes to ProgramData\Ember\fan-diag.log.
    private static readonly string DiagPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ember", "fan-diag.log");
    private static readonly Dictionary<string, string> LastState = new();

    private static void Diag(IHardware hw, ISensor s)
    {
        if (s.SensorType is not (SensorType.Fan or SensorType.Control)) return;
        var state = s.Value is null ? "null" : float.IsFinite(s.Value.Value) ? s.Value.Value.ToString("0.#") : "nonfinite";
        var key = s.Identifier.ToString();
        if (LastState.TryGetValue(key, out var old) && old == state) return;
        LastState[key] = state;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagPath)!);
            File.AppendAllText(DiagPath, $"{DateTime.Now:s} {hw.HardwareType} {s.SensorType} {s.Name} [{key}] {old ?? "-"} -> {state}\n");
        }
        catch { /* diagnostic only */ }
    }

    private static void Collect(IHardware hw, List<SensorReading> into)
    {
        hw.Update();
        foreach (var s in hw.Sensors)
        {
            Diag(hw, s);
            var r = SensorMapper.Map(hw.HardwareType, s.SensorType, s.Identifier.ToString(), s.Name, s.Value);
            if (r is not null) into.Add(r);
        }
        foreach (var sub in hw.SubHardware) Collect(sub, into);
    }

    public void Dispose() => _computer.Close();
}
