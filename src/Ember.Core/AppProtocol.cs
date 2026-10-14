using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Core;

public abstract record AppRequest;
public sealed record AppStatusRequest : AppRequest;
public sealed record SelectSceneRequest(string Name) : AppRequest;
public sealed record SetOnRequest(bool On) : AppRequest;
public sealed record ToggleOnRequest : AppRequest;
/// <summary>Moves the current scene's brightness by <paramref name="Delta"/> (-1 to 1; the result is clamped to 0..1).</summary>
public sealed record NudgeBrightnessRequest(double Delta) : AppRequest;
public sealed record ShowWindowRequest : AppRequest;

public sealed record AppStatus(bool On, string CurrentScene, IReadOnlyList<string> Scenes, double Brightness);
public sealed record AppReply(bool Ok, string? Error, AppStatus? Status);

/// <summary>
/// JSON-lines messages for the app pipe (ember.app.v1), which the Stream Deck plugin uses to reach the running app.
/// Same trust rules as <see cref="ControlProtocol"/>: a closed set of operations, strictly validated values, and decoding never throws.
/// </summary>
public static class AppProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 8192;
    public const int MaxSceneNameLength = 60;
    public const int MaxScenes = 50;

    sealed class Dto
    {
        [JsonRequired, JsonPropertyName("v")] public int V { get; set; }
        [JsonRequired, JsonPropertyName("op")] public string? Op { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("on")] public bool? On { get; set; }
        [JsonPropertyName("delta")] public double? Delta { get; set; }
    }

    sealed class StatusDto
    {
        [JsonRequired, JsonPropertyName("on")] public bool On { get; set; }
        [JsonRequired, JsonPropertyName("scene")] public string? Scene { get; set; }
        [JsonRequired, JsonPropertyName("scenes")] public List<string?>? Scenes { get; set; }
        [JsonRequired, JsonPropertyName("brightness")] public double Brightness { get; set; }
    }

    sealed class ReplyDto
    {
        [JsonRequired, JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("status")] public StatusDto? Status { get; set; }
    }

    public static string Encode(AppRequest r)
    {
        var d = new Dto { V = Version };
        switch (r)
        {
            case AppStatusRequest: d.Op = "status"; break;
            case SelectSceneRequest x: d.Op = "selectScene"; d.Name = x.Name; break;
            case SetOnRequest x: d.Op = "setOn"; d.On = x.On; break;
            case ToggleOnRequest: d.Op = "toggleOn"; break;
            case NudgeBrightnessRequest x: d.Op = "nudgeBrightness"; d.Delta = x.Delta; break;
            case ShowWindowRequest: d.Op = "showWindow"; break;
            default: throw new ArgumentException("Unknown app request.", nameof(r));
        }
        return JsonSerializer.Serialize(d);
    }

    public static bool TryDecode(string line, out AppRequest? request)
    {
        request = null;
        try
        {
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) return false;
            var d = JsonSerializer.Deserialize<Dto>(line);
            if (d is null || d.V != Version || d.Op is null) return false;
            request = d.Op switch
            {
                "status" => new AppStatusRequest(),
                "selectScene" => SceneName(d.Name) is { } n ? new SelectSceneRequest(n) : null,
                "setOn" => d.On is { } on ? new SetOnRequest(on) : null,
                "toggleOn" => new ToggleOnRequest(),
                "nudgeBrightness" => d.Delta is { } delta && double.IsFinite(delta) && delta is >= -1 and <= 1 ? new NudgeBrightnessRequest(delta) : null,
                "showWindow" => new ShowWindowRequest(),
                _ => null,
            };
            return request is not null;
        }
        catch (Exception)
        {
            request = null;
            return false;
        }
    }

    static string? SceneName(string? s) => s is { Length: > 0 and <= MaxSceneNameLength } ? s : null;

    public static string EncodeReply(AppReply r) => JsonSerializer.Serialize(new ReplyDto
    {
        Ok = r.Ok,
        Error = r.Error,
        Status = r.Status is null ? null : new StatusDto
        {
            On = r.Status.On, Scene = r.Status.CurrentScene, Scenes = r.Status.Scenes.Select(s => (string?)s).ToList(), Brightness = r.Status.Brightness,
        },
    });

    public static bool TryDecodeReply(string line, out AppReply? reply)
    {
        reply = null;
        try
        {
            if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) return false;
            var d = JsonSerializer.Deserialize<ReplyDto>(line);
            if (d is null) return false;
            AppStatus? status = null;
            if (d.Status is { } s)
            {
                if (s.Scene is null || s.Scenes is null || s.Scenes.Count > MaxScenes || s.Scenes.Any(x => x is null) || !double.IsFinite(s.Brightness)) return false;
                status = new AppStatus(s.On, s.Scene, s.Scenes.Select(x => x!).ToList(), s.Brightness);
            }
            reply = new AppReply(d.Ok, d.Error, status);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
