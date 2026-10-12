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
        Func<IReadOnlyList<string>>? processNames = null, string? controlPipeName = null) =>
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
                        try { SettingsDirectory.Ensure(dir); }
                        catch (Exception ex) { p.GetRequiredService<ILogger<FanControlLoop>>().LogError(ex, "Could not prepare {Dir}; fan control will run with default settings", dir); }
                        var hub = p.GetRequiredService<SnapshotHub>();
                        var fans = p.GetRequiredService<IFanController>();
                        var loop = new FanControlLoop(hub, fans,
                            new SettingsStore(Path.Combine(dir, "settings.json")), processNames ?? ProcessNames, p.GetRequiredService<TimeProvider>(),
                            p.GetRequiredService<ILogger<FanControlLoop>>(), identity?.Invoke() ?? new FanControlIdentity("unknown", "unknown"), BuildFeatures.Current);
                        loop.Runner = new HeaderTestRunner(fans, id => Reading(hub, id.Replace("/control/", "/fan/", StringComparison.Ordinal)),
                            id => Reading(hub, id), Task.Delay,
                            () => loop.TestMustStop);
                        return loop;
                    });
                    s.AddHostedService(p => new SensorPoller(p.GetRequiredService<ISensorSource>(), p.GetRequiredService<SnapshotHub>(),
                        p.GetRequiredService<TimeProvider>(), p.GetRequiredService<ILogger<SensorPoller>>(),
                        p.GetService<IDriverProbe>(), () => p.GetRequiredService<FanControlLoop>().Status));
                    s.AddHostedService(p => p.GetRequiredService<FanControlLoop>());
                    s.AddHostedService(p => new ControlPipeServer(p.GetRequiredService<FanControlLoop>().Handle,
                        controlPipeName ?? EmberNames.ControlPipeName, p.GetRequiredService<ILogger<ControlPipeServer>>()));
                }
                s.AddHostedService(p => new PipeServer(p.GetRequiredService<SnapshotHub>(), pipeName, p.GetRequiredService<ILogger<PipeServer>>()));
            });

    static double? Reading(SnapshotHub hub, string id) =>
        hub.Latest?.Readings.FirstOrDefault(r => r.Id == id) is { } r ? r.Value : null;

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
