using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Ember.Core;

namespace Ember.App;

/// <summary>Closes the fan-control programs Ember detected, and restarts Ember as administrator when it needs the rights to.</summary>
public static class CompetitorCloser
{
    public sealed record Result(IReadOnlyList<string> Closed, IReadOnlyList<string> NeedAdmin);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Asks each matching program's window to close, and ends it if it is still there after 3 s. Only programs on
    /// the fixed competitor list are touched. A program that belongs to another user or runs elevated cannot be
    /// closed from a normal app: it is reported so the user can restart Ember as administrator.
    /// </summary>
    public static async Task<Result> CloseAsync(IReadOnlyList<string> displayNames)
    {
        var wanted = CompetitorList.Default.ProcessNamesFor(displayNames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var closed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var needAdmin = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<Process>();
        foreach (var p in Process.GetProcesses())
        {
            if (wanted.Contains(p.ProcessName)) targets.Add(p);
            else p.Dispose();
        }
        foreach (var p in targets)
        {
            try
            {
                try { p.CloseMainWindow(); } catch (Exception) { /* no window */ }
                if (!await Task.Run(() => p.WaitForExit(3000)).ConfigureAwait(false)) p.Kill(entireProcessTree: true);
                closed.Add(p.ProcessName);
            }
            catch (Win32Exception) { needAdmin.Add(p.ProcessName); } // access denied
            catch (InvalidOperationException) { closed.Add(p.ProcessName); } // already gone
            finally { p.Dispose(); }
        }
        return new Result(closed.ToList(), needAdmin.Except(closed).ToList());
    }

    /// <summary>Starts a new elevated copy of the app and returns true; false if the user declined the prompt.</summary>
    public static bool RestartAsAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" })?.Dispose();
            return true;
        }
        catch (Win32Exception) { return false; } // the user cancelled the UAC prompt
    }
}
