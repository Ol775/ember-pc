using System.Net;

namespace Ember.Core.Tests;

public class UpdateInstallerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-upd-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    const string Base = "https://github.com/Ol775/ember-pc/releases/download/v0.2.0/";
    static readonly byte[] Payload = [1, 2, 3, 4, 5];
    static readonly string Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Payload));

    sealed class Fake(Func<string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) => Task.FromResult(answer(r.RequestUri!.AbsoluteUri));
    }

    static ReleaseInfo Release(string? installer = Base + "Ember-Setup-0.2.0.exe", string? hash = Base + "Ember-Setup-0.2.0.exe.sha256") =>
        new(new AppVersion(0, 2, 0, null), "v0.2.0", false, "https://github.com/Ol775/ember-pc/releases/tag/v0.2.0", null, installer, hash);

    static UpdateInstaller With(string hashText) => new(new HttpClient(new Fake(u => u.EndsWith(".sha256")
        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hashText) }
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) })));

    [Fact]
    public async Task A_download_that_matches_its_checksum_is_kept()
    {
        var r = await With(Hash + "  Ember-Setup-0.2.0.exe\n").DownloadAsync(Release(), _dir);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(Payload, File.ReadAllBytes(r.Path!));
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_thrown_away()
    {
        var r = await With(new string('A', 64)).DownloadAsync(Release(), _dir);
        Assert.False(r.Ok);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_release_without_a_checksum_or_from_another_site_is_refused()
    {
        Assert.False((await With(Hash).DownloadAsync(Release(hash: null), _dir)).Ok);
        Assert.False((await With(Hash).DownloadAsync(Release(installer: "https://evil.example/Ember-Setup-0.2.0.exe"), _dir)).Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("zzzz")]
    public void Unreadable_checksums_are_refused(string text) => Assert.Null(UpdateInstaller.ParseHash(text));

    [Fact]
    public void The_checker_finds_the_installer_and_checksum_among_the_assets()
    {
        var json = """
        [{"tag_name":"v0.2.0","html_url":"https://github.com/Ol775/ember-pc/releases/tag/v0.2.0","assets":[
          {"name":"Ember-0.2.0.zip","browser_download_url":"https://github.com/Ol775/ember-pc/releases/download/v0.2.0/Ember-0.2.0.zip"},
          {"name":"Ember-Setup-0.2.0.exe","browser_download_url":"https://github.com/Ol775/ember-pc/releases/download/v0.2.0/Ember-Setup-0.2.0.exe"},
          {"name":"Ember-Setup-0.2.0.exe.sha256","browser_download_url":"https://github.com/Ol775/ember-pc/releases/download/v0.2.0/Ember-Setup-0.2.0.exe.sha256"}]}]
        """;
        var r = Assert.Single(UpdateRules.Parse(json));
        Assert.EndsWith("Ember-Setup-0.2.0.exe", r.InstallerUrl);
        Assert.EndsWith(".exe.sha256", r.HashUrl);
    }
}
