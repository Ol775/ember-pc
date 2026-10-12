using LibreHardwareMonitor.Hardware;

namespace Ember.Service;

/// <summary>
/// The one LibreHardwareMonitor <see cref="Computer"/> for this process. Sensor reads and fan writes share it, and
/// every use must hold <see cref="Gate"/>, because the library is not thread-safe.
/// </summary>
internal sealed class LhmHardware : IDisposable
{
    public Computer Computer { get; } = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
    };

    public object Gate { get; } = new();

    public LhmHardware() => Computer.Open();

    /// <summary>The motherboard model and library version a header test result is valid for.</summary>
    public FanControlIdentity Identity()
    {
        lock (Gate)
        {
            var board = Computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.Name ?? "unknown";
            return new FanControlIdentity(board, typeof(Computer).Assembly.GetName().Version?.ToString() ?? "unknown");
        }
    }

    public void Dispose()
    {
        lock (Gate) Computer.Close();
    }
}
