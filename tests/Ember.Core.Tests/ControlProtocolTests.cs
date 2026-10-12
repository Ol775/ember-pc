namespace Ember.Core.Tests;

public class ControlProtocolTests
{
    public static IEnumerable<object[]> Messages() => new[]
    {
        new object[] { new SetMode(FanMode.Quiet) },
        new object[] { new SetGameMode(FanMode.Extreme) },
        new object[] { new GameStarted("Elden Ring.exe") },
        new object[] { new GameEnded() },
        new object[] { new DetectorHeartbeat() },
        new object[] { new SetNotGame("chrome.exe", true) },
        new object[] { new ConfirmHeader("/lpc/nct6687d/0/control/2", true) },
        new object[] { new RunHeaderTest("/gpu-nvidia/0/control/1") },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void Round_trip(ControlMessage m)
    {
        Assert.True(ControlProtocol.TryDecode(ControlProtocol.Encode(m), out var back));
        Assert.Equal(m, back);
    }

    [Theory]
    [InlineData("{\"v\":1,\"op\":\"setMode\",\"mode\":\"Ai\"}")]
    [InlineData("{\"v\":1,\"op\":\"setMode\",\"mode\":1}")]
    [InlineData("{\"v\":1,\"op\":\"setMode\",\"mode\":\"1\"}")]
    [InlineData("{\"v\":1,\"op\":\"setMode\",\"mode\":\"Turbo\"}")]
    [InlineData("{\"v\":1,\"op\":\"setGameMode\",\"mode\":\"Ai\"}")]
    [InlineData("{\"v\":1,\"op\":\"format\"}")]
    [InlineData("{\"v\":2,\"op\":\"gameEnded\"}")]
    [InlineData("{\"op\":\"gameEnded\"}")]
    [InlineData("{\"v\":1,\"op\":\"setMode\"}")]
    [InlineData("{\"v\":1,\"op\":\"gameStarted\"}")]
    [InlineData("{\"v\":1,\"op\":\"gameStarted\",\"exe\":\"..\\\\..\\\\evil.exe\"}")]
    [InlineData("{\"v\":1,\"op\":\"gameStarted\",\"exe\":\"game.dll\"}")]
    [InlineData("{\"v\":1,\"op\":\"gameStarted\",\"exe\":\"/etc/x.exe\"}")]
    [InlineData("{\"v\":1,\"op\":\"confirmHeader\",\"id\":\"/lpc/../../x\",\"enabled\":true}")]
    [InlineData("{\"v\":1,\"op\":\"confirmHeader\",\"id\":\"/lpc/nct6687d/0/control/2\"}")]
    [InlineData("{\"v\":1,\"op\":\"runHeaderTest\",\"id\":\"CPU Fan\"}")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Rejects_invalid(string line)
    {
        Assert.False(ControlProtocol.TryDecode(line, out var m));
        Assert.Null(m);
    }

    [Fact]
    public void Rejects_oversized_line()
    {
        var line = "{\"v\":1,\"op\":\"gameEnded\",\"pad\":\"" + new string('x', ControlProtocol.MaxLineBytes) + "\"}";
        Assert.False(ControlProtocol.TryDecode(line, out _));
    }

    [Fact]
    public void Garbage_never_throws()
    {
        var rng = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var chars = Enumerable.Range(0, rng.Next(0, 80)).Select(_ => (char)rng.Next(0, 0x250)).ToArray();
            ControlProtocol.TryDecode(new string(chars), out _);
        }
    }

    [Fact]
    public void Reply_round_trip()
    {
        foreach (var r in new[] { new ControlReply(true, null), new ControlReply(false, "rate limited") })
        {
            Assert.True(ControlProtocol.TryDecodeReply(ControlProtocol.EncodeReply(r), out var back));
            Assert.Equal(r, back);
        }
        Assert.False(ControlProtocol.TryDecodeReply("nope", out _));
    }
}
