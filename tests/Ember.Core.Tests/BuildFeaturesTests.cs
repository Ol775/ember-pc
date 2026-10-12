namespace Ember.Core.Tests;

public class BuildFeaturesTests
{
    [Fact]
    public void Alpha_build_has_no_beta_features()
    {
        Assert.Equal("alpha", BuildFeatures.Channel);
        Assert.False(BuildFeatures.Current.GameDetection);
        Assert.False(BuildFeatures.Current.GpuFan);
    }

    [Fact]
    public void All_has_both() => Assert.True(BuildFeatures.All.GameDetection && BuildFeatures.All.GpuFan);
}
