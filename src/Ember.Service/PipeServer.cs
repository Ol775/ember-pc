using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using Ember.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

public sealed class PipeServer(SnapshotHub hub, string pipeName, ILogger<PipeServer> log) : BackgroundService
{
    internal Func<string, NamedPipeServerStream> CreatePipe { get; init; } = PipeSecurityFactory.Create;
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clients = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = CreatePipe(pipeName);
                    await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (pipe is not null) await pipe.DisposeAsync().ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested) break;
                    log.LogError(ex, "Pipe accept failed; retrying");
                    try { await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }
                clients.RemoveAll(t => t.IsCompleted);
                clients.Add(ServeAsync(pipe, stoppingToken));
            }
        }
        finally
        {
            await Task.WhenAll(clients).ConfigureAwait(false); // ServeAsync never throws
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        // Capacity 1 + DropOldest: a slow client only ever misses old snapshots; Publish never blocks.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        void Enqueue(Snapshot s)
        {
            try { channel.Writer.TryWrite(Protocol.Encode(s) + "\n"); }
            catch (Exception ex) { log.LogWarning(ex, "Skipping snapshot that cannot be encoded"); }
        }

        hub.Published += Enqueue;
        try
        {
            if (hub.Latest is { } latest) Enqueue(latest);
            await foreach (var line in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await pipe.WriteAsync(Encoding.UTF8.GetBytes(line), ct).ConfigureAwait(false);
                await pipe.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogDebug(ex, "Pipe client ended"); }
        finally
        {
            hub.Published -= Enqueue;
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }
}
