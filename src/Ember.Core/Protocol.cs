using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

/// <summary>JSON-lines wire format. Trust boundary: TryDecode never throws.</summary>
public static class Protocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 1_048_576;

    // Wire shape. To add an optional field, add a property here and map it in Encode/TryDecode.
    sealed class SnapshotDto
    {
        [JsonPropertyName("v")] public int V { get; set; }
        [JsonPropertyName("t")] public DateTimeOffset T { get; set; }
        [JsonPropertyName("stale")] public bool Stale { get; set; }
        [JsonPropertyName("r")] public List<ReadingDto?>? R { get; set; }
    }

    sealed class ReadingDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("n")] public string? N { get; set; }
        [JsonPropertyName("h")] public string? H { get; set; }
        [JsonPropertyName("k")] public string? K { get; set; }
        [JsonPropertyName("val")] public double Val { get; set; }
    }

    public static string Encode(Snapshot s) => JsonSerializer.Serialize(new SnapshotDto
    {
        V = Version,
        T = s.Timestamp,
        Stale = s.Stale,
        R = s.Readings.Select(r => (ReadingDto?)new ReadingDto
        {
            Id = r.Id, N = r.Name, H = r.Hardware.ToString(), K = r.Kind.ToString(), Val = r.Value,
        }).ToList(),
    });

    public static bool TryDecode(string line, out Snapshot? snapshot)
    {
        snapshot = null;
        try
        {
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxMessageBytes) return false;
            var dto = JsonSerializer.Deserialize<SnapshotDto>(line);
            if (dto is null || dto.V != Version || dto.R is null) return false;

            var readings = new List<SensorReading>(dto.R.Count);
            foreach (var r in dto.R)
            {
                if (r is null || r.Id is null || r.N is null || !double.IsFinite(r.Val)) return false;
                // IsDefined(string) matches names only, so numeric strings like "1" are rejected.
                if (r.H is null || !Enum.IsDefined(typeof(HardwareKind), r.H)) return false;
                if (r.K is null || !Enum.IsDefined(typeof(SensorKind), r.K)) return false;
                readings.Add(new SensorReading(r.Id, r.N,
                    Enum.Parse<HardwareKind>(r.H), Enum.Parse<SensorKind>(r.K), r.Val));
            }
            snapshot = new Snapshot(dto.T, readings, dto.Stale);
            return true;
        }
        catch (Exception)
        {
            snapshot = null;
            return false;
        }
    }
}
