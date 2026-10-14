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
