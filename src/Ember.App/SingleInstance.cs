using System.Threading;

namespace Ember.App;

/// <summary>
/// Only one Ember app per sign-in: a second copy would fight the first over G HUB's lighting session and the lighting devices.
/// The second launch asks the first to show its window and quits. An old copy that is still closing gets a few seconds to finish.
/// </summary>
sealed class SingleInstance : IDisposable
{
    const string Name = @"Local\Ember.App.SingleInstance";
    readonly Mutex _mutex;
    readonly EventWaitHandle _show;
    bool _owned;

    SingleInstance(Mutex mutex, EventWaitHandle show, bool owned) { _mutex = mutex; _show = show; _owned = owned; }

    /// <summary>Null when another Ember is running (it has been asked to show itself).</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(false, Name);
        var show = new EventWaitHandle(false, EventResetMode.AutoReset, Name + ".Show");
        bool owned;
        try { owned = mutex.WaitOne(TimeSpan.FromSeconds(4)); }
        catch (AbandonedMutexException) { owned = true; }
        if (owned) return new SingleInstance(mutex, show, true);
        show.Set();
        mutex.Dispose();
        show.Dispose();
        return null;
    }

    /// <summary>Calls <paramref name="onShow"/> (on a background thread) each time another launch asks this copy to show itself.</summary>
    public void ListenForShow(Action onShow)
    {
        var t = new Thread(() => { while (_show.WaitOne()) onShow(); }) { IsBackground = true, Name = "Ember show listener" };
        t.Start();
    }

    public void Dispose()
    {
        if (_owned) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not ours any more */ } _owned = false; }
        _mutex.Dispose();
    }
}
