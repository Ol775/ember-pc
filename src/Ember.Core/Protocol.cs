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
        // Optional fan control status; absent from older services.
        [JsonPropertyName("c")] public ControlDto? C { get; set; }
    }

    sealed class ControlDto
    {
        [JsonRequired, JsonPropertyName("m")] public string? M { get; set; }
        [JsonRequired, JsonPropertyName("s")] public string? S { get; set; }
        [JsonPropertyName("so")] public bool So { get; set; }
        [JsonPropertyName("g")] public string? G { get; set; }
        [JsonRequired, JsonPropertyName("h")] public List<HeaderDto?>? H { get; set; }
        [JsonRequired, JsonPropertyName("x")] public List<string?>? X { get; set; }
        [JsonPropertyName("t")] public string? T { get; set; }
        [JsonPropertyName("ng")] public List<string?>? Ng { get; set; }
        [JsonPropertyName("gc")] public bool Gc { get; set; }
        // Optional: the Manual-mode curves, as [temp, percent] pairs.
        [JsonPropertyName("mc")] public CurvesDto? Mc { get; set; }
    }

    sealed class CurvesDto
    {
        [JsonRequired, JsonPropertyName("cpu")] public List<double[]?>? Cpu { get; set; }
        [JsonRequired, JsonPropertyName("case")] public List<double[]?>? Case { get; set; }
    }

    sealed class HeaderDto
    {
        [JsonRequired, JsonPropertyName("id")] public string? Id { get; set; }
        [JsonRequired, JsonPropertyName("n")] public string? N { get; set; }
        [JsonRequired, JsonPropertyName("st")] public string? St { get; set; }
        [JsonPropertyName("ok")] public bool Ok { get; set; }
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
        C = s.Control is null ? null : new ControlDto
        {
            M = s.Control.Mode.ToString(), S = s.Control.Source.ToString(), So = s.Control.SafetyOverride, G = s.Control.GameExe,
            H = s.Control.Headers.Select(h => (HeaderDto?)new HeaderDto { Id = h.Id, N = h.Name, St = h.State.ToString(), Ok = h.Tested }).ToList(),
            X = s.Control.Competitors.Select(c => (string?)c).ToList(), T = s.Control.TestMessage,
            Ng = s.Control.NotGames.Select(c => (string?)c).ToList(), Gc = s.Control.GpuControl,
            Mc = new CurvesDto { Cpu = Pairs(s.Control.Curves.Cpu), Case = Pairs(s.Control.Curves.Case) },
        },
        R = s.Readings.Select(r => (ReadingDto?)new ReadingDto
        {
            Id = r.Id, N = r.Name, H = r.Hardware.ToString(), K = r.Kind.ToString(), Val = r.Value,
        }).ToList(),
    });

    static List<double[]?> Pairs(IReadOnlyList<CurvePoint> points) => points.Select(p => (double[]?)new[] { p.TempC, p.Percent }).ToList();

    static IReadOnlyList<CurvePoint>? Points(List<double[]?>? pairs) =>
        pairs is null || pairs.Any(p => p is not { Length: 2 }) ? null : pairs.Select(p => new CurvePoint(p![0], p[1])).ToList();

    static bool TryDecodeControl(ControlDto c, out FanControlStatus? status)
    {
        status = null;
        if (c.M is null || !Enum.IsDefined(typeof(FanMode), c.M)) return false;
        if (c.S is null || !Enum.IsDefined(typeof(ModeSource), c.S)) return false;
        if (c.H is null || c.X is null) return false;
        var headers = new List<HeaderStatus>(c.H.Count);
        foreach (var h in c.H)
        {
            if (h is null || h.Id is null || h.N is null || h.St is null || !Enum.IsDefined(typeof(HeaderState), h.St)) return false;
            headers.Add(new HeaderStatus(h.Id, h.N, Enum.Parse<HeaderState>(h.St), h.Ok));
        }
        if (c.X.Any(x => x is null)) return false;
        if (c.Ng is not null && c.Ng.Any(x => x is null)) return false;
        ManualCurves? curves = null;
        if (c.Mc is not null)
        {
            // A curve that does not pass the rules is a broken message, not something to draw.
            var cpu = Points(c.Mc.Cpu);
            var cas = Points(c.Mc.Case);
            if (ManualCurveRules.Check(cpu) is not null || ManualCurveRules.Check(cas) is not null) return false;
            curves = new ManualCurves(cpu!, cas!);
        }
        status = new FanControlStatus(Enum.Parse<FanMode>(c.M), Enum.Parse<ModeSource>(c.S), c.So, c.G, headers,
            c.X.Select(x => x!).ToList(), c.T, c.Ng?.Select(x => x!).ToList(), c.Gc, curves);
        return true;
    }

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
            FanControlStatus? control = null;
            if (dto.C is not null && !TryDecodeControl(dto.C, out control)) return false;
            snapshot = new Snapshot(dto.T, readings, dto.Stale, driver, control);
            return true;
        }
        catch (Exception)
        {
            snapshot = null;
            return false;
        }
    }
}
