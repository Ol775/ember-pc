using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Ember.Core;

/// <summary>
/// Sends one control message to the service and returns its reply. The server is verified as the Ember service
/// first, like the read pipe. Failures become a plain-language reply, never an exception.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class ControlClient
{
    public static async Task<ControlReply> SendAsync(ControlMessage message, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var pipe = await PipeStreamConnector.ConnectAsync(EmberNames.ControlPipeName, PipeDirection.InOut, timeout.Token)
                .ConfigureAwait(false);
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(ControlProtocol.Encode(message) + "\n"), timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            var line = await LineReader.ReadLineAsync(new BufferedStream(pipe), ControlProtocol.MaxLineBytes, timeout.Token).ConfigureAwait(false);
            return line is not null && ControlProtocol.TryDecodeReply(line, out var reply) && reply is not null
                ? reply
                : new ControlReply(false, "The service did not answer.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ControlReply(false, "The service did not answer in time.");
        }
        catch (Exception)
        {
            return new ControlReply(false, "Could not reach the Ember service.");
        }
    }
}
