namespace Ember.Core.Tests;

public class LaunchOptionsTests
{
    [Theory]
    [InlineData("--minimised")]
    [InlineData("--MINIMISED")]
    [InlineData("--minimized")]
    public void Minimised_switch_is_found(string arg) => Assert.True(LaunchOptions.Parse(["x", arg]).Minimised);

    [Fact]
    public void No_arguments_means_a_normal_start() => Assert.False(LaunchOptions.Parse([]).Minimised);

    [Fact]
    public void Unknown_arguments_are_ignored() => Assert.False(LaunchOptions.Parse(["--what", "minimised"]).Minimised);

    [Fact]
    public void Startup_command_quotes_the_path_and_adds_the_switch_only_when_asked()
    {
        Assert.Equal(@"""C:\Program Files\Ember\Ember.App.exe""", LaunchOptions.StartupCommand(@"C:\Program Files\Ember\Ember.App.exe", false));
        Assert.Equal(@"""C:\Program Files\Ember\Ember.App.exe"" --minimised", LaunchOptions.StartupCommand(@"C:\Program Files\Ember\Ember.App.exe", true));
    }

    [Fact]
    public void Startup_command_round_trips_through_parse() =>
        Assert.True(LaunchOptions.Parse(LaunchOptions.StartupCommand("a.exe", true).Split(' ')).Minimised);
}
