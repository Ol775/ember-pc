namespace Ember.Service.Tests;

internal sealed class FakeFanController(params FanHeader[] headers) : IFanController
{
    public List<(string Op, string Id, double? Value)> Calls { get; } = new();

    /// <summary>Headers that still report software mode after a release (a stuck fan).</summary>
    public HashSet<string> Stuck { get; } = new();
    public Func<string, double, bool>? FailWrite { get; set; }
    public bool FailRelease { get; set; }
    public bool EnumerateThrows { get; set; }

    public IReadOnlyList<FanHeader> Enumerate() =>
        EnumerateThrows
            ? throw new InvalidOperationException("enumerate failed")
            : headers.Select(h => h with { InSoftwareMode = h.InSoftwareMode || Stuck.Contains(h.Id) }).ToList();

    public void SetPercent(string headerId, double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        Known(headerId);
        if (FailWrite?.Invoke(headerId, percent) == true) throw new InvalidOperationException("write failed");
        Calls.Add(("set", headerId, percent));
    }

    public void Release(string headerId)
    {
        Known(headerId);
        if (FailRelease) throw new InvalidOperationException("release failed");
        Calls.Add(("release", headerId, null));
    }

    public void ReleaseToAutomatic(string headerId)
    {
        Known(headerId);
        Calls.Add(("auto", headerId, null));
    }

    void Known(string id)
    {
        if (!headers.Any(h => h.Id == id)) throw new InvalidOperationException($"Unknown fan header {id}.");
    }

    public void Dispose() { }
}
