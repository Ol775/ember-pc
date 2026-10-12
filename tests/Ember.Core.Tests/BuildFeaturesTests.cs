namespace Ember.Core.Tests;

public class BuildFeaturesTests
{
    [Fact]
    public void Beta_build_has_the_beta_features()
    {
        Assert.Equal("beta", BuildFeatures.Channel);
        Assert.True(BuildFeatures.Current.GameDetection);
        Assert.True(BuildFeatures.Current.GpuFan);
    }

    [Fact]
    public void Alpha_set_has_none_and_all_has_both()
    {
        Assert.False(BuildFeatures.Alpha.GameDetection || BuildFeatures.Alpha.GpuFan);
        Assert.True(BuildFeatures.All.GameDetection && BuildFeatures.All.GpuFan);
    }
}
