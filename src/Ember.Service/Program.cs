using Ember.Core;
using Ember.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class Program
{
    private static int Main(string[] args) =>
        Run(ServiceHost.Create(args, () => new LhmSensorSource(), "ember.v1"));

    // Returns non-zero if the sensor source cannot be created (e.g. driver missing), so the
    // service-recovery settings restart the service.
    internal static int Run(IHostBuilder builder)
    {
        var faults = new CriticalLogTracker();
        builder.ConfigureLogging(l => l.AddProvider(faults));
        using var host = builder.Build();
        try
        {
            _ = host.Services.GetRequiredService<ISensorSource>();
        }
        catch (Exception ex)
        {
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ember.Service")
                .LogCritical(ex, "Cannot create the sensor source; exiting");
            return 1;
        }
        host.Run();
        // A faulted BackgroundService stops the host cleanly (exit 0) and the host logs it at Critical;
        // report that as a failure so service recovery restarts us.
        return faults.SawCritical || Environment.ExitCode != 0 ? 1 : 0;
    }
}

internal sealed class CriticalLogTracker : ILoggerProvider, ILogger
{
    private volatile bool _saw;
    public bool SawCritical => _saw;
    public ILogger CreateLogger(string categoryName) => this;
    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Critical;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Critical) _saw = true;
    }
    public void Dispose() { }
}
