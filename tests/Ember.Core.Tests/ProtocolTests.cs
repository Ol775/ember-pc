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
        Assert.True(Protocol.TryDecode(Line().Replace("\"v\":1,", "\"v\":1,\"future\":\"x\","), out _));

    [Theory]
    [InlineData(DriverStatus.Unknown)]
    [InlineData(DriverStatus.Ok)]
    [InlineData(DriverStatus.Missing)]
    public void Driver_status_round_trips(DriverStatus driver)
    {
        Assert.True(Protocol.TryDecode(Protocol.Encode(Sample() with { Driver = driver }), out var d));
        Assert.Equal(driver, d!.Driver);
    }

    [Fact]
    public void Missing_driver_field_decodes_as_unknown()
    {
        Assert.True(Protocol.TryDecode(Line(), out var d));
        Assert.Equal(DriverStatus.Unknown, d!.Driver);
    }

    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("\"1\"")]
    [InlineData("1")]
    public void Invalid_driver_value_is_rejected(string d) =>
        Assert.False(Protocol.TryDecode(Line().Replace("\"v\":1,", $"\"v\":1,\"d\":{d},"), out _));

    [Fact]
    public void Missing_val_is_rejected() =>
        Assert.False(Protocol.TryDecode(Line().Replace(",\"val\":54.0", ""), out _));

    [Fact]
    public void Missing_timestamp_is_rejected() =>
        Assert.False(Protocol.TryDecode(Line().Replace("\"t\":\"2026-10-12T08:30:00+00:00\",", ""), out _));

    // Also covered by the version comparison: a missing "v" would default to 0, and TryDecode rejects any v != 1.
    // So this passes with or without [JsonRequired] on "v"; it pins the behaviour, not the attribute.
    [Fact]
    public void Missing_version_is_rejected() =>
        Assert.False(Protocol.TryDecode(Line().Replace("\"v\":1,", ""), out _));

    [Fact]
    public void Driver_missing_with_no_readings_round_trips()
    {
        var s = new Snapshot(DateTimeOffset.UnixEpoch, [], false, DriverStatus.Missing);
        Assert.True(Protocol.TryDecode(Protocol.Encode(s), out var d));
        Assert.Empty(d!.Readings);
        Assert.Equal(DriverStatus.Missing, d.Driver);
    }

    [Fact]
    public void Encode_has_no_newline_inside() => Assert.DoesNotContain('\n', Protocol.Encode(Sample()));
}

public class ProtocolControlTests
{
    static Snapshot WithControl() => new(
        new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero), Array.Empty<SensorReading>(), false, DriverStatus.Ok,
        new FanControlStatus(FanMode.Quiet, ModeSource.GameOverride, true, "game.exe",
            new[] { new HeaderStatus("/lpc/nct6687d/0/control/2", "System Fan #1", HeaderState.Enabled, true) },
            new[] { "Fan Control" }, "Test passed"));

    [Fact]
    public void Round_trip_preserves_control_status()
    {
        var s = WithControl();
        Assert.True(Protocol.TryDecode(Protocol.Encode(s), out var d));
        var c = d!.Control!;
        Assert.Equal(FanMode.Quiet, c.Mode);
        Assert.Equal(ModeSource.GameOverride, c.Source);
        Assert.True(c.SafetyOverride);
        Assert.Equal("game.exe", c.GameExe);
        Assert.Equal(s.Control!.Headers, c.Headers);
        Assert.Equal(new[] { "Fan Control" }, c.Competitors);
        Assert.Equal("Test passed", c.TestMessage);
    }

    [Fact]
    public void Missing_c_decodes_as_null()
    {
        var line = "{\"v\":1,\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false,\"r\":[]}";
        Assert.True(Protocol.TryDecode(line, out var d));
        Assert.Null(d!.Control);
    }

    [Theory]
    [InlineData("\"st\":\"Bogus\"")]
    [InlineData("\"st\":1")]
    public void Invalid_header_state_rejected(string st)
    {
        var line = "{\"v\":1,\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false,\"r\":[],\"c\":{\"m\":\"Normal\",\"s\":\"User\",\"h\":[{\"id\":\"a\",\"n\":\"A\"," + st + "}],\"x\":[]}}";
        Assert.False(Protocol.TryDecode(line, out _));
    }

    [Fact]
    public void Invalid_mode_in_status_rejected()
    {
        var line = "{\"v\":1,\"t\":\"2026-10-12T08:30:00+00:00\",\"stale\":false,\"r\":[],\"c\":{\"m\":\"Turbo\",\"s\":\"User\",\"h\":[],\"x\":[]}}";
        Assert.False(Protocol.TryDecode(line, out _));
    }
}
