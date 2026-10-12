namespace Ember.Core.Tests;

public class ScaffoldTests
{
    [Fact]
    public void Core_assembly_loads()
    {
        Assert.Equal("Ember.Core", typeof(Ember.Core.AssemblyMarker).Assembly.GetName().Name);
    }
}
