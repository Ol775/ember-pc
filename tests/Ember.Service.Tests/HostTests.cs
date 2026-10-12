using System.IO.Pipes;
using Ember.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ember.Service.Tests;

public sealed class HostTests
{
    private static IHostBuilder Quiet(IHostBuilder b) => b.ConfigureLogging(l => l.ClearProviders());

    [Fact]
    public async Task Host_starts_and_serves_a_snapshot()
    {
        var name = $"ember.test.{Guid.NewGuid():N}";
        var fake = new FakeSensorSource { OnRead = () => [new("cpu.load", "CPU", HardwareKind.Cpu, SensorKind.Load, 42)] };
        using var host = Quiet(ServiceHost.Create([], () => fake, name)).Build();
        await host.StartAsync();
        try
        {
            using var c = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
            await c.ConnectAsync(5000);
            using var cts = new CancellationTokenSource(5000);
            await using var e = SnapshotReader.ReadAsync(c, cts.Token).GetAsyncEnumerator();
            Assert.True(await e.MoveNextAsync());
            Assert.Equal(42, Assert.Single(e.Current.Readings).Value);
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void Run_returns_nonzero_when_the_sensor_source_cannot_be_created()
    {
        var builder = Quiet(ServiceHost.Create([], () => throw new InvalidOperationException("driver missing"), "ember.test.unused"));
        Assert.Equal(1, Program.Run(builder));
    }

    private sealed class FaultingService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(50, stoppingToken);
            throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void Run_returns_nonzero_when_a_hosted_service_faults()
    {
        var builder = Quiet(ServiceHost.Create([], () => new FakeSensorSource(), $"ember.test.{Guid.NewGuid():N}"))
            .ConfigureServices(s => s.AddHostedService<FaultingService>());
        try { Assert.NotEqual(0, Program.Run(builder)); }
        finally { Environment.ExitCode = 0; }
    }

    private sealed class StopsItselfService(IHostApplicationLifetime life) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(50, stoppingToken);
            life.StopApplication();
            await Task.Delay(Timeout.Infinite, stoppingToken); // ends cancelled on stop, not faulted
        }
    }

    [Fact]
    public void Run_returns_zero_on_a_normal_stop()
    {
        var builder = Quiet(ServiceHost.Create([], () => new FakeSensorSource(), $"ember.test.{Guid.NewGuid():N}"))
            .ConfigureServices(s => s.AddHostedService<StopsItselfService>());
        Assert.Equal(0, Program.Run(builder));
    }

    [Fact]
    public void Run_detects_a_faulted_service_even_with_logging_off()
    {
        var builder = ServiceHost.Create([], () => new FakeSensorSource(), $"ember.test.{Guid.NewGuid():N}")
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "None" }))
            .ConfigureServices(s => s.AddHostedService<FaultingService>());
        try { Assert.NotEqual(0, Program.Run(builder)); }
        finally { Environment.ExitCode = 0; }
    }

    [Fact]
    public void Run_returns_nonzero_when_the_pipe_name_is_squatted()
    {
        var name = $"ember.test.{Guid.NewGuid():N}";
        using var squatter = new NamedPipeServerStream(name, PipeDirection.Out, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Assert.Equal(1, Program.Run(Quiet(ServiceHost.Create([], () => new FakeSensorSource(), name))));
    }
}

public sealed class HostFanControlTests
{
    [Fact]
    public async Task Host_with_fan_controller_publishes_control_status_and_releases_on_stop()
    {
        var name = $"ember.test.{Guid.NewGuid():N}";
        var dir = Path.Combine(Path.GetTempPath(), "ember-host-" + Guid.NewGuid().ToString("N"));
        var fake = new FakeSensorSource { OnRead = () => [new("cpu", "CPU", HardwareKind.Cpu, SensorKind.Temperature, 50)] };
        var fans = new FakeFanController(new FanHeader("/lpc/nct6687d/0/control/2", "System Fan #1", true, true, false));
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(EmberSettings.Default with
        {
            Headers = new[] { new HeaderRecord("/lpc/nct6687d/0/control/2", true, "Case", "B", "1") },
        });
        using var host = ServiceHost.Create([], () => fake, name, () => fans, () => new FanControlIdentity("B", "1"), dir, () => [])
            .ConfigureLogging(l => l.ClearProviders()).Build();
        await host.StartAsync();
        try
        {
            using var c = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous);
            await c.ConnectAsync(5000);
            using var cts = new CancellationTokenSource(8000);
            await using var e = SnapshotReader.ReadAsync(c, cts.Token).GetAsyncEnumerator();
            Snapshot? last = null;
            for (var i = 0; i < 4 && await e.MoveNextAsync(); i++) last = e.Current;
            Assert.NotNull(last?.Control);
            Assert.Equal(HeaderState.Enabled, Assert.Single(last!.Control!.Headers).State);
            Assert.Contains(fans.Calls, x => x.Op == "set");
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        Assert.Contains(fans.Calls, x => x.Op == "release");
        Directory.Delete(dir, true);
    }
}
