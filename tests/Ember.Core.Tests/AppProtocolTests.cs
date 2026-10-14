namespace Ember.Core.Tests;

public class AppProtocolTests
{
    public static IEnumerable<object[]> Requests() => new[]
    {
        new object[] { new AppStatusRequest() },
        new object[] { new SelectSceneRequest("Warm glow") },
        new object[] { new SetOnRequest(true) },
        new object[] { new SetOnRequest(false) },
        new object[] { new ToggleOnRequest() },
        new object[] { new NudgeBrightnessRequest(-0.1) },
        new object[] { new ShowWindowRequest() },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void Every_request_round_trips(AppRequest r)
    {
        Assert.True(AppProtocol.TryDecode(AppProtocol.Encode(r), out var back));
        Assert.Equal(r, back);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"v\":2,\"op\":\"status\"}")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"op\":\"format\"}")]
    [InlineData("{\"v\":1,\"op\":\"selectScene\"}")]
    [InlineData("{\"v\":1,\"op\":\"selectScene\",\"name\":\"\"}")]
    [InlineData("{\"v\":1,\"op\":\"setOn\"}")]
    [InlineData("{\"v\":1,\"op\":\"nudgeBrightness\"}")]
    public void Garbage_and_incomplete_requests_are_rejected(string line)
    {
        Assert.False(AppProtocol.TryDecode(line, out var r));
        Assert.Null(r);
    }

    [Fact]
    public void Oversized_line_is_rejected_without_throwing()
    {
        var line = "{\"v\":1,\"op\":\"selectScene\",\"name\":\"" + new string('x', 9000) + "\"}";
        Assert.False(AppProtocol.TryDecode(line, out _));
    }

    [Fact]
    public void Scene_name_over_sixty_characters_is_rejected()
    {
        Assert.False(AppProtocol.TryDecode(AppProtocol.Encode(new SelectSceneRequest(new string('x', 61))), out _));
        Assert.True(AppProtocol.TryDecode(AppProtocol.Encode(new SelectSceneRequest(new string('x', 60))), out _));
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Brightness_nudge_out_of_range_is_rejected(double delta)
    {
        var line = "{\"v\":1,\"op\":\"nudgeBrightness\",\"delta\":" + (double.IsFinite(delta) ? delta.ToString(System.Globalization.CultureInfo.InvariantCulture) : "\"NaN\"") + "}";
        Assert.False(AppProtocol.TryDecode(line, out _));
    }

    [Fact]
    public void Reply_round_trip_keeps_scene_order()
    {
        var reply = new AppReply(true, null, new AppStatus(true, "B", ["A", "B", "C"], 0.5));
        Assert.True(AppProtocol.TryDecodeReply(AppProtocol.EncodeReply(reply), out var back));
        Assert.True(back!.Ok);
        Assert.Equal(["A", "B", "C"], back.Status!.Scenes);
        Assert.Equal("B", back.Status.CurrentScene);
        Assert.Equal(0.5, back.Status.Brightness);
        Assert.True(back.Status.On);
    }

    [Fact]
    public void Error_reply_round_trips_without_status()
    {
        Assert.True(AppProtocol.TryDecodeReply(AppProtocol.EncodeReply(new AppReply(false, "invalid", null)), out var back));
        Assert.False(back!.Ok);
        Assert.Equal("invalid", back.Error);
        Assert.Null(back.Status);
    }

    [Fact]
    public void Reply_decoding_never_throws_on_garbage()
    {
        foreach (var line in new[] { "", "{", "[]", "null", new string('x', 20000) })
            Assert.False(AppProtocol.TryDecodeReply(line, out _));
    }
}
