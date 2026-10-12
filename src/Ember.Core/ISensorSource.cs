namespace Ember.Core;

public interface ISensorSource : IDisposable
{
    IReadOnlyList<SensorReading> Read();
}
