using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Ember.Core;

/// <summary>
/// The app pipe (ember.app.v1): one request line, one reply line, repeated per connection. Only the signed-in user who runs
/// the app may connect (the pipe's ACL names just that user), the first instance claims the name exclusively, connections
/// are capped and rate limited, and every line goes through <see cref="AppProtocol"/> before the handler sees it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AppPipeServer : IAsyncDisposable
{
    public const int MaxConnections = 4;
    public const int MessagesPerSecond = 20;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);

    readonly Func<AppRequest, AppReply> _handle;
    readonly string _name;
    readonly CancellationTokenSource _cts = new();
    NamedPipeServerStream? _first;
    Task? _loop;
    int _active;

    public AppPipeServer(Func<AppRequest, AppReply> handle, string pipeName)
    {
        _handle = handle;
        _name = pipeName;
    }

    /// <summary>Claims the pipe name (throws an <see cref="IOException"/> when something else already owns it) and starts serving.</summary>
    public void Start()
    {
        _first = Create(true);
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    NamedPipeServerStream Create(bool first)
    {
        var security = new PipeSecurity();
        using (var self = WindowsIdentity.GetCurrent())
            security.AddAccessRule(new PipeAccessRule(self.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        return NamedPipeServerStreamAcl.Create(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    async Task RunAsync(CancellationToken ct)
    {
        var clients = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = _first ?? Create(false);
                    _first = null;
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    pipe?.Dispose();
                    if (ct.IsCancellationRequested) break;
                    DiagLog.Warn("AppPipe", "Accept failed; retrying", ex);
                    try { await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }
                if (Volatile.Read(ref _active) >= MaxConnections) { pipe.Dispose(); continue; }
                Interlocked.Increment(ref _active);
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(pipe, ct));
            }
        }
        finally
        {
            _first?.Dispose();
            await Task.WhenAll(clients).ConfigureAwait(false); // ServeAsync never throws
        }
    }

    async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            var reader = new BufferedStream(pipe, 4096);
            var tokens = (double)MessagesPerSecond;
            var last = DateTime.UtcNow;
            while (!ct.IsCancellationRequested)
            {
                string? line;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(IdleTimeout);
                try { line = await LineReader.ReadLineAsync(reader, AppProtocol.MaxLineBytes, idle.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
                catch (InvalidDataException)
                {
                    await ReplyAsync(pipe, new AppReply(false, "invalid", null), ct).ConfigureAwait(false);
                    break;
                }
                if (line is null) break;

                var now = DateTime.UtcNow;
                tokens = Math.Min(MessagesPerSecond, tokens + (now - last).TotalSeconds * MessagesPerSecond);
                last = now;
                if (tokens < 1)
                {
                    await ReplyAsync(pipe, new AppReply(false, "rate limited", null), ct).ConfigureAwait(false);
                    continue;
                }
                tokens -= 1;

                if (!AppProtocol.TryDecode(line, out var request) || request is null)
                {
                    await ReplyAsync(pipe, new AppReply(false, "invalid", null), ct).ConfigureAwait(false);
                    break;
                }
                AppReply reply;
                try { reply = _handle(request); }
                catch (Exception ex)
                {
                    DiagLog.Warn("AppPipe", "Request handler failed", ex);
                    reply = new AppReply(false, "failed", null);
                }
                await ReplyAsync(pipe, reply, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagLog.Info("AppPipe", "Client ended: " + ex.Message); }
        finally
        {
            Interlocked.Decrement(ref _active);
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    static async Task ReplyAsync(Stream s, AppReply reply, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await s.WriteAsync(Encoding.UTF8.GetBytes(AppProtocol.EncodeReply(reply) + "\n"), timeout.Token).ConfigureAwait(false);
        await s.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loop is not null) await _loop.ConfigureAwait(false);
        _first?.Dispose();
        _cts.Dispose();
    }
}
