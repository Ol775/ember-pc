using System.Runtime.Versioning;
using Ember.Core;

namespace Ember.Service;

/// <summary>
/// Uses LibreHardwareMonitorLib's own public check, so Ember reports exactly what LHM sees. LHM 0.9.6 reads
/// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO once per process, so an install made
/// while the service runs shows up after a service restart (see docs/driver-research.md).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PawnIoProbe : IDriverProbe
{
    public DriverStatus Check()
    {
        try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled ? DriverStatus.Ok : DriverStatus.Missing; }
        catch (Exception) { return DriverStatus.Unknown; }
    }
}
