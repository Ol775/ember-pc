using System.Text;

namespace Ember.Core;

/// <summary>
/// Joins the fragments of one WebSocket text message. A message that grows past the limit is dropped whole, including any
/// later fragments of it, so a truncated start can never be mistaken for a message. Text is decoded only once the message is complete.
/// </summary>
public sealed class WebSocketMessageAssembler(int maxBytes)
{
    readonly MemoryStream _buffer = new();
    bool _overflow;

    /// <summary>Adds a fragment. Returns the whole message when <paramref name="endOfMessage"/> is true and it is usable, otherwise null.</summary>
    public string? Add(ReadOnlySpan<byte> chunk, bool endOfMessage)
    {
        if (!_overflow)
        {
            if (_buffer.Length + chunk.Length > maxBytes) { _overflow = true; _buffer.SetLength(0); }
            else _buffer.Write(chunk);
        }
        if (!endOfMessage) return null;
        var text = !_overflow && _buffer.Length > 0 ? Encoding.UTF8.GetString(_buffer.GetBuffer(), 0, (int)_buffer.Length) : null;
        _buffer.SetLength(0);
        _overflow = false;
        return text;
    }
}
