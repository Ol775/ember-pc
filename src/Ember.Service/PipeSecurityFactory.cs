using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ember.Service;

public static class PipeSecurityFactory
{
    public static NamedPipeServerStream Create(string pipeName)
    {
        if (OperatingSystem.IsWindows()) return CreateWindows(pipeName);
        return new NamedPipeServerStream(pipeName, PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindows(string pipeName)
    {
        // LocalSystem (the service account) needs FullControl, not ReadWrite: creating each further
        // server instance needs CreateNewInstance (FILE_CREATE_PIPE_INSTANCE), which ReadWrite lacks.
        // Clients only read, so Interactive gets Read and no write or CreateNewInstance.
        var security = new PipeSecurity();
        void Allow(WellKnownSidType sid, PipeAccessRights rights) =>
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow));
        Allow(WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
        Allow(WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.ReadWrite);
        Allow(WellKnownSidType.InteractiveSid, PipeAccessRights.Read);
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }
}
