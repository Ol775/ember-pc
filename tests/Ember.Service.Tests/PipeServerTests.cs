using System.IO.Pipes;
using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public sealed class PipeServerTests : IAsyncLifetime
{
    private readonly string _name = $"ember.test.{Guid.NewGuid():N}";
    private readonly SnapshotHub _hub = new();
    private PipeServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = new PipeServer(_hub, _name, NullLogger<PipeServer>.Instance);
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await _server.StopAsync(CancellationToken.None);

    private static Snapshot Snap(double v) => new(DateTimeOffset.UnixEpoch, [new("id", "n", HardwareKind.Cpu, SensorKind.Load, v)], false);

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var c = new NamedPipeClientStream(".", _name, PipeDirection.In, PipeOptions.Asynchronous);
        await c.ConnectAsync(5000);
        return c;
    }

    private static CancellationToken Timeout(int ms = 5000) => new CancellationTokenSource(ms).Token;

    private static double Value(Snapshot s) => s.Readings[0].Value;

    [Fact]
    public async Task Client_receives_latest_then_new_snapshots()
    {
        _hub.Publish(Snap(1));
        using var c = await ConnectAsync();
        await using var e = SnapshotReader.ReadAsync(c, Timeout()).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(1, Value(e.Current));
        _hub.Publish(Snap(2));
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(2, Value(e.Current));
    }

    [Fact]
    public async Task Two_clients_both_receive()
    {
        _hub.Publish(Snap(1));
        using var a = await ConnectAsync();
        using var b = await ConnectAsync();
        await using var ea = SnapshotReader.ReadAsync(a, Timeout()).GetAsyncEnumerator();
        await using var eb = SnapshotReader.ReadAsync(b, Timeout()).GetAsyncEnumerator();
        Assert.True(await ea.MoveNextAsync());
        Assert.True(await eb.MoveNextAsync());
        _hub.Publish(Snap(2));
        Assert.True(await ea.MoveNextAsync());
        Assert.True(await eb.MoveNextAsync());
        Assert.Equal(2, Value(ea.Current));
        Assert.Equal(2, Value(eb.Current));
    }

    [Fact]
    public async Task Client_disconnect_does_not_stop_server()
    {
        _hub.Publish(Snap(1));
        using (var c = await ConnectAsync())
        {
            await using var e = SnapshotReader.ReadAsync(c, Timeout()).GetAsyncEnumerator();
            Assert.True(await e.MoveNextAsync());
        }
        _hub.Publish(Snap(2));
        using var c2 = await ConnectAsync();
        await using var e2 = SnapshotReader.ReadAsync(c2, Timeout()).GetAsyncEnumerator();
        Assert.True(await e2.MoveNextAsync());
        Assert.Equal(2, Value(e2.Current));
    }

    [Fact]
    public async Task Slow_client_does_not_block_fast_client()
    {
        _hub.Publish(Snap(0));
        using var slow = await ConnectAsync(); // never reads
        using var fast = await ConnectAsync();
        await using var e = SnapshotReader.ReadAsync(fast, Timeout(2000)).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        for (var i = 1; i <= 200; i++) _hub.Publish(Snap(i));
        var last = 0.0;
        while (last < 200 && await e.MoveNextAsync()) last = Value(e.Current);
        Assert.Equal(200, last);
    }

    [Fact]
    public async Task Unencodable_snapshot_is_skipped_and_client_keeps_receiving()
    {
        _hub.Publish(Snap(double.NaN)); // Latest is unencodable at connect time
        using var c = await ConnectAsync();
        await using var e = SnapshotReader.ReadAsync(c, Timeout()).GetAsyncEnumerator();
        // Give the server time to serve the unencodable Latest (it must skip it, not drop the client).
        await Task.Delay(100);
        _hub.Publish(Snap(3));
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(3, Value(e.Current));
    }

    [Fact]
    public async Task Accept_failure_is_logged_and_server_keeps_serving()
    {
        await _server.StopAsync(CancellationToken.None);
        var calls = 0;
        _server = new PipeServer(_hub, _name, NullLogger<PipeServer>.Instance)
        {
            RetryDelay = TimeSpan.FromMilliseconds(10),
            CreatePipe = n => calls++ == 0 ? throw new IOException("boom") : PipeSecurityFactory.Create(n),
        };
        await _server.StartAsync(CancellationToken.None);
        _hub.Publish(Snap(1));
        using var c = await ConnectAsync();
        await using var e = SnapshotReader.ReadAsync(c, Timeout()).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());
        Assert.Equal(1, Value(e.Current));
    }

    [Fact]
    public async Task Server_stops_on_cancellation_within_2s()
    {
        _hub.Publish(Snap(1));
        using var c = await ConnectAsync();
        using var cts = new CancellationTokenSource(2000);
        await _server.StopAsync(cts.Token);
        Assert.False(cts.IsCancellationRequested);
    }
}
