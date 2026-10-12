namespace Ember.Core.Tests;

public class ReleaseRulesTests
{
    [Fact] public void System_fan_goes_back_to_its_saved_speed() => Assert.Equal(60, ReleaseRules.ValueFor("System Fan #1", 60));
    [Fact] public void System_fan_saved_speed_respects_the_floor() => Assert.Equal(20, ReleaseRules.ValueFor("System Fan #1", 5));
    [Fact] public void CPU_fan_is_released_at_100_whatever_was_saved() => Assert.Equal(100, ReleaseRules.ValueFor("CPU Fan", 66));
    [Fact] public void Unknown_saved_speed_means_100() => Assert.Equal(100, ReleaseRules.ValueFor("System Fan #1", null));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_saved_speed_means_100(double p) => Assert.Equal(100, ReleaseRules.ValueFor("System Fan #1", p));
}
