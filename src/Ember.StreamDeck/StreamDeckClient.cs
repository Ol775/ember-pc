using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using Ember.Core;

namespace Ember.StreamDeck;

/// <summary>The WebSocket to the Stream Deck app: send one text message, or read incoming ones until the app closes the socket.</summary>
sealed class StreamDeckClient : IDisposable
{
    const int MaxMessageBytes = 64 * 1024;
    readonly ClientWebSocket _ws = new();
    readonly SemaphoreSlim _sendLock = new(1, 1);

    public Task ConnectAsync(int port, CancellationToken ct) => _ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), ct);

    public async Task SendAsync(string message, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try { await _ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
        catch (WebSocketException) { /* the app is closing: the receive loop ends the run */ }
        finally { _sendLock.Release(); }
    }

    /// <summary>Whole messages, fragments joined. A message over 64 KB is skipped. Ends when the app closes the socket.</summary>
    public async IAsyncEnumerable<string> ReceiveAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[8192];
        var message = new WebSocketMessageAssembler(MaxMessageBytes);
        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try { result = await _ws.ReceiveAsync(buffer, ct).ConfigureAwait(false); }
            catch (WebSocketException) { yield break; }
            catch (OperationCanceledException) { yield break; }
            if (result.MessageType == WebSocketMessageType.Close) yield break;
            var text = message.Add(buffer.AsSpan(0, result.Count), result.EndOfMessage);
            if (text is not null) yield return text;
        }
    }

    public void Dispose()
    {
        _ws.Dispose();
        _sendLock.Dispose();
    }
}
