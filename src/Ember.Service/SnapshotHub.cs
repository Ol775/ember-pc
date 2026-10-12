using Ember.Core;

namespace Ember.Service;

public sealed class SnapshotHub
{
    public Snapshot? Latest { get; private set; }

    public event Action<Snapshot>? Published;

    public void Publish(Snapshot s)
    {
        Latest = s;
        var handlers = Published;
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList().Cast<Action<Snapshot>>())
        {
            try { h(s); }
            catch { /* a bad subscriber must not break the others */ }
        }
    }
}
