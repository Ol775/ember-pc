using Ember.Lighting.OpenRgb;

namespace Ember.Lighting.Govee;

/// <summary>
/// Finds Govee lamps: first on the local network, then (when an API key is saved) in the cloud, for the ones the network
/// scan did not find. A lamp that is missed by one scan is kept for a minute, because Govee devices answer scans unreliably.
/// </summary>
public sealed class GoveeSource(GoveeLanClient lan, Func<GoveeCloudClient?> cloud, Func<string, int>? segments = null) : IDeviceSource
{
    static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(1), ScanFor = TimeSpan.FromSeconds(1.5);
    readonly Dictionary<string, (GoveeLanDevice Device, DateTime Seen)> _known = new();
    readonly Dictionary<string, IDeviceSink> _sinks = new();
    string _cloudNote = "";

    public string Name => "Govee";
    public string Status { get; private set; } = "Looking for lamps…";

    public async Task<IReadOnlyList<IDeviceSink>> RefreshAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var d in await lan.DiscoverAsync(ScanFor, ct).ConfigureAwait(false)) _known[d.Id] = (d, now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Status = "Could not scan the network: " + ex.Message; }
        foreach (var gone in _known.Where(kv => now - kv.Value.Seen > KeepFor).Select(kv => kv.Key).ToList())
        {
            _known.Remove(gone);
            _sinks.Remove("govee:" + gone);
        }

        var result = new List<IDeviceSink>();
        foreach (var (id, (device, _)) in _known)
        {
            if (!_sinks.TryGetValue("govee:" + id, out var sink) || sink is not GoveeLanSink) _sinks["govee:" + id] = sink = new GoveeLanSink(lan, device, () => segments?.Invoke("govee:" + id) ?? 1);
            result.Add(sink);
        }

        var cloudClient = cloud();
        _cloudNote = "";
        if (cloudClient is not null)
        {
            try
            {
                foreach (var d in await cloudClient.GetDevicesAsync(ct).ConfigureAwait(false))
                {
                    if (!d.CanSetColor || _known.ContainsKey(d.Id)) continue;
                    if (!_sinks.TryGetValue("govee:" + d.Id, out var sink) || sink is not GoveeCloudSink) _sinks["govee:" + d.Id] = sink = new GoveeCloudSink(cloudClient, d);
                    result.Add(sink);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _cloudNote = " Cloud: " + ex.Message; }
        }

        var lanCount = _known.Count;
        var cloudCount = result.Count - lanCount;
        Status = (result.Count == 0
            ? "No lamps found. Turn on LAN control for each lamp in the Govee app, or save an API key in Settings."
            : $"{lanCount} on the network" + (cloudCount > 0 ? $", {cloudCount} through the cloud" : "")) + _cloudNote;
        return result;
    }

    public void ReportFailure(string sinkId) { /* lamps have no connection to drop; the next scan decides */ }
}
