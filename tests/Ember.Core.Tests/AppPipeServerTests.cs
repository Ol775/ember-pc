using System.IO.Pipes;
using System.Text;

namespace Ember.Core.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AppPipeServerTests : IAsyncLifetime
{
    readonly string _name = $"ember.apptest.{Guid.NewGuid():N}";
    readonly List<AppRequest> _received = [];
    AppPipeServer _server = null!;

    public Task InitializeAsync()
    {
        _server = new AppPipeServer(r =>
        {
            lock (_received) _received.Add(r);
            return new AppReply(true, null, new AppStatus(true, "Warm glow", ["Warm glow", "Cool"], 1));
        }, _name);
        _server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    async Task<NamedPipeClientStream> ConnectAsync()
    {
        var c = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await c.ConnectAsync(5000);
        return c;
    }

    static async Task SendAsync(Stream s, string line) => await s.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));

    static async Task<AppReply?> ReadReplyAsync(Stream s)
    {
        using var cts = new CancellationTokenSource(5000);
        var line = await LineReader.ReadLineAsync(s, AppProtocol.MaxLineBytes, cts.Token);
        return line is not null && AppProtocol.TryDecodeReply(line, out var r) ? r : null;
    }

    [Fact]
    public async Task Status_request_is_answered_with_the_handler_reply()
    {
        using var c = await ConnectAsync();
        await SendAsync(c, AppProtocol.Encode(new AppStatusRequest()));
        var reply = await ReadReplyAsync(c);
        Assert.True(reply!.Ok);
        Assert.Equal(["Warm glow", "Cool"], reply.Status!.Scenes);
        Assert.IsType<AppStatusRequest>(Assert.Single(_received));
    }

    [Fact]
    public async Task Garbage_gets_invalid_and_the_server_keeps_serving()
    {
        using (var bad = await ConnectAsync())
        {
            await SendAsync(bad, "this is not json");
            var reply = await ReadReplyAsync(bad);
            Assert.False(reply!.Ok);
            Assert.Equal("invalid", reply.Error);
        }
        using var good = await ConnectAsync();
        await SendAsync(good, AppProtocol.Encode(new ShowWindowRequest()));
        Assert.True((await ReadReplyAsync(good))!.Ok);
        Assert.Single(_received);
    }

    [Fact]
    public async Task Connections_beyond_the_cap_are_dropped()
    {
        var held = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < AppPipeServer.MaxConnections; i++)
            {
                var c = await ConnectAsync();
                held.Add(c);
                await SendAsync(c, AppProtocol.Encode(new AppStatusRequest()));
                Assert.True((await ReadReplyAsync(c))!.Ok); // proves this one is being served
            }
            using var extra = await ConnectAsync();
            using var cts = new CancellationTokenSource(3000);
            string? line = null;
            try
            {
                await SendAsync(extra, AppProtocol.Encode(new AppStatusRequest()));
                line = await LineReader.ReadLineAsync(extra, AppProtocol.MaxLineBytes, cts.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            Assert.Null(line); // dropped: closed or silent, never answered
        }
        finally { foreach (var c in held) c.Dispose(); }
    }

    [Fact]
    public void A_second_server_on_the_same_name_refuses_to_start()
    {
        var other = new AppPipeServer(_ => new AppReply(true, null, null), _name);
        Assert.ThrowsAny<Exception>(() => other.Start()); // the name is taken: access denied or IO error, never a silent second server
    }
}
