namespace Ember.Core;

public static class LineReader
{
    /// <summary>
    /// Reads up to '\n' (excluded). Null at end of stream; an unterminated final line is incomplete and dropped.
    /// Throws InvalidDataException once the line exceeds maxBytes. Reads one byte at a time from the stream,
    /// so callers should pass a buffered stream (SnapshotReader does) and no bytes are lost between calls.
    /// </summary>
    public static async Task<string?> ReadLineAsync(Stream s, int maxBytes, CancellationToken ct)
    {
        var line = new List<byte>(256);
        var one = new byte[1];
        while (true)
        {
            if (await s.ReadAsync(one.AsMemory(), ct).ConfigureAwait(false) == 0) return null;
            if (one[0] == (byte)'\n') return System.Text.Encoding.UTF8.GetString(line.ToArray());
            if (line.Count >= maxBytes) throw new InvalidDataException($"Line exceeds {maxBytes} bytes.");
            line.Add(one[0]);
        }
    }
}
