using System.Net;

namespace Ember.Core.Tests;

public class UpdateFeedTests
{
    const string Feed = """
    <?xml version="1.0" encoding="UTF-8"?>
    <feed xmlns="http://www.w3.org/2005/Atom">
      <entry>
        <id>tag:github.com,2008:Repository/1/v1.1.0-beta.3</id>
        <link rel="alternate" type="text/html" href="https://github.com/Ol775/ember-pc/releases/tag/v1.1.0-beta.3"/>
        <title>Ember 1.1.0-beta.3</title>
        <content type="html">&lt;h3&gt;Added&lt;/h3&gt;&lt;ul&gt;&lt;li&gt;&lt;strong&gt;A switch&lt;/strong&gt; for each integration.&lt;/li&gt;&lt;/ul&gt;</content>
      </entry>
      <entry>
        <id>tag:github.com,2008:Repository/1/v1.0.0</id>
        <link rel="alternate" type="text/html" href="https://github.com/Ol775/ember-pc/releases/tag/v1.0.0"/>
        <title>Ember 1.0.0 (production)</title>
      </entry>
      <entry>
        <link rel="alternate" href="https://evil.example/releases/tag/v9.9.9"/>
      </entry>
    </feed>
    """;

    static AppVersion V(string s) { Assert.True(AppVersion.TryParse(s, out var v)); return v; }

    sealed class Stub(Func<string, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(reply(request.RequestUri!.ToString()));
        }
    }

    [Fact]
    public void The_feed_gives_versions_installer_addresses_and_plain_notes()
    {
        var releases = UpdateRules.ParseAtom(Feed);
        Assert.Equal(["v1.1.0-beta.3", "v1.0.0"], releases.Select(r => r.Tag));   // the entry off github.com is dropped
        var beta = releases[0];
        Assert.True(beta.Prerelease);
        Assert.False(releases[1].Prerelease);
        Assert.Equal("https://github.com/Ol775/ember-pc/releases/download/v1.1.0-beta.3/Ember-Setup-1.1.0-beta.3.exe", beta.InstallerUrl);
        Assert.Equal(beta.InstallerUrl + ".sha256", beta.HashUrl);
        Assert.True(UpdateRules.IsDownloadUrl(beta.InstallerUrl!));
        Assert.Contains("- A switch for each integration.", beta.Notes);
        Assert.DoesNotContain("<", beta.Notes);
    }

    [Theory]
    [InlineData("")] [InlineData("not xml")] [InlineData("<feed/>")]
    [InlineData("<!DOCTYPE x [<!ENTITY a \"b\">]><feed xmlns=\"http://www.w3.org/2005/Atom\"/>")]
    public void A_broken_or_hostile_feed_is_no_releases(string xml) => Assert.Empty(UpdateRules.ParseAtom(xml));

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task When_the_api_is_rate_limited_the_feed_finds_the_update(HttpStatusCode limited)
    {
        var stub = new Stub(url => url.EndsWith("releases.atom") ? new(HttpStatusCode.OK) { Content = new StringContent(Feed) } : new(limited));
        var r = await new UpdateChecker(new HttpClient(stub)).CheckAsync(V("1.1.0-beta.2"), UpdateChannel.Beta);
        Assert.Equal(UpdateStatus.Available, r.Status);
        Assert.Equal("v1.1.0-beta.3", r.Release!.Tag);
        Assert.Equal(2, stub.Urls.Count);
    }

    [Fact]
    public async Task The_stable_channel_ignores_beta_releases_in_the_feed()
    {
        var stub = new Stub(url => url.EndsWith("releases.atom") ? new(HttpStatusCode.OK) { Content = new StringContent(Feed) } : new(HttpStatusCode.Forbidden));
        var r = await new UpdateChecker(new HttpClient(stub)).CheckAsync(V("1.0.0"), UpdateChannel.Stable);
        Assert.Equal(UpdateStatus.UpToDate, r.Status);
    }

    [Fact]
    public async Task When_both_say_no_the_message_explains_the_limit()
    {
        var stub = new Stub(_ => new(HttpStatusCode.Forbidden));
        var r = await new UpdateChecker(new HttpClient(stub)).CheckAsync(V("1.0.0"), UpdateChannel.Beta);
        Assert.Equal(UpdateStatus.Failed, r.Status);
        Assert.Contains("403", r.Message);
        Assert.Contains("shared, VPN", r.Message);
    }
}

public class ChannelSwitchTests
{
    static AppVersion V(string s) { Assert.True(AppVersion.TryParse(s, out var v)); return v; }

    static ReleaseInfo R(string v) => new(V(v), "v" + v, V(v).Pre is not null, "https://github.com/Ol775/ember-pc/releases/tag/v" + v, null);

    static readonly ReleaseInfo[] Releases = [R("1.1.0"), R("1.2.0-beta.1"), R("1.1.0-beta.7"), R("1.0.0")];

    [Fact]
    public void Latest_of_stable_is_the_newest_production_release_even_if_this_build_is_a_newer_beta()
    {
        Assert.Equal("v1.1.0", UpdateRules.Latest(Releases, UpdateChannel.Stable)!.Tag);
        Assert.Null(UpdateRules.Newer(Releases, V("1.2.0-beta.1"), UpdateChannel.Stable)); // the normal check would say nothing to do
    }

    [Fact]
    public void Latest_of_beta_is_the_newest_release_of_any_kind()
    {
        Assert.Equal("v1.2.0-beta.1", UpdateRules.Latest(Releases, UpdateChannel.Beta)!.Tag);
        Assert.Equal("v1.1.0", UpdateRules.Latest(Releases.Where(r => r.Tag != "v1.2.0-beta.1"), UpdateChannel.Beta)!.Tag);
    }

    [Fact]
    public async Task Asking_for_any_version_offers_an_older_release()
    {
        var json = "[{\"tag_name\":\"v1.1.0\",\"html_url\":\"https://github.com/Ol775/ember-pc/releases/tag/v1.1.0\"},{\"tag_name\":\"v1.2.0-beta.1\",\"prerelease\":true,\"html_url\":\"https://github.com/Ol775/ember-pc/releases/tag/v1.2.0-beta.1\"}]";
        var http = new HttpClient(new Fixed(json));
        var checker = new UpdateChecker(http, "https://example.test/releases");
        Assert.Equal(UpdateStatus.UpToDate, (await checker.CheckAsync(V("1.2.0-beta.1"), UpdateChannel.Stable)).Status);
        var any = await checker.CheckAsync(V("1.2.0-beta.1"), UpdateChannel.Stable, anyVersion: true);
        Assert.Equal(UpdateStatus.Available, any.Status);
        Assert.Equal("v1.1.0", any.Release!.Tag);
    }

    sealed class Fixed(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
