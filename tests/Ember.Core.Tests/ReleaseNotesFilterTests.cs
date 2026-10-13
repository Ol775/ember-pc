namespace Ember.Core.Tests;

public class ReleaseNotesFilterTests
{
    const string Log = "# Changelog\n\nIntro.\n\n## 1.1.0-beta.2 (2026-11-01)\n\n- beta two\n\n## 1.0.0 (2026-10-20)\n\n- first production\n\n## 0.1.0-beta.10 (2026-10-13)\n\n- old beta\n";

    [Fact]
    public void Only_releases_without_a_label_remain_and_the_intro_is_kept()
    {
        var shown = ReleaseNotesFilter.ProductionOnly(Log);
        Assert.Contains("Intro.", shown);
        Assert.Contains("first production", shown);
        Assert.DoesNotContain("beta two", shown);
        Assert.DoesNotContain("old beta", shown);
    }

    [Fact]
    public void Nothing_in_gives_nothing_out() => Assert.Equal("", ReleaseNotesFilter.ProductionOnly(null));
}
