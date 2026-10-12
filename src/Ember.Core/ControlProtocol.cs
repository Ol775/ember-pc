using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Ember.Core;

public abstract record ControlMessage;
public sealed record SetMode(FanMode Mode) : ControlMessage;
public sealed record SetGameMode(FanMode Mode) : ControlMessage;
public sealed record GameStarted(string Exe) : ControlMessage;
public sealed record GameEnded : ControlMessage;
public sealed record DetectorHeartbeat : ControlMessage;
public sealed record SetNotGame(string Exe, bool NotGame) : ControlMessage;
public sealed record ConfirmHeader(string HeaderId, bool Enabled) : ControlMessage;
public sealed record RunHeaderTest(string HeaderId) : ControlMessage;
public sealed record SetGpuControl(bool Enabled) : ControlMessage;
public sealed record SetExperimental(bool Enabled) : ControlMessage;
/// <summary>Replaces one of the user's Manual-mode curves. Only the shape is checked on decoding; the service applies <see cref="ManualCurveRules"/>.</summary>
public sealed record SetCurve(CurveGroup Group, IReadOnlyList<CurvePoint> Points) : ControlMessage;

public sealed record ControlReply(bool Ok, string? Error);

/// <summary>
/// JSON-lines control messages. Trust boundary: any local interactive user can reach the control pipe, so decoding
/// accepts only a closed set of operations with strictly validated values, and never throws. There is deliberately
/// no message that sets a fan speed directly: the only percentages are the points of a Manual curve, which the service
/// checks against <see cref="ManualCurveRules"/> and which the safety rules, floors and the 90 °C override still sit on top of.
/// </summary>
public static partial class ControlProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 4096;

    [GeneratedRegex(@"^[A-Za-z0-9._ -]{1,60}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ExeName();

    [GeneratedRegex(@"^/(lpc/[a-z0-9]+/\d+|gpu-nvidia/\d+)/control/\d+$")]
    private static partial Regex HeaderId();

    sealed class Dto
    {
        [JsonRequired, JsonPropertyName("v")] public int V { get; set; }
        [JsonRequired, JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("exe")] public string? Exe { get; set; }
        [JsonPropertyName("notGame")] public bool? NotGame { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("group")] public string? Group { get; set; }
        [JsonPropertyName("points")] public List<PointDto?>? Points { get; set; }
    }

    sealed class PointDto
    {
        [JsonRequired, JsonPropertyName("t")] public double T { get; set; }
        [JsonRequired, JsonPropertyName("p")] public double P { get; set; }
    }

    sealed class ReplyDto
    {
        [JsonRequired, JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    public static string Encode(ControlMessage m)
    {
        var d = new Dto { V = Version };
        switch (m)
        {
            case SetMode x: d.Op = "setMode"; d.Mode = x.Mode.ToString(); break;
            case SetGameMode x: d.Op = "setGameMode"; d.Mode = x.Mode.ToString(); break;
            case GameStarted x: d.Op = "gameStarted"; d.Exe = x.Exe; break;
            case GameEnded: d.Op = "gameEnded"; break;
            case DetectorHeartbeat: d.Op = "heartbeat"; break;
            case SetNotGame x: d.Op = "setNotGame"; d.Exe = x.Exe; d.NotGame = x.NotGame; break;
            case ConfirmHeader x: d.Op = "confirmHeader"; d.Id = x.HeaderId; d.Enabled = x.Enabled; break;
            case RunHeaderTest x: d.Op = "runHeaderTest"; d.Id = x.HeaderId; break;
            case SetGpuControl x: d.Op = "setGpuControl"; d.Enabled = x.Enabled; break;
            case SetExperimental x: d.Op = "setExperimental"; d.Enabled = x.Enabled; break;
            case SetCurve x:
                d.Op = "setCurve"; d.Group = x.Group.ToString();
                d.Points = x.Points.Select(p => (PointDto?)new PointDto { T = p.TempC, P = p.Percent }).ToList();
                break;
            default: throw new ArgumentException("Unknown control message.", nameof(m));
        }
        return JsonSerializer.Serialize(d);
    }

    public static bool TryDecode(string line, out ControlMessage? message)
    {
        message = null;
        try
        {
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) return false;
            var d = JsonSerializer.Deserialize<Dto>(line);
            if (d is null || d.V != Version || d.Op is null) return false;
            message = d.Op switch
            {
                "setMode" => Mode(d.Mode) is { } m ? new SetMode(m) : null,
                "setGameMode" => Mode(d.Mode) is { } m ? new SetGameMode(m) : null,
                "gameStarted" => Exe(d.Exe) is { } e ? new GameStarted(e) : null,
                "gameEnded" => new GameEnded(),
                "heartbeat" => new DetectorHeartbeat(),
                "setNotGame" => Exe(d.Exe) is { } e && d.NotGame is { } n ? new SetNotGame(e, n) : null,
                "confirmHeader" => Header(d.Id) is { } h && d.Enabled is { } en ? new ConfirmHeader(h, en) : null,
                "runHeaderTest" => Header(d.Id) is { } h ? new RunHeaderTest(h) : null,
                "setGpuControl" => d.Enabled is { } g ? new SetGpuControl(g) : null,
                "setExperimental" => d.Enabled is { } x ? new SetExperimental(x) : null,
                "setCurve" => Curve(d),
                _ => null,
            };
            return message is not null;
        }
        catch (Exception)
        {
            message = null;
            return false;
        }
    }

    public static string EncodeReply(ControlReply r) => JsonSerializer.Serialize(new ReplyDto { Ok = r.Ok, Error = r.Error });

    public static bool TryDecodeReply(string line, out ControlReply? reply)
    {
        reply = null;
        try
        {
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) return false;
            var d = JsonSerializer.Deserialize<ReplyDto>(line);
            if (d is null) return false;
            reply = new ControlReply(d.Ok, d.Error);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static SetCurve? Curve(Dto d)
    {
        if (d.Group is null || !Enum.IsDefined(typeof(CurveGroup), d.Group) || d.Points is not { Count: >= 2 and <= 10 } list) return null;
        if (list.Any(p => p is null || !double.IsFinite(p.T) || !double.IsFinite(p.P))) return null;
        return new SetCurve(Enum.Parse<CurveGroup>(d.Group), list.Select(p => new CurvePoint(p!.T, p.P)).ToList());
    }

    // IsDefined(string) matches names only, so numeric strings like "1" are rejected; Ai is not selectable yet.
    static FanMode? Mode(string? s) =>
        s is not null && Enum.IsDefined(typeof(FanMode), s) && Enum.Parse<FanMode>(s) is var m && m != FanMode.Ai ? m : null;

    static string? Exe(string? s) => s is not null && ExeName().IsMatch(s) ? s : null;

    static string? Header(string? s) => s is not null && HeaderId().IsMatch(s) ? s : null;
}
