using System.Runtime.CompilerServices;

namespace Ember.Core;

public static class SnapshotReader
{
    public static async IAsyncEnumerable<Snapshot> ReadAsync(Stream s, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffered = new BufferedStream(s, 8192);
        while (await LineReader.ReadLineAsync(buffered, Protocol.MaxMessageBytes, ct).ConfigureAwait(false) is { } line)
        {
            if (!Protocol.TryDecode(line, out var snap) || snap is null)
                throw new InvalidDataException("Malformed snapshot line.");
            yield return snap;
        }
    }
}
