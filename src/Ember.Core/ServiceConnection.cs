using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ember.Core.Tests")]

namespace Ember.Core;

public enum ConnectionState { Connecting, Connected, Unavailable }

/// <summary>
/// Reconnecting client for the service pipe. Every connection problem becomes Unavailable,
/// a backoff wait, then a retry; nothing but cancellation ends <see cref="RunAsync"/>.
/// </summary>
public sealed class ServiceConnection
{
    public static readonly TimeSpan[] DefaultBackoff =
    {
        TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5),
    };

    readonly Func<CancellationToken, Task<Stream>> _connect;
    readonly TimeSpan[] _backoff;
    readonly Func<TimeSpan, CancellationToken, Task> _delay;
    ConnectionState? _state;

    public event Action<ConnectionState>? StateChanged;
    public event Action<Snapshot>? SnapshotReceived;

    public ServiceConnection(Func<CancellationToken, Task<Stream>> connect, TimeSpan[] backoff)
        : this(connect, backoff, Task.Delay) { }

    internal ServiceConnection(Func<CancellationToken, Task<Stream>> connect, TimeSpan[] backoff,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(backoff);
        ArgumentNullException.ThrowIfNull(delay);
        if (backoff.Length == 0) throw new ArgumentException("Backoff must not be empty.", nameof(backoff));
        if (backoff.Any(b => b < TimeSpan.Zero)) throw new ArgumentException("Backoff delays must not be negative.", nameof(backoff));
        _connect = connect;
        _backoff = (TimeSpan[])backoff.Clone();
        _delay = delay;
    }

    /// <summary>Runs until <paramref name="ct"/> is cancelled, then returns normally (no OperationCanceledException).</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_state is null) SetState(ConnectionState.Connecting); // after a failure stay Unavailable, so retries don't flicker
                var stream = await _connect(ct).ConfigureAwait(false);
                await using (stream.ConfigureAwait(false))
                {
                    SetState(ConnectionState.Connected);
                    await foreach (var snap in SnapshotReader.ReadAsync(stream, ct).ConfigureAwait(false))
                    {
                        attempt = 0;
                        Raise(SnapshotReceived, snap);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { /* connect failure, IO error, InvalidDataException: all mean "reconnect" */ }

            if (ct.IsCancellationRequested) break;
            SetState(ConnectionState.Unavailable);
            try { await _delay(_backoff[Math.Min(attempt, _backoff.Length - 1)], ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            attempt++;
        }
    }

    void SetState(ConnectionState s)
    {
        if (_state == s) return;
        _state = s;
        Raise(StateChanged, s);
    }

    // A throwing subscriber must neither end the loop nor starve later subscribers.
    static void Raise<T>(Action<T>? handlers, T arg)
    {
        if (handlers is null) return;
        foreach (var h in handlers.GetInvocationList())
        {
            try { ((Action<T>)h)(arg); } catch (Exception) { }
        }
    }
}
