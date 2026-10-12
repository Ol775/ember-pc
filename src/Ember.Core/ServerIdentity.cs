namespace Ember.Core;

public static class ServerIdentity
{
    /// <summary>
    /// The pipe server is the Ember service only if the service is running and the Service Control Manager
    /// reports the same non-zero process id as the pipe server.
    /// </summary>
    public static bool IsTrusted(uint pipeServerPid, uint? servicePid, bool serviceRunning) =>
        serviceRunning && servicePid is { } pid && pid != 0 && pid == pipeServerPid;
}
