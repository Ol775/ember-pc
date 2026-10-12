using Ember.Core;
using Ember.Watchdog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

try
{
    Host.CreateDefaultBuilder(args)
        .UseWindowsService(o => o.ServiceName = EmberNames.WatchdogServiceName)
        .ConfigureServices(s => s.AddHostedService<WatchdogWorker>())
        .Build()
        .Run();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Ember watchdog failed: {ex.Message}");
    return 1; // non-zero so the service recovery settings restart it
}
