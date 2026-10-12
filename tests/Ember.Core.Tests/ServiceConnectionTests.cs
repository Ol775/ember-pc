using System.Collections.Concurrent;
using System.Text;

namespace Ember.Core.Tests;

public class ServiceConnectionTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan[] Fast = { TimeSpan.FromMilliseconds(1) };

    static Snapshot Snap(double v) => new(DateTimeOffset.UnixEpoch,
        new[] { new SensorReading("a", "A", HardwareKind.Cpu, SensorKind.Load, v) }, false);

    static Stream Lines(params string[] lines) =>
        new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n"))));

    sealed class TrackedStream(Stream inner) : Stream
    {
        public bool Disposed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override ValueTask<int> ReadAsync(Memory<byte> m, CancellationToken ct = default) => inner.ReadAsync(m, ct);
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    /// <summary>Read blocks until cancelled, like an idle pipe.</summary>
    sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> m, CancellationToken ct = default)
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return 0;
        }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    /// <summary>Each connect attempt takes the next step; after the script runs out, it hangs until cancelled.</summary>
    static Func<CancellationToken, Task<Stream>> Script(params Func<Stream>[] steps)
    {
        var i = -1;
        return ct =>
        {
            var n = Interlocked.Increment(ref i);
            return n < steps.Length ? Task.FromResult(steps[n]()) : Task.FromResult<Stream>(new HangingStream());
        };
    }

    static Func<Stream> Throw() => () => throw new IOException("service not running");

    static Func<CancellationToken, Task<Stream>> ScriptThatMayThrow(params Func<Stream>[] steps)
    {
        var s = Script(steps);
        return ct => { try { return s(ct); } catch (Exception e) { return Task.FromException<Stream>(e); } };
    }

    /// <summary>Runs until <paramref name="done"/> completes (or timeout), then cancels and awaits RunAsync.</summary>
    static async Task RunUntil(ServiceConnection c, Func<Task> done)
    {
        using var cts = new CancellationTokenSource();
        var run = c.RunAsync(cts.Token);
        await Task.WhenAny(done(), Task.Delay(Timeout));
        cts.Cancel();
        await run.WaitAsync(Timeout);
    }

    static Task WaitFor(Func<bool> cond) => Task.Run(async () =>
    {
        var end = DateTime.UtcNow + Timeout;
        while (!cond() && DateTime.UtcNow < end) await Task.Delay(1);
    });

    [Fact]
    public async Task Connects_and_raises_snapshots()
    {
        var c = new ServiceConnection(ScriptThatMayThrow(() => Lines(Protocol.Encode(Snap(1)), Protocol.Encode(Snap(2)))), Fast);
        var snaps = new ConcurrentQueue<Snapshot>();
        var states = new ConcurrentQueue<ConnectionState>();
        c.SnapshotReceived += snaps.Enqueue;
        c.StateChanged += states.Enqueue;

        await RunUntil(c, () => WaitFor(() => snaps.Count >= 2));

        Assert.Equal(new[] { 1.0, 2.0 }, snaps.Take(2).Select(s => s.Readings[0].Value));
        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected }, states.Take(2));
    }

    [Fact]
    public async Task Connect_failure_goes_unavailable_then_retries()
    {
        var c = new ServiceConnection(ScriptThatMayThrow(Throw(), Throw(), () => Lines(Protocol.Encode(Snap(7)))), Fast);
        var states = new ConcurrentQueue<ConnectionState>();
        var snaps = new ConcurrentQueue<Snapshot>();
        c.StateChanged += states.Enqueue;
        c.SnapshotReceived += snaps.Enqueue;

        await RunUntil(c, () => WaitFor(() => !snaps.IsEmpty));

        Assert.Single(snaps);
        Assert.Equal(new[]
        {
            ConnectionState.Connecting, ConnectionState.Unavailable, ConnectionState.Connecting,
            ConnectionState.Unavailable, ConnectionState.Connecting, ConnectionState.Connected,
        }, states.Take(6));
    }

    [Fact]
    public async Task Stream_end_goes_unavailable_then_reconnects()
    {
        var c = new ServiceConnection(ScriptThatMayThrow(() => Lines(Protocol.Encode(Snap(1))), () => Lines(Protocol.Encode(Snap(2)))), Fast);
        var snaps = new ConcurrentQueue<Snapshot>();
        var states = new ConcurrentQueue<ConnectionState>();
        c.SnapshotReceived += snaps.Enqueue;
        c.StateChanged += states.Enqueue;

        await RunUntil(c, () => WaitFor(() => snaps.Count >= 2));

        Assert.Equal(2, snaps.Count);
        Assert.Equal(new[]
        {
            ConnectionState.Connecting, ConnectionState.Connected, ConnectionState.Unavailable,
            ConnectionState.Connecting, ConnectionState.Connected,
        }, states.Take(5));
    }

    [Fact]
    public async Task Bad_message_drops_connection_and_reconnects()
    {
        var first = new TrackedStream(Lines("garbage"));
        var c = new ServiceConnection(ScriptThatMayThrow(() => first, () => Lines(Protocol.Encode(Snap(3)))), Fast);
        var snaps = new ConcurrentQueue<Snapshot>();
        var states = new ConcurrentQueue<ConnectionState>();
        c.SnapshotReceived += snaps.Enqueue;
        c.StateChanged += states.Enqueue;

        await RunUntil(c, () => WaitFor(() => !snaps.IsEmpty));

        Assert.Single(snaps);
        Assert.True(first.Disposed);
        Assert.Contains(ConnectionState.Unavailable, states);
    }

    [Fact]
    public async Task Oversized_message_drops_connection()
    {
        var huge = new string('a', Protocol.MaxMessageBytes + 10);
        var first = new TrackedStream(Lines(huge));
        var c = new ServiceConnection(ScriptThatMayThrow(() => first, () => Lines(Protocol.Encode(Snap(4)))), Fast);
        var snaps = new ConcurrentQueue<Snapshot>();
        var states = new ConcurrentQueue<ConnectionState>();
        c.SnapshotReceived += snaps.Enqueue;
        c.StateChanged += states.Enqueue;

        await RunUntil(c, () => WaitFor(() => !snaps.IsEmpty));

        Assert.Single(snaps);
        Assert.True(first.Disposed);
        Assert.Equal(new[] { ConnectionState.Connecting, ConnectionState.Connected, ConnectionState.Unavailable }, states.Take(3));
    }

    [Fact]
    public async Task Stream_is_disposed_when_connection_ends()
    {
        var s = new TrackedStream(Lines(Protocol.Encode(Snap(1))));
        var c = new ServiceConnection(ScriptThatMayThrow(() => s), Fast);
        var states = new ConcurrentQueue<ConnectionState>();
        c.StateChanged += states.Enqueue;

        await RunUntil(c, () => WaitFor(() => states.Contains(ConnectionState.Unavailable)));

        Assert.True(s.Disposed);
    }

    [Fact]
    public async Task Backoff_grows_then_caps()
    {
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var c = new ServiceConnection(
            _ => Task.FromException<Stream>(new IOException("down")),
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) },
            (d, ct) =>
            {
                delays.Add(d);
                if (delays.Count == 6) cts.Cancel();
                return Task.CompletedTask;
            });

        await c.RunAsync(cts.Token).WaitAsync(Timeout);

        Assert.Equal(new[] { 1.0, 2, 5, 5, 5, 5 }, delays.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task Snapshot_resets_backoff()
    {
        var delays = new List<TimeSpan>();
        using var cts = new CancellationTokenSource();
        var connect = ScriptThatMayThrow(Throw(), Throw(), () => Lines(Protocol.Encode(Snap(1))), Throw());
        var c = new ServiceConnection(connect,
            new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) },
            (d, ct) =>
            {
                delays.Add(d);
                if (delays.Count == 4) cts.Cancel();
                return Task.CompletedTask;
            });

        await c.RunAsync(cts.Token).WaitAsync(Timeout);

        // fail, fail, snapshot+end (counter reset, so b[0]), fail (b[1])
        Assert.Equal(new[] { 1.0, 2, 1, 2 }, delays.Select(d => d.TotalSeconds));
    }

    [Fact]
    public async Task Cancellation_ends_RunAsync_without_throwing_while_reading()
    {
        var c = new ServiceConnection(ct => Task.FromResult<Stream>(new HangingStream()), Fast);
        var states = new ConcurrentQueue<ConnectionState>();
        c.StateChanged += states.Enqueue;
        using var cts = new CancellationTokenSource();
        var run = c.RunAsync(cts.Token);
        await WaitFor(() => states.Contains(ConnectionState.Connected));

        cts.Cancel();
        await run.WaitAsync(Timeout); // must complete normally, not throw OperationCanceledException

        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cancellation_ends_RunAsync_without_throwing_while_connecting()
    {
        var c = new ServiceConnection(async ct =>
        {
            await Task.Delay(System.Threading.Timeout.Infinite, ct);
            return new HangingStream();
        }, Fast);
        using var cts = new CancellationTokenSource();
        var run = c.RunAsync(cts.Token);
        cts.CancelAfter(20);

        await run.WaitAsync(Timeout);

        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cancellation_during_backoff_ends_RunAsync()
    {
        var c = new ServiceConnection(_ => Task.FromException<Stream>(new IOException()),
            new[] { TimeSpan.FromHours(1) });
        using var cts = new CancellationTokenSource();
        var run = c.RunAsync(cts.Token);
        cts.CancelAfter(20);

        await run.WaitAsync(Timeout);

        Assert.True(run.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Subscriber_exception_does_not_end_loop()
    {
        var c = new ServiceConnection(ScriptThatMayThrow(() => Lines(Protocol.Encode(Snap(1)), Protocol.Encode(Snap(2)))), Fast);
        var snaps = new ConcurrentQueue<Snapshot>();
        c.SnapshotReceived += _ => throw new InvalidOperationException("boom");
        c.SnapshotReceived += snaps.Enqueue;
        c.StateChanged += _ => throw new InvalidOperationException("boom");

        await RunUntil(c, () => WaitFor(() => snaps.Count >= 2));

        Assert.Equal(2, snaps.Count);
    }

    [Fact]
    public void Constructor_validates_arguments()
    {
        Func<CancellationToken, Task<Stream>> connect = _ => Task.FromResult<Stream>(new MemoryStream());
        Assert.Throws<ArgumentNullException>(() => new ServiceConnection(null!, Fast));
        Assert.Throws<ArgumentException>(() => new ServiceConnection(connect, Array.Empty<TimeSpan>()));
        Assert.Throws<ArgumentException>(() => new ServiceConnection(connect, new[] { TimeSpan.FromSeconds(-1) }));
        Assert.Equal(4, ServiceConnection.DefaultBackoff.Length);
    }
}
