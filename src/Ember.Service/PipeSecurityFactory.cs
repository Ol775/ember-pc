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
        var security = new PipeSecurity();
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.InteractiveSid })
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }
}
