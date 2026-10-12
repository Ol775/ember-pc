using Ember.Core;

namespace Ember.Service.Tests;

internal sealed class FakeSensorSource : ISensorSource
{
    public Func<IReadOnlyList<SensorReading>> OnRead { get; set; } = () => [];

    public IReadOnlyList<SensorReading> Read() => OnRead();

    public void Dispose() { }
}
