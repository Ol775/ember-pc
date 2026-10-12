using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Ember.App;

/// <summary>
/// Opens the service pipe and checks that the process serving it runs as LocalSystem before trusting it,
/// so a non-admin process that grabbed the name "ember.v1" first cannot feed the app fake data.
/// The real service runs as LocalSystem: scripts/install-service.ps1 registers it with New-Service, which uses
/// the LocalSystem account by default.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PipeStreamConnector
{
    const string PipeName = "ember.v1";
    const int ConnectTimeoutMs = 1000;
    const uint ProcessQueryLimitedInformation = 0x1000;
    const uint TokenQuery = 0x0008;

    public static async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Ember runs on Windows only.");
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
            // ServiceConnection treats any IOException as "service unavailable" and retries.
            if (!IsServedByLocalSystem(pipe.SafePipeHandle)) throw new IOException("Pipe server is not the Ember service");
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Fails closed: any API failure (including access denied) counts as "not the Ember service".
    static bool IsServedByLocalSystem(SafePipeHandle pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe, out var pid)) return false;
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process.IsInvalid) return false;
        if (!OpenProcessToken(process, TokenQuery, out var token)) { token.Dispose(); return false; }
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            return identity.User is { } sid && sid.IsWellKnown(WellKnownSidType.LocalSystemSid);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);
}
