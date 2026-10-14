using Ember.StreamDeck;

// Stream Deck starts this program with: -port <n> -pluginUUID <id> -registerEvent registerPlugin -info <json>
static string? Arg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

if (!int.TryParse(Arg(args, "-port"), out var port) || Arg(args, "-pluginUUID") is not { Length: > 0 } uuid)
    return 2;

using var client = new StreamDeckClient();
using var cts = new CancellationTokenSource();
try
{
    await client.ConnectAsync(port, cts.Token);
    await new Deck(client, uuid).RunAsync(cts.Token);
    return 0;
}
catch (Exception)
{
    return 1;
}
