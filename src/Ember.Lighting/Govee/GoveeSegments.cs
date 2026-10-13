namespace Ember.Lighting.Govee;

/// <summary>Something that can say how many parts a device can light separately, so the Lights page can offer to set it up.</summary>
public interface ISegmentDetector
{
    /// <summary>The number of segments for the device, or null when it cannot be told.</summary>
    Task<int?> DetectSegmentsAsync(string deviceId, CancellationToken ct);
}

/// <summary>
/// What is known about how many separately lit parts Govee lamps have. The count the cloud reports is the range the
/// interface accepts, which for some models is more than the lamp physically has, so a model on this list wins over the cloud.
/// </summary>
public static class GoveeSegments
{
    // H6056 (Flow Plus light bars): two bars of six, although the cloud accepts 15. Counts come from other people's reports,
    // not from Govee, so they are a starting point and the Lights page lets you change them.
    static readonly Dictionary<string, int> Known = new(StringComparer.OrdinalIgnoreCase) { ["H6056"] = 12 };

    public static int? ForModel(string? sku, int? fromCloud)
    {
        if (sku is not null && Known.TryGetValue(sku, out var known)) return known;
        return fromCloud is >= 2 and <= 255 ? fromCloud : null;
    }
}
