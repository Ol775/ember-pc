using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ember.Service;

public static class SettingsDirectory
{
    /// <summary>
    /// Creates the folder (if needed) so only SYSTEM and Administrators can write it; everyone else can read.
    /// Settings decide which headers Ember may drive, so ordinary users must not be able to edit them.
    /// </summary>
    public static void Ensure(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows()) Secure(path);
    }

    [SupportedOSPlatform("windows")]
    static void Secure(string path)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        void Allow(WellKnownSidType sid, FileSystemRights rights) =>
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights, inherit, PropagationFlags.None, AccessControlType.Allow));
        Allow(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        Allow(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        Allow(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}
