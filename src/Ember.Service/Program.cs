using Ember.Core;
using Ember.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class Program
{
    private static int Main(string[] args)
    {
        LhmHardware? hardware = null;
        LhmHardware Hardware() => hardware ??= new LhmHardware();
        try
        {
            return Run(ServiceHost.Create(args, () => new LhmSensorSource(Hardware()), EmberNames.PipeName,
                () => new LhmFanController(Hardware())));
        }
        finally { hardware?.Dispose(); }
    }

    // Non-zero on any failure (source cannot be created, startup throws, a hosted service faulted) so the
    // service-recovery settings restart the service; handled here to avoid an unhandled-exception crash record.
    internal static int Run(IHostBuilder builder)
    {
        IHost? host = null;
        ILogger? log = null; // captured up front: host.Run disposes the provider
        try
        {
            host = builder.Build();
            _ = host.Services.GetRequiredService<ISensorSource>();
            log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ember.Service");
            var services = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToList();
            host.Run();
            // A faulted BackgroundService stops the host cleanly (exit 0), so check for faults explicitly.
            var faulted = services.Any(s => s.ExecuteTask is { IsFaulted: true });
            return faulted || Environment.ExitCode != 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            // The host may already be disposed, so logging can throw; always report to stderr too.
            try { log?.LogCritical(ex, "Ember service failed; exiting"); }
            catch { /* stderr below */ }
            Console.Error.WriteLine($"Ember service failed: {ex.Message}");
            return 1;
        }
        finally { host?.Dispose(); }
    }
}
