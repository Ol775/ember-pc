namespace Ember.Core.Tests;

public class LegalTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-legal-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(Legal.TermsVersion, 0, true)]
    [InlineData(0, Legal.TermsVersion, true)]
    [InlineData(Legal.TermsVersion + 1, 0, true)]
    [InlineData(-5, -5, false)]
    public void Terms_count_as_accepted_when_either_record_covers_the_current_version(int inApp, int byInstaller, bool expected) =>
        Assert.Equal(expected, Legal.IsAccepted(inApp, byInstaller));

    [Fact]
    public void The_accepted_version_is_remembered_between_runs()
    {
        var store = new PreferencesStore(Path.Combine(_dir, "app.json"));
        Assert.Equal(0, store.Load().AcceptedTerms);
        Assert.True(store.TrySave(AppPreferences.Default with { AcceptedTerms = Legal.TermsVersion }));
        Assert.Equal(Legal.TermsVersion, store.Load().AcceptedTerms);
    }

    [Theory]
    [InlineData("{\"AcceptedTerms\":-9}", 0)]
    [InlineData("{\"AcceptedTerms\":99999999}", 1000)]
    [InlineData("{\"AcceptedTerms\":\"yes\"}", 0)]
    public void A_damaged_value_cannot_make_the_terms_look_accepted(string json, int expected)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "x.json");
        File.WriteAllText(path, json);
        Assert.Equal(expected, new PreferencesStore(path).Load().AcceptedTerms);
    }

    [Fact]
    public void The_terms_document_exists_and_the_installer_uses_the_same_version()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TERMS.md"))) root = root.Parent;
        Assert.NotNull(root);
        Assert.Contains($"Version {Legal.TermsVersion},", File.ReadAllText(Path.Combine(root!.FullName, "TERMS.md")));
        Assert.Contains($"TermsVersion = {Legal.TermsVersion};", File.ReadAllText(Path.Combine(root.FullName, "src", "Ember.Setup", "Installer.cs")));
    }
}
