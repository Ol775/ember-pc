using System.Diagnostics;
using Ember.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service;

public static class ServiceHost
{
    public static IHostBuilder Create(string[] args, Func<ISensorSource> sourceFactory, string pipeName,
        Func<IFanController>? fanFactory = null, Func<FanControlIdentity>? identity = null, string? settingsDir = null,
        Func<IReadOnlyList<string>>? processNames = null) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(o => o.ServiceName = EmberNames.ServiceName)
            .ConfigureServices(s =>
            {
                s.AddSingleton<SnapshotHub>();
                s.AddSingleton(_ => sourceFactory());
                s.AddSingleton(TimeProvider.System);
                if (OperatingSystem.IsWindows()) s.AddSingleton<IDriverProbe, PawnIoProbe>();
                if (fanFactory is null)
                {
                    s.AddHostedService<SensorPoller>();
                }
                else
                {
                    s.AddSingleton(_ => fanFactory());
                    s.AddSingleton(p =>
                    {
                        var dir = settingsDir ?? Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ember");
                        SettingsDirectory.Ensure(dir);
                        return new FanControlLoop(p.GetRequiredService<SnapshotHub>(), p.GetRequiredService<IFanController>(),
                            new SettingsStore(Path.Combine(dir, "settings.json")), processNames ?? ProcessNames, p.GetRequiredService<TimeProvider>(),
                            p.GetRequiredService<ILogger<FanControlLoop>>(), identity?.Invoke() ?? new FanControlIdentity("unknown", "unknown"));
                    });
                    s.AddHostedService(p => new SensorPoller(p.GetRequiredService<ISensorSource>(), p.GetRequiredService<SnapshotHub>(),
                        p.GetRequiredService<TimeProvider>(), p.GetRequiredService<ILogger<SensorPoller>>(),
                        p.GetService<IDriverProbe>(), () => p.GetRequiredService<FanControlLoop>().Status));
                    s.AddHostedService(p => p.GetRequiredService<FanControlLoop>());
                }
                s.AddHostedService(p => new PipeServer(p.GetRequiredService<SnapshotHub>(), pipeName, p.GetRequiredService<ILogger<PipeServer>>()));
            });

    static IReadOnlyList<string> ProcessNames()
    {
        var names = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try { names.Add(p.ProcessName); }
            catch (Exception) { /* access denied or exited */ }
            finally { p.Dispose(); }
        }
        return names;
    }
}
