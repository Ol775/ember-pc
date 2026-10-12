using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Ember.Core;

/// <summary>
/// Opens the service pipe and checks that the process serving it is the installed EmberService before trusting it,
/// so a non-admin process that grabbed the name "ember.v1" first cannot feed the app fake data.
/// The service runs as LocalSystem (scripts/install-service.ps1). The identity proof is that the pipe server's PID
/// equals the PID the Service Control Manager reports for EmberService. A non-admin squatter cannot spoof that,
/// because only admins can create or control that service. Querying the service status needs only
/// SC_MANAGER_CONNECT and SERVICE_QUERY_STATUS, which the default service DACL grants to Interactive and Service logons; the app is interactive, so it works as a
/// normal user (windows-checklist c1 is the real test).
/// </summary>
[SupportedOSPlatform("windows")]
public static class PipeStreamConnector
{
    const string PipeName = EmberNames.PipeName;
    const string ServiceName = EmberNames.ServiceName;
    const int ConnectTimeoutMs = 1000;

    const uint ScManagerConnect = 0x0001;
    const uint ServiceQueryStatus = 0x0004;
    const int ScStatusProcessInfo = 0;
    const uint ServiceRunning = 0x00000004;

    public static async Task<Stream> ConnectAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Ember runs on Windows only.");
        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(ConnectTimeoutMs, ct).ConfigureAwait(false);
            // ServiceConnection treats any IOException as "service unavailable" and retries.
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid)
                || !TryQueryService(out var servicePid, out var running)
                || !ServerIdentity.IsTrusted(serverPid, servicePid, running))
                throw new IOException("Pipe server is not the Ember service");
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Fails closed: any SCM failure (service missing, access denied) returns false.
    static bool TryQueryService(out uint pid, out bool running)
    {
        pid = 0;
        running = false;
        var scm = OpenSCManagerW(null, null, ScManagerConnect);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var service = OpenServiceW(scm, ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero) return false;
            try
            {
                var size = (uint)Marshal.SizeOf<ServiceStatusProcess>();
                if (!QueryServiceStatusEx(service, ScStatusProcessInfo, out var status, size, out _)) return false;
                pid = status.ProcessId;
                running = status.CurrentState == ServiceRunning;
                return true;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess buffer,
        uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseServiceHandle(IntPtr handle);
}
