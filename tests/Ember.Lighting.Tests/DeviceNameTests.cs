using Ember.Lighting;

namespace Ember.Lighting.Tests;

public class DeviceNameTests
{
    [Fact]
    public void Chosen_names_are_kept_when_sensible_and_refused_when_blank_or_huge()
    {
        var ok = LightingState.Default with { DeviceNames = new Dictionary<string, string> { ["govee:1"] = "Desk lamp" } };
        Assert.True(LightingState.IsValid(ok));
        Assert.False(LightingState.IsValid(ok with { DeviceNames = new Dictionary<string, string> { ["govee:1"] = "  " } }));
        Assert.False(LightingState.IsValid(ok with { DeviceNames = new Dictionary<string, string> { ["govee:1"] = new string('x', 61) } }));
    }
}
