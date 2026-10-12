namespace Ember.Service.Tests;

internal sealed class FakeFanController(params FanHeader[] headers) : IFanController
{
    public List<(string Op, string Id, double? Value)> Calls { get; } = new();

    public IReadOnlyList<FanHeader> Enumerate() => headers;

    public void SetPercent(string headerId, double percent)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        Known(headerId);
        Calls.Add(("set", headerId, percent));
    }

    public void ReleaseToAuto(string headerId)
    {
        Known(headerId);
        Calls.Add(("release", headerId, null));
    }

    void Known(string id)
    {
        if (!headers.Any(h => h.Id == id)) throw new InvalidOperationException($"Unknown fan header {id}.");
    }

    public void Dispose() { }
}
