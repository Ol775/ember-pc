namespace Ember.Core.Tests;

public class ServerIdentityTests
{
    [Fact]
    public void Running_service_with_matching_pid_is_trusted() => Assert.True(ServerIdentity.IsTrusted(1234, 1234, true));

    [Fact]
    public void Mismatched_pid_is_rejected() => Assert.False(ServerIdentity.IsTrusted(1234, 999, true));

    [Fact]
    public void Zero_pid_is_rejected() => Assert.False(ServerIdentity.IsTrusted(0, 0, true));

    [Fact]
    public void Service_not_running_is_rejected() => Assert.False(ServerIdentity.IsTrusted(1234, 1234, false));

    [Fact]
    public void Unknown_service_pid_is_rejected() => Assert.False(ServerIdentity.IsTrusted(1234, null, true));
}
