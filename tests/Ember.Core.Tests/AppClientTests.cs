namespace Ember.Core.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class AppClientTests
{
    [Fact]
    public async Task Round_trip_against_a_running_server()
    {
        var name = $"ember.appclient.{Guid.NewGuid():N}";
        await using var server = new AppPipeServer(r => new AppReply(true, null, new AppStatus(true, r.GetType().Name, ["A"], 1)), name);
        server.Start();
        var reply = await AppClient.SendAsync(new AppStatusRequest(), name, verifyServer: false);
        Assert.True(reply.Ok);
        Assert.Equal("AppStatusRequest", reply.Status!.CurrentScene);
    }

    [Fact]
    public async Task When_the_app_is_not_running_the_reply_says_so_and_nothing_throws()
    {
        var reply = await AppClient.SendAsync(new AppStatusRequest(), $"ember.appclient.none.{Guid.NewGuid():N}", verifyServer: false);
        Assert.False(reply.Ok);
        Assert.Equal("Ember is not running.", reply.Error);
    }

    [Fact]
    public async Task A_server_that_is_not_the_ember_app_is_refused()
    {
        var name = $"ember.appclient.{Guid.NewGuid():N}";
        await using var server = new AppPipeServer(_ => new AppReply(true, null, null), name);
        server.Start();
        var reply = await AppClient.SendAsync(new AppStatusRequest(), name, verifyServer: true); // this process is the test host, not Ember.App
        Assert.False(reply.Ok);
        Assert.NotEqual("Ember is not running.", reply.Error);
    }
}
