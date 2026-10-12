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
        return 0;
    }
}
