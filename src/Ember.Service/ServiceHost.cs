using Ember.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

public static class ServiceHost
{
    public static IHostBuilder Create(string[] args, Func<ISensorSource> sourceFactory, string pipeName,
        Func<IFanController>? fanFactory = null) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(o => o.ServiceName = EmberNames.ServiceName)
            .ConfigureServices(s =>
            {
                s.AddSingleton<SnapshotHub>();
                s.AddSingleton(_ => sourceFactory());
                if (fanFactory is not null) s.AddSingleton(_ => fanFactory());
                s.AddSingleton(TimeProvider.System);
                if (OperatingSystem.IsWindows()) s.AddSingleton<IDriverProbe, PawnIoProbe>();
                s.AddHostedService<SensorPoller>();
                s.AddHostedService(p => new PipeServer(p.GetRequiredService<SnapshotHub>(), pipeName, p.GetRequiredService<ILogger<PipeServer>>()));
            });
}
