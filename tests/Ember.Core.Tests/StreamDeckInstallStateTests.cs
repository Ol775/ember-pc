namespace Ember.Core.Tests;

public class StreamDeckInstallStateTests
{
    [Fact]
    public void Not_found_when_the_stream_deck_app_is_missing_even_if_a_plugin_folder_is_left() =>
        Assert.Equal(StreamDeckState.NotFound, StreamDeckInstallState.Evaluate(false, "1.2.0.1", "1.2.0.1"));

    [Fact]
    public void Not_installed_when_there_is_no_installed_version() =>
        Assert.Equal(StreamDeckState.NotInstalled, StreamDeckInstallState.Evaluate(true, null, "1.2.0.1"));

    [Fact]
    public void Installed_when_the_versions_are_equal() =>
        Assert.Equal(StreamDeckState.Installed, StreamDeckInstallState.Evaluate(true, "1.2.0.1", "1.2.0.1"));

    [Theory]
    [InlineData("1.2.0.0", "1.2.0.1")]
    [InlineData("1.1.0.0", "1.2.0.1")]
    [InlineData("1.2.0.9", "1.2.1.0")]
    public void Needs_update_when_older(string installed, string bundled) =>
        Assert.Equal(StreamDeckState.NeedsUpdate, StreamDeckInstallState.Evaluate(true, installed, bundled));

    [Fact]
    public void A_newer_installed_plugin_counts_as_installed() =>
        Assert.Equal(StreamDeckState.Installed, StreamDeckInstallState.Evaluate(true, "1.3.0.0", "1.2.0.1"));

    [Theory]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData("1.2")]
    public void An_unreadable_installed_version_needs_an_update(string installed) =>
        Assert.Equal(StreamDeckState.NeedsUpdate, StreamDeckInstallState.Evaluate(true, installed, "1.2.0.1"));

    [Fact]
    public void Reads_the_version_from_a_manifest() =>
        Assert.Equal("1.2.0.1", StreamDeckInstallState.ReadManifestVersion("{\"Name\":\"x\",\"Version\":\"1.2.0.1\"}"));

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"Version\":5}")]
    [InlineData("{\"Name\":\"x\"}")]
    public void Reading_a_broken_manifest_gives_null_and_never_throws(string json) =>
        Assert.Null(StreamDeckInstallState.ReadManifestVersion(json));
}
