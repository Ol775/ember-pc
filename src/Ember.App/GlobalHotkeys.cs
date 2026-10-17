using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ember.App;

/// <summary>
/// Three system-wide keys that work while a game has the focus: Ctrl+Alt+Shift with L (lights on or off), S (next lighting scene) and
/// F (next fan mode). Off until switched on in Settings. A key another program already holds is skipped, and nothing is read from the
/// keyboard: Windows only tells Ember when one of these exact combinations is pressed.
/// </summary>
sealed class GlobalHotkeys : IDisposable
{
    public enum Action { Lights = 1, Scene = 2, FanMode = 3 }

    const int WmHotkey = 0x0312;
    const uint Alt = 1, Control = 2, Shift = 4, NoRepeat = 0x4000;
    static readonly (Action Action, uint Key)[] Keys = [(Action.Lights, 'L'), (Action.Scene, 'S'), (Action.FanMode, 'F')];

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly Window _window;
    readonly Action<Action> _pressed;
    HwndSource? _source;
    readonly List<int> _registered = [];

    public GlobalHotkeys(Window window, Action<Action> pressed) => (_window, _pressed) = (window, pressed);

    /// <summary>How many of the three keys Windows gave Ember (a combination another program holds is not).</summary>
    public int Enable()
    {
        Disable();
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return 0;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(Hook);
        foreach (var (action, key) in Keys)
            if (RegisterHotKey(hwnd, (int)action, Control | Alt | Shift | NoRepeat, key)) _registered.Add((int)action);
        return _registered.Count;
    }

    public void Disable()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        foreach (var id in _registered) UnregisterHotKey(hwnd, id);
        _registered.Clear();
        _source?.RemoveHook(Hook);
        _source = null;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.Contains((int)wParam))
        {
            handled = true;
            _pressed((Action)(int)wParam);
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Disable();
}
