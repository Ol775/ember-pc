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
    // JsonRequired fields must be present (a missing "val" must never decode as 0); "stale" and "d" are optional.
    sealed class SnapshotDto
    {
        [JsonRequired, JsonPropertyName("v")] public int V { get; set; }
        [JsonRequired, JsonPropertyName("t")] public DateTimeOffset T { get; set; }
        [JsonPropertyName("stale")] public bool Stale { get; set; }
        [JsonRequired, JsonPropertyName("r")] public List<ReadingDto?>? R { get; set; }
        // Optional: absent/null decodes as Unknown (older services); an unknown name is rejected.
        [JsonPropertyName("d")] public string? D { get; set; }
    }

    sealed class ReadingDto
    {
        [JsonRequired, JsonPropertyName("id")] public string? Id { get; set; }
        [JsonRequired, JsonPropertyName("n")] public string? N { get; set; }
        [JsonRequired, JsonPropertyName("h")] public string? H { get; set; }
        [JsonRequired, JsonPropertyName("k")] public string? K { get; set; }
        [JsonRequired, JsonPropertyName("val")] public double Val { get; set; }
    }

    public static string Encode(Snapshot s) => JsonSerializer.Serialize(new SnapshotDto
    {
        V = Version,
        T = s.Timestamp,
        Stale = s.Stale,
        D = s.Driver.ToString(),
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
            if (dto.D is not null && !Enum.IsDefined(typeof(DriverStatus), dto.D)) return false;
            var driver = dto.D is null ? DriverStatus.Unknown : Enum.Parse<DriverStatus>(dto.D);

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
            snapshot = new Snapshot(dto.T, readings, dto.Stale, driver);
            return true;
        }
        catch (Exception)
        {
            snapshot = null;
            return false;
        }
    }
}
