using System.IO.Pipes;
using System.Text;
using Ember.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

/// <summary>
/// The control pipe (ember.ctl.v1): one request line, one reply line. Any interactive local user can connect, so
/// every line is validated by <see cref="ControlProtocol"/> before the handler sees it, connections are capped, and
/// each connection is rate limited. A malformed or oversized line gets an "invalid" reply and the connection is closed.
/// </summary>
public sealed class ControlPipeServer(Func<ControlMessage, ControlReply> handle, string pipeName,
    ILogger<ControlPipeServer> log, TimeProvider? clock = null) : BackgroundService
{
    public const int MaxConnections = 8;
    public const int MessagesPerSecond = 20;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(10);

    internal Func<string, bool, NamedPipeServerStream> CreatePipe { get; init; } = PipeSecurityFactory.CreateControl;
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    readonly TimeProvider _clock = clock ?? TimeProvider.System;
    int _live; // pipe instances (listening or serving) currently open
    int _active; // connections being served

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clients = new List<Task>();
        NamedPipeServerStream? first;
        try { first = Track(CreatePipe(pipeName, true)); }
        catch (Exception ex)
        {
            log.LogCritical(ex, "Cannot claim pipe {Pipe} exclusively (already exists?); refusing to serve", pipeName);
            throw;
        }
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = first ?? Track(CreatePipe(pipeName, Volatile.Read(ref _live) == 0));
                    first = null;
                    await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (pipe is not null) await Release(pipe).ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested) break;
                    log.LogError(ex, "Control pipe accept failed; retrying");
                    try { await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }
                if (Volatile.Read(ref _active) >= MaxConnections)
                {
                    await Release(pipe).ConfigureAwait(false); // too many clients: drop this one
                    continue;
                }
                Interlocked.Increment(ref _active);
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(pipe, stoppingToken));
            }
        }
        finally
        {
            if (first is not null) await Release(first).ConfigureAwait(false);
            await Task.WhenAll(clients).ConfigureAwait(false); // ServeAsync never throws
        }
    }

    NamedPipeServerStream Track(NamedPipeServerStream pipe)
    {
        Interlocked.Increment(ref _live);
        return pipe;
    }

    async ValueTask Release(NamedPipeServerStream pipe)
    {
        try { await pipe.DisposeAsync().ConfigureAwait(false); }
        finally { Interlocked.Decrement(ref _live); }
    }

    async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            var reader = new BufferedStream(pipe, 4096);
            var tokens = (double)MessagesPerSecond;
            var last = _clock.GetTimestamp();
            while (!ct.IsCancellationRequested)
            {
                string? line;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(IdleTimeout); // a silent client must not hold one of the 8 slots forever
                try { line = await LineReader.ReadLineAsync(reader, ControlProtocol.MaxLineBytes, idle.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
                catch (InvalidDataException)
                {
                    await ReplyAsync(pipe, new ControlReply(false, "invalid"), ct).ConfigureAwait(false);
                    break;
                }
                if (line is null) break;

                var now = _clock.GetTimestamp();
                tokens = Math.Min(MessagesPerSecond, tokens + _clock.GetElapsedTime(last, now).TotalSeconds * MessagesPerSecond);
                last = now;
                if (tokens < 1)
                {
                    await ReplyAsync(pipe, new ControlReply(false, "rate limited"), ct).ConfigureAwait(false);
                    continue;
                }
                tokens -= 1;

                if (!ControlProtocol.TryDecode(line, out var message) || message is null)
                {
                    await ReplyAsync(pipe, new ControlReply(false, "invalid"), ct).ConfigureAwait(false);
                    break;
                }
                ControlReply reply;
                try { reply = handle(message); }
                catch (Exception ex)
                {
                    log.LogError(ex, "Control message handler failed");
                    reply = new ControlReply(false, "failed");
                }
                await ReplyAsync(pipe, reply, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogDebug(ex, "Control client ended"); }
        finally
        {
            Interlocked.Decrement(ref _active);
            await Release(pipe).ConfigureAwait(false);
        }
    }

    static async Task ReplyAsync(Stream pipe, ControlReply reply, CancellationToken ct)
    {
        // A client that never reads must not park this connection forever.
        using var write = CancellationTokenSource.CreateLinkedTokenSource(ct);
        write.CancelAfter(WriteTimeout);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(ControlProtocol.EncodeReply(reply) + "\n"), write.Token).ConfigureAwait(false);
        await pipe.FlushAsync(write.Token).ConfigureAwait(false);
    }
}
