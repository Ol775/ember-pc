using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ember.Service;

public static class PipeSecurityFactory
{
    public static NamedPipeServerStream Create(string pipeName, bool firstInstance)
    {
        // FirstPipeInstance throws if the name already exists, so a squatter cannot pre-create it.
        // Later instances must not set it (they would fail).
        var options = firstInstance ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous;
        if (OperatingSystem.IsWindows()) return CreateWindows(pipeName, options);
        return new NamedPipeServerStream(pipeName, PipeDirection.Out,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options);
    }

    /// <summary>The control pipe: two-way, Interactive users may read and write (the server validates every message).</summary>
    public static NamedPipeServerStream CreateControl(string pipeName, bool firstInstance)
    {
        var options = firstInstance ? PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance : PipeOptions.Asynchronous;
        if (!OperatingSystem.IsWindows())
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, options);
        return CreateWindows(pipeName, options, PipeDirection.InOut, PipeAccessRights.ReadWrite);
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateWindows(string pipeName, PipeOptions options,
        PipeDirection direction = PipeDirection.Out, PipeAccessRights interactive = PipeAccessRights.Read)
    {
        // LocalSystem (the service account) needs FullControl, not ReadWrite: creating each further
        // server instance needs CreateNewInstance (FILE_CREATE_PIPE_INSTANCE), which ReadWrite lacks.
        // Clients only read, so Interactive gets Read and no write or CreateNewInstance.
        var security = new PipeSecurity();
        void Allow(WellKnownSidType sid, PipeAccessRights rights) =>
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow));
        Allow(WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
        // An explicit DACL drops the creator's implicit rights, and Windows checks CreateNewInstance for instance 2+
        // against the first instance's DACL. So the creating user needs FullControl too, or a non-LocalSystem host
        // (console run, dotnet test) cannot create further instances. Under LocalSystem this is the same SID as above.
        using (var self = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(self.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        Allow(WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.ReadWrite);
        Allow(WellKnownSidType.InteractiveSid, interactive);
        return NamedPipeServerStreamAcl.Create(pipeName, direction,
            NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options,
            0, 0, security);
    }
}
