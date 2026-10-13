using System.Net;

namespace Ember.Core.Tests;

public class UpdateCheckerTests
{
    static AppVersion V(string s) { Assert.True(AppVersion.TryParse(s, out var v)); return v; }

    [Theory]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("1.2.3", "1.10.0")]
    [InlineData("0.1.0-beta.1", "0.1.0-beta.2")]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.10")]
    [InlineData("0.1.0-beta.2", "0.1.0")]
    [InlineData("0.1.0-alpha", "0.1.0-beta")]
    public void Versions_sort_in_release_order(string older, string newer)
    {
        Assert.True(V(older).CompareTo(V(newer)) < 0);
        Assert.True(V(newer).CompareTo(V(older)) > 0);
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3-beta.1+build5", "1.2.3-beta.1")]
    public void Tags_parse(string tag, string shown) => Assert.Equal(shown, V(tag).ToString());

    [Theory]
    [InlineData("")] [InlineData("latest")] [InlineData("1")] [InlineData("1.2.3.4")] [InlineData("1.x.3")] [InlineData("1.2.3-")] [InlineData("-1.2.3")]
    public void Bad_tags_are_refused(string tag) => Assert.False(AppVersion.TryParse(tag, out _));

    const string Sample = """
    [
      {"tag_name":"v0.3.0-beta.1","prerelease":true,"draft":false,"html_url":"https://github.com/Ol775/ember-pc/releases/tag/v0.3.0-beta.1","body":"beta"},
      {"tag_name":"v0.2.0","prerelease":false,"draft":false,"html_url":"https://github.com/Ol775/ember-pc/releases/tag/v0.2.0"},
      {"tag_name":"v9.9.9","prerelease":false,"draft":true,"html_url":"https://github.com/Ol775/ember-pc/releases/tag/v9.9.9"},
      {"tag_name":"v0.1.0","prerelease":false,"draft":false,"html_url":"https://github.com/Ol775/ember-pc/releases/tag/v0.1.0"},
      {"tag_name":"junk","html_url":"https://github.com/x"},
      {"tag_name":"v7.0.0","html_url":"https://evil.example/release"},
      5
    ]
    """;

    [Fact]
    public void Drafts_junk_and_foreign_links_are_skipped() =>
        Assert.Equal(["v0.3.0-beta.1", "v0.2.0", "v0.1.0"], UpdateRules.Parse(Sample).Select(r => r.Tag));

    [Fact]
    public void Stable_ignores_prereleases()
    {
        var r = UpdateRules.Newer(UpdateRules.Parse(Sample), V("0.1.0"), UpdateChannel.Stable);
        Assert.Equal("v0.2.0", r?.Tag);
    }

    [Fact]
    public void Beta_sees_prereleases()
    {
        var r = UpdateRules.Newer(UpdateRules.Parse(Sample), V("0.1.0"), UpdateChannel.Beta);
        Assert.Equal("v0.3.0-beta.1", r?.Tag);
    }

    [Fact]
    public void Nothing_newer_gives_null() =>
        Assert.Null(UpdateRules.Newer(UpdateRules.Parse(Sample), V("0.3.0"), UpdateChannel.Beta));

    [Theory]
    [InlineData("not json")] [InlineData("{}")] [InlineData("null")] [InlineData("")]
    public void Garbage_parses_to_nothing(string body) => Assert.Empty(UpdateRules.Parse(body));

    sealed class Stub(Func<HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply());
    }

    static UpdateChecker Checker(Func<HttpResponseMessage> reply) => new(new HttpClient(new Stub(reply)), "https://example.test/releases");

    [Fact]
    public async Task Newer_release_is_reported()
    {
        var c = Checker(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sample) });
        var r = await c.CheckAsync(V("0.1.0"), UpdateChannel.Stable);
        Assert.Equal(UpdateStatus.Available, r.Status);
        Assert.Equal("v0.2.0", r.Release!.Tag);
    }

    [Fact]
    public async Task Current_version_is_up_to_date()
    {
        var c = Checker(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Sample) });
        Assert.Equal(UpdateStatus.UpToDate, (await c.CheckAsync(V("0.2.0"), UpdateChannel.Stable)).Status);
    }

    [Fact]
    public async Task Private_repository_or_no_releases_is_not_an_error()
    {
        Assert.Equal(UpdateStatus.NoReleases, (await Checker(() => new HttpResponseMessage(HttpStatusCode.NotFound)).CheckAsync(V("0.1.0"), UpdateChannel.Stable)).Status);
        Assert.Equal(UpdateStatus.NoReleases, (await Checker(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }).CheckAsync(V("0.1.0"), UpdateChannel.Stable)).Status);
    }

    [Fact]
    public async Task Server_errors_and_network_failures_report_failed_without_throwing()
    {
        Assert.Equal(UpdateStatus.Failed, (await Checker(() => new HttpResponseMessage(HttpStatusCode.Forbidden)).CheckAsync(V("0.1.0"), UpdateChannel.Stable)).Status);
        Assert.Equal(UpdateStatus.Failed, (await Checker(() => throw new HttpRequestException("down")).CheckAsync(V("0.1.0"), UpdateChannel.Stable)).Status);
    }

    [Fact]
    public async Task An_oversized_reply_is_refused()
    {
        var c = Checker(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1_200_000)) });
        Assert.Equal(UpdateStatus.Failed, (await c.CheckAsync(V("0.1.0"), UpdateChannel.Stable)).Status);
    }
}
