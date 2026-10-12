using System.Text;

namespace Ember.Core.Tests;

public class SnapshotReaderTests
{
    static Snapshot Snap(double v) => new(DateTimeOffset.UnixEpoch,
        new[] { new SensorReading("a", "A", HardwareKind.Cpu, SensorKind.Load, v) }, false);

    static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    static async Task<List<Snapshot>> ReadAll(Stream s)
    {
        var list = new List<Snapshot>();
        await foreach (var x in SnapshotReader.ReadAsync(s, CancellationToken.None)) list.Add(x);
        return list;
    }

    [Fact]
    public async Task Reads_two_snapshots()
    {
        var all = await ReadAll(Stream(Protocol.Encode(Snap(1)) + "\n" + Protocol.Encode(Snap(2)) + "\n"));
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all[1].Readings[0].Value);
    }

    [Fact]
    public async Task Bad_line_throws_InvalidData() =>
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAll(Stream("garbage\n")));

    [Fact]
    public async Task Oversized_line_throws_InvalidData_without_reading_all()
    {
        var bytes = new byte[2 * 1024 * 1024];
        Array.Fill(bytes, (byte)'a');
        var s = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ReadAll(s));
        Assert.True(s.Position <= Protocol.MaxMessageBytes + 8192, $"position {s.Position}");
    }

    [Fact]
    public async Task Empty_stream_yields_nothing() => Assert.Empty(await ReadAll(new MemoryStream()));

    [Fact]
    public async Task Final_line_without_newline_is_ignored()
    {
        var all = await ReadAll(Stream(Protocol.Encode(Snap(1)) + "\n" + Protocol.Encode(Snap(2))));
        Assert.Single(all);
    }
}
