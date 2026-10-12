using System.IO.Pipes;
using Ember.Core;
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
            using var stop = new CancellationTokenSource(3000);
            await host.StopAsync(stop.Token);
        }
    }

    [Fact]
    public void Run_returns_nonzero_when_the_sensor_source_cannot_be_created()
    {
        var builder = Quiet(ServiceHost.Create([], () => throw new InvalidOperationException("driver missing"), "ember.test.unused"));
        Assert.Equal(1, Program.Run(builder));
    }
}
