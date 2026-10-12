namespace Ember.Core;

public enum TempSource { Cpu, Gpu, Motherboard }

public sealed record SafetyResult(bool AllFansMax, IReadOnlySet<TempSource> MissingSources);

/// <summary>
/// The service-side safety rules. At or above <see cref="TripC"/> on any source every fan goes to 100% until all
/// sources are below <see cref="ReleaseC"/>. A source with no good value for more than <see cref="StaleAfter"/> is
/// reported missing. Time comes only from the <c>now</c> argument. Not thread-safe.
/// </summary>
public sealed class SafetyMonitor
{
    public const double TripC = 90, ReleaseC = 85;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    readonly Dictionary<TempSource, DateTimeOffset> _lastGoodOrFirstAsked = new();
    bool _tripped;

    public SafetyResult Evaluate(DateTimeOffset now, IReadOnlyDictionary<TempSource, double?> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var missing = new HashSet<TempSource>();
        var anyHot = false;
        var anyAboveRelease = false;
        var anyUnreadable = false;
        foreach (var (source, value) in current)
        {
            if (value is { } v && double.IsFinite(v))
            {
                _lastGoodOrFirstAsked[source] = now;
                if (v >= TripC) anyHot = true;
                if (v >= ReleaseC) anyAboveRelease = true;
            }
            else
            {
                anyUnreadable = true;
                _lastGoodOrFirstAsked.TryAdd(source, now);
                if (now - _lastGoodOrFirstAsked[source] > StaleAfter) missing.Add(source);
            }
        }
        if (anyHot) _tripped = true;
        else if (!anyAboveRelease && !anyUnreadable) _tripped = false; // a hot source that stops reporting must not release the trip
        return new SafetyResult(_tripped, missing);
    }
}
