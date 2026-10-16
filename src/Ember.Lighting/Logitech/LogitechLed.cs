using System.Runtime.InteropServices;

namespace Ember.Lighting.Logitech;

/// <summary>The few calls of Logitech's LED SDK that Ember uses. Colours are percent (0 to 100), as the SDK wants.</summary>
public interface ILogitechLed : IDisposable
{
    bool Init();
    /// <summary>Which kind of device the following colour calls reach.</summary>
    bool SetTargetDevice(LogitechTarget target);
    bool SetLighting(int redPercent, int greenPercent, int bluePercent);
    /// <summary>One picture for a per-key keyboard: 21 x 6 pixels, 4 bytes each (blue, green, red, alpha).</summary>
    bool SetLightingFromBitmap(byte[] bgra);
    /// <summary>One key by the SDK's key name (the G keys have names of their own and are not in the bitmap). False when G HUB does not take it.</summary>
    bool SetLightingForKeyName(int keyName, int redPercent, int greenPercent, int bluePercent);
    /// <summary>Hands the lighting back to G HUB.</summary>
    void Shutdown();
}

public enum LogitechTarget { Rgb = 2, PerKeyRgb = 4 }

/// <summary>Where G HUB keeps the LED SDK engine it installs, and how Ember makes sure it is that file and nothing else.</summary>
public static class LogitechLocator
{
    public const string FileName = "sdk_legacy_led_x64.dll";
    const string ClsidKey = @"SOFTWARE\Classes\CLSID\{a6519e67-7632-4375-afdf-caa889744403}\ServerBinary";

    /// <summary>
    /// The path G HUB registers for its LED SDK (or its usual install folder), only if the file exists, has the expected name
    /// and sits under Program Files, where only administrators can write. Null when G HUB is not installed.
    /// </summary>
    public static string? Find()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string?[] candidates = [FromRegistry(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LGHUB", "sdks", FileName)];
        return candidates.FirstOrDefault(c => IsTrusted(c));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? FromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(ClsidKey);
            return key?.GetValue(null) as string;
        }
        catch (Exception) { return null; }
    }

    public static bool IsTrusted(string? path, string? programFiles = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var roots = programFiles is null
                ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
                : [programFiles];
            return Path.GetFileName(full).Equals(FileName, StringComparison.OrdinalIgnoreCase)
                && roots.Any(r => r.Length > 0 && full.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                && File.Exists(full);
        }
        catch (Exception) { return false; }
    }
}

/// <summary>Loads G HUB's own LED SDK engine from disk. Nothing from Logitech is copied or shipped with Ember.</summary>
public sealed class NativeLogitechLed : ILogitechLed
{
    readonly nint _lib;
    readonly InitFn _init;
    readonly TargetFn _target;
    readonly LightFn _light;
    readonly BitmapFn _bitmap;
    readonly ShutdownFn _shutdown;
    readonly KeyNameFn? _keyName;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool KeyNameFn(int keyName, int r, int g, int b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool InitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool TargetFn(int device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool LightFn(int r, int g, int b);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate bool BitmapFn([MarshalAs(UnmanagedType.LPArray)] byte[] bitmap);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ShutdownFn();

    NativeLogitechLed(nint lib)
    {
        _lib = lib;
        _init = Get<InitFn>("LogiLedInit");
        _target = Get<TargetFn>("LogiLedSetTargetDevice");
        _light = Get<LightFn>("LogiLedSetLighting");
        _bitmap = Get<BitmapFn>("LogiLedSetLightingFromBitmap");
        _shutdown = Get<ShutdownFn>("LogiLedShutdown");
        // an older engine may lack this call: the keyboard grid still works without it
        if (NativeLibrary.TryGetExport(lib, "LogiLedSetLightingForKeyWithKeyName", out var p)) _keyName = Marshal.GetDelegateForFunctionPointer<KeyNameFn>(p);
    }

    /// <summary>Null when G HUB's SDK file cannot be found or loaded.</summary>
    public static NativeLogitechLed? TryLoad(string? path = null)
    {
        path ??= LogitechLocator.Find();
        if (path is null || !LogitechLocator.IsTrusted(path)) return null;
        nint lib = 0;
        try
        {
            lib = NativeLibrary.Load(path);
            return new NativeLogitechLed(lib);
        }
        catch (Exception)
        {
            if (lib != 0) NativeLibrary.Free(lib);
            return null;
        }
    }

    T Get<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    public bool Init() => _init();
    public bool SetTargetDevice(LogitechTarget target) => _target((int)target);
    public bool SetLighting(int r, int g, int b) => _light(r, g, b);
    public bool SetLightingFromBitmap(byte[] bgra) => bgra.Length == LogitechKeyboardSink.Bytes && _bitmap(bgra);
    public bool SetLightingForKeyName(int keyName, int r, int g, int b) => _keyName is { } f && f(keyName, r, g, b);
    public void Shutdown() => _shutdown();
    public void Dispose() { try { _shutdown(); } catch (Exception) { /* already gone */ } NativeLibrary.Free(_lib); }
}
