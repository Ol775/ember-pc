using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Ember.App;

/// <summary>
/// Restarts the Ember services (the watchdog first so it does not restart the service behind our back) without closing the app,
/// which reconnects on its own. Stopping the service hands the fans back to the board, so they are never left at a fixed speed.
/// Windows asks for permission when Ember is not running as administrator.
/// </summary>
public static class ServiceRestarter
{
    const string Script = "$ErrorActionPreference='Stop'; Stop-Service EmberWatchdog -Force; Stop-Service EmberService -Force; Start-Service EmberService; Start-Sleep -Seconds 2; Start-Service EmberWatchdog";

    /// <summary>Blocks until the services are back or the attempt failed; returns a sentence for the user.</summary>
    public static string Restart()
    {
        try
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + Script + "\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            using var p = Process.Start(psi);
            if (p is null) return "Could not start the restart.";
            if (!p.WaitForExit(90_000)) return "The restart is taking too long. Check the services in Windows.";
            return p.ExitCode == 0 ? "Services restarted. Ember reconnects by itself." : $"The restart failed (code {p.ExitCode}). Try again, or restart Ember.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return "Cancelled: Windows needs your permission to restart the services."; }
        catch (Exception ex) { return "Could not restart the services: " + ex.Message; }
    }
}
