namespace Ember.Core;

public sealed class HistoryBuffer(int capacity)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<(DateTimeOffset, double)>> _series = new();

    public void Add(Snapshot s)
    {
        if (s.Stale) return;
        lock (_lock)
        {
            foreach (var r in s.Readings)
            {
                if (!_series.TryGetValue(r.Id, out var q)) _series[r.Id] = q = new();
                q.Enqueue((s.Timestamp, r.Value));
                while (q.Count > capacity) q.Dequeue();
            }
        }
    }

    public IReadOnlyList<(DateTimeOffset Time, double Value)> Series(string sensorId)
    {
        lock (_lock)
            return _series.TryGetValue(sensorId, out var q) ? q.ToArray() : Array.Empty<(DateTimeOffset, double)>();
    }
}
