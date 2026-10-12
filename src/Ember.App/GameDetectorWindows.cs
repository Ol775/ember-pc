using System.Diagnostics;
using System.Runtime.InteropServices;
using Ember.Core;

namespace Ember.App;

/// <summary>Reads the foreground window for <see cref="GameDetection"/>: which program, and whether it covers its whole monitor.</summary>
public static class GameDetectorWindows
{
    public static ForegroundInfo Foreground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return new ForegroundInfo(null, false);
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            return new ForegroundInfo(process.ProcessName + ".exe", CoversMonitor(hwnd));
        }
        catch (Exception)
        {
            return new ForegroundInfo(null, false); // process exited or access denied
        }
    }

    static bool CoversMonitor(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return false;
        return r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom;
    }

    const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
