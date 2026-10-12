using System.IO.Pipes;
using System.Text;
using Ember.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ember.Service.Tests;

public sealed class ControlPipeServerTests : IAsyncLifetime
{
    private readonly string _name = $"ember.ctltest.{Guid.NewGuid():N}";
    private readonly List<ControlMessage> _received = new();
    private ControlPipeServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = new ControlPipeServer(m => { lock (_received) _received.Add(m); return new ControlReply(true, null); }, _name,
            NullLogger<ControlPipeServer>.Instance);
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync() => await _server.StopAsync(CancellationToken.None);

    private async Task<NamedPipeClientStream> ConnectAsync()
    {
        var c = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c.ConnectAsync(5000);
        return c;
    }

    private static async Task SendAsync(Stream s, string line) =>
        await s.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));

    private static async Task<ControlReply?> ReadReplyAsync(Stream s)
    {
        using var cts = new CancellationTokenSource(5000);
        var line = await LineReader.ReadLineAsync(s, 4096, cts.Token);
        return line is not null && ControlProtocol.TryDecodeReply(line, out var r) ? r : null;
    }

    [Fact]
    public async Task Valid_message_reaches_handler_and_reply_returned()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, ControlProtocol.Encode(new SetMode(FanMode.Quiet)));
        var reply = await ReadReplyAsync(c);
        Assert.True(reply!.Ok);
        Assert.Equal(new SetMode(FanMode.Quiet), Assert.Single(_received));
    }

    [Fact]
    public async Task Several_messages_on_one_connection()
    {
        using var c = await ConnectAsync();
        for (var i = 0; i < 3; i++)
        {
            await SendAsync(c, ControlProtocol.Encode(new DetectorHeartbeat()));
            Assert.True((await ReadReplyAsync(c))!.Ok);
        }
        Assert.Equal(3, _received.Count);
    }

    [Fact]
    public async Task Malformed_line_replies_invalid_and_closes()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, "not json");
        var reply = await ReadReplyAsync(c);
        Assert.False(reply!.Ok);
        Assert.Equal("invalid", reply.Error);
        Assert.Null(await ReadReplyAsync(c)); // closed
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Oversized_line_closes()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, new string('x', ControlProtocol.MaxLineBytes + 10));
        Assert.Equal("invalid", (await ReadReplyAsync(c))!.Error);
        Assert.Null(await ReadReplyAsync(c));
    }

    [Fact]
    public async Task Ai_mode_never_reaches_handler()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, "{\"v\":1,\"op\":\"setMode\",\"mode\":\"Ai\"}");
        Assert.False((await ReadReplyAsync(c))!.Ok);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task Rate_limit_after_20_per_second()
    {
        using var c = await ConnectAsync();
        var line = ControlProtocol.Encode(new DetectorHeartbeat());
        // Pipes here are unbuffered, so send and read at the same time or both sides block on write.
        var sender = Task.Run(async () => { for (var i = 0; i < 60; i++) await SendAsync(c, line); });
        var limited = 0;
        for (var i = 0; i < 60; i++)
            if ((await ReadReplyAsync(c))!.Error == "rate limited") limited++;
        await sender;
        Assert.True(limited >= 20, $"only {limited} rate limited");
        Assert.True(_received.Count < 60);
    }

    [Fact]
    public async Task Ninth_connection_is_refused()
    {
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < ControlPipeServer.MaxConnections; i++)
            {
                var c = await ConnectAsync();
                clients.Add(c);
                await SendAsync(c, ControlProtocol.Encode(new DetectorHeartbeat()));
                Assert.True((await ReadReplyAsync(c))!.Ok); // proves this one is being served
            }
            using var ninth = await ConnectAsync();
            ControlReply? reply = null;
            try
            {
                await SendAsync(ninth, ControlProtocol.Encode(new DetectorHeartbeat()));
                reply = await ReadReplyAsync(ninth);
            }
            catch (IOException) { /* the server closed the pipe before we wrote: also a refusal */ }
            Assert.Null(reply);
        }
        finally
        {
            foreach (var c in clients) c.Dispose();
        }
    }

    [Fact]
    public async Task Squatter_makes_start_fail()
    {
        using var squatter = new NamedPipeServerStream(_name + ".sq", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var second = new ControlPipeServer(_ => new ControlReply(true, null), _name + ".sq", NullLogger<ControlPipeServer>.Instance);
        // The exclusive claim fails at once, so starting the host fails instead of serving beside the squatter.
        await Assert.ThrowsAnyAsync<Exception>(() => second.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Stops_cleanly_on_cancel_with_connected_client()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, ControlProtocol.Encode(new DetectorHeartbeat()));
        await ReadReplyAsync(c);
        await _server.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
