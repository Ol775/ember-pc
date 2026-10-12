using System.Text;

namespace Ember.Core.Tests;

public class ProtocolTests
{
    static Snapshot Sample() => new(
        new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero),
        new[]
        {
            new SensorReading("/cpu/temp", "CPU Package", HardwareKind.Cpu, SensorKind.Temperature, 54.5),
            new SensorReading("/fan/1", "Fan #1", HardwareKind.Motherboard, SensorKind.FanRpm, 1200),
        },
        true);

    static string Line(string val = "54.0", string h = "Cpu", string k = "Temperature", string v = "1") =>
        $"{{\"v\":{v},\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false,\"r\":[{{\"id\":\"a\",\"n\":\"A\",\"h\":\"{h}\",\"k\":\"{k}\",\"val\":{val}}}]}}";

    [Fact]
    public void Round_trip_preserves_snapshot()
    {
        var s = Sample();
        Assert.True(Protocol.TryDecode(Protocol.Encode(s), out var d));
        Assert.NotNull(d);
        Assert.Equal(s.Timestamp, d!.Timestamp);
        Assert.Equal(s.Stale, d.Stale);
        Assert.Equal(s.Readings, d.Readings);
    }

    [Fact]
    public void Decode_accepts_valid_line() => Assert.True(Protocol.TryDecode(Line(), out _));

    [Fact]
    public void Decode_rejects_wrong_version() => Assert.False(Protocol.TryDecode(Line(v: "2"), out _));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    public void Decode_rejects_garbage(string line)
    {
        Assert.False(Protocol.TryDecode(line, out var s));
        Assert.Null(s);
    }

    [Fact]
    public void Decode_rejects_missing_readings()
    {
        Assert.False(Protocol.TryDecode("{\"v\":1,\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false}", out _));
        Assert.False(Protocol.TryDecode("{\"v\":1,\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false,\"r\":null}", out _));
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("1e999")]
    [InlineData("-1e999")]
    [InlineData("null")]
    public void Decode_rejects_non_finite_value(string val) => Assert.False(Protocol.TryDecode(Line(val), out _));

    [Theory]
    [InlineData("Bogus", "Temperature")]
    [InlineData("Cpu", "Bogus")]
    [InlineData("1", "Temperature")]
    [InlineData("Cpu", "0")]
    public void Decode_rejects_unknown_enum_names(string h, string k) =>
        Assert.False(Protocol.TryDecode(Line(h: h, k: k), out _));

    [Fact]
    public void Decode_rejects_oversized_line() =>
        Assert.False(Protocol.TryDecode(Line() + new string(' ', Protocol.MaxMessageBytes), out _));

    [Fact]
    public void Decode_ignores_unknown_fields() =>
        Assert.True(Protocol.TryDecode(Line().Replace("\"v\":1,", "\"v\":1,\"d\":\"x\","), out _));

    [Fact]
    public void Encode_has_no_newline_inside() => Assert.DoesNotContain('\n', Protocol.Encode(Sample()));
}
