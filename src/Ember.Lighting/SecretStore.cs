using System.Runtime.InteropServices;
using System.Text;

namespace Ember.Lighting;

/// <summary>Where API keys live. Never a file Ember writes.</summary>
public interface ISecretStore
{
    string? Get(string name);
    /// <summary>Saves the value, or removes it when null or empty. False if it could not be done.</summary>
    bool Set(string name, string? value);
}

public sealed class MemorySecretStore : ISecretStore
{
    readonly Dictionary<string, string> _values = [];
    public string? Get(string name) => _values.GetValueOrDefault(name);
    public bool Set(string name, string? value)
    {
        if (string.IsNullOrEmpty(value)) _values.Remove(name); else _values[name] = value;
        return true;
    }
}

/// <summary>Windows Credential Manager, as a generic credential named "Ember/&lt;name&gt;" for the signed-in user.</summary>
public sealed class WindowsCredentialStore : ISecretStore
{
    const uint Generic = 1, PersistLocalMachine = 2;
    const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    static extern void CredFree(IntPtr buffer);

    static string Target(string name) => "Ember/" + name;

    public string? Get(string name)
    {
        if (!OperationSystemOk()) return null;
        if (!CredRead(Target(name), Generic, 0, out var ptr)) return null;
        try
        {
            var c = Marshal.PtrToStructure<Credential>(ptr);
            if (c.CredentialBlob == IntPtr.Zero || c.CredentialBlobSize == 0 || c.CredentialBlobSize > 8192) return null;
            var bytes = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CredFree(ptr); }
    }

    public bool Set(string name, string? value)
    {
        if (!OperationSystemOk()) return false;
        if (string.IsNullOrEmpty(value))
            return CredDelete(Target(name), Generic, 0) || Marshal.GetLastWin32Error() == ErrorNotFound;
        var bytes = Encoding.Unicode.GetBytes(value);
        if (bytes.Length > 8192) return false;
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var c = new Credential
            {
                Type = Generic, TargetName = Target(name), CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob,
                Persist = PersistLocalMachine, UserName = "Ember",
            };
            return CredWrite(ref c, 0);
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
        }
    }

    static bool OperationSystemOk() => OperatingSystem.IsWindows();
}
