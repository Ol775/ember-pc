namespace Ember.Core.Tests;

public class BuildFeaturesTests
{
    [Theory]
    [InlineData("1.0.0", "prod")]
    [InlineData("1.0.0+abc123", "prod")]
    [InlineData("1.1.0-beta.1", "beta")]
    [InlineData("0.1.0-beta.10+903c813", "beta")]
    [InlineData(null, "prod")]
    public void A_version_label_makes_a_beta_build(string? version, string channel) => Assert.Equal(channel, BuildFeatures.ChannelOf(version));

    [Fact]
    public void This_build_has_the_features_of_its_own_channel()
    {
        Assert.Equal(BuildFeatures.IsProduction ? BuildFeatures.Production : BuildFeatures.All, BuildFeatures.Current);
    }

    [Fact]
    public void Production_has_none_of_the_beta_features_and_beta_has_all()
    {
        Assert.True(BuildFeatures.Production.GameDetection);   // marked Beta, behind the Experimental switch
        Assert.False(BuildFeatures.Production.GpuFan);
        Assert.True(BuildFeatures.All.GameDetection && BuildFeatures.All.GpuFan);
    }
}
