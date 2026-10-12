namespace Ember.Core.Tests;

public class CompetitorListTests
{
    [Fact]
    public void Monitoring_only_tools_are_not_competitors() =>
        Assert.Empty(CompetitorList.Default.Match(new[] { "HWiNFO64", "OpenRGB" }));

    [Fact]
    public void Matches_case_insensitively() =>
        Assert.Equal(new[] { "Fan Control" }, CompetitorList.Default.Match(new[] { "fancontrol" }));

    [Fact]
    public void Ignores_exe_suffix() =>
        Assert.Equal(new[] { "iCUE" }, CompetitorList.Default.Match(new[] { "iCUE.exe" }));

    [Fact]
    public void Returns_display_name_once() =>
        Assert.Equal(new[] { "MSI Center" }, CompetitorList.Default.Match(new[] { "MSI Center", "MSI_Center", "msi_center.exe" }));

    [Fact]
    public void Unknown_process_gives_empty() =>
        Assert.Empty(CompetitorList.Default.Match(new[] { "notepad", "explorer.exe" }));

    [Fact]
    public void Default_list_contains_FanControl_and_iCUE() =>
        Assert.Equal(new[] { "Fan Control", "iCUE" }, CompetitorList.Default.Match(new[] { "FanControl", "iCUE" }));

    [Fact]
    public void Custom_list_works() =>
        Assert.Equal(new[] { "X" }, new CompetitorList(new Dictionary<string, string[]> { ["X"] = new[] { "x.exe" } }).Match(new[] { "X" }));
}
