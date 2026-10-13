using System.Runtime.InteropServices;
using System.Windows.Threading;
using Ember.Lighting;

namespace Ember.App;

/// <summary>
/// Tells the lighting engine that a key went down, for the "Reactive keys" effect. It runs only while a layer of that kind is
/// switched on, looks only at the keys of a standard keyboard map, and passes on nothing but "this key was pressed just now":
/// no text is built, stored, logged or sent anywhere, and the key is reduced to its place on the keyboard straight away.
/// </summary>
public sealed class KeyWatcher(Action<int> pressed) : IDisposable
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

    readonly int[] _keys = Enumerable.Range(1, 254).Where(vk => KeyboardLayout.Position(vk) is not null).ToArray();
    readonly bool[] _down = new bool[256];
    DispatcherTimer? _timer;

    public bool Running => _timer is not null;

    public void Start()
    {
        if (_timer is not null) return;
        foreach (var vk in _keys) _down[vk] = (GetAsyncKeyState(vk) & 0x8000) != 0;   // keys already held are not "new"
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (_, _) => Poll(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        Array.Clear(_down);
    }

    void Poll()
    {
        try
        {
            foreach (var vk in _keys)
            {
                var now = (GetAsyncKeyState(vk) & 0x8000) != 0;
                if (now && !_down[vk]) pressed(vk);
                _down[vk] = now;
            }
        }
        catch (Exception) { /* never let a failed poll reach the window */ }
    }

    public void Dispose() => Stop();
}
