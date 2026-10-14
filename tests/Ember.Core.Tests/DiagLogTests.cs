using Ember.Core;

namespace Ember.Core.Tests;

[Collection("DiagLog")]
public class DiagLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-log-" + Guid.NewGuid().ToString("N"));

    public DiagLogTests() => DiagLog.Use(_dir);
    public void Dispose() { DiagLog.Use(DiagLog.DefaultFolder); if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    string Text() => string.Join("\n", Directory.GetFiles(_dir, "ember-*.log").Select(File.ReadAllText));

    [Fact]
    public void Lines_go_to_a_daily_file_with_level_area_and_error()
    {
        DiagLog.Info("Lighting", "Corsair: connected");
        DiagLog.Warn("Lighting", "refresh failed", new IOException("pipe closed"));
        var text = Text();
        Assert.Contains("INFO  Lighting: Corsair: connected", text);
        Assert.Contains("WARN  Lighting: refresh failed | IOException: pipe closed", text);
        Assert.Matches(@"ember-\d{8}\.log$", Directory.GetFiles(_dir).Single());
    }

    [Fact]
    public void A_value_that_has_not_changed_is_logged_once()
    {
        for (var i = 0; i < 20; i++) DiagLog.Changed("Lighting", "Logitech", "not answering");
        DiagLog.Changed("Lighting", "Logitech", "connected");
        DiagLog.Changed("Lighting", "Logitech", "not answering");
        Assert.Equal(3, Text().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Theory]
    [InlineData("apiKey=abcd1234secret", "abcd1234secret")]
    [InlineData("Authorization token: sometokenvalue", "sometokenvalue")]
    [InlineData("password = hunter2", "hunter2")]
    [InlineData("bridge key 0123456789abcdef0123456789abcdef0123", "0123456789abcdef0123456789abcdef0123")]
    public void Secrets_are_blanked_before_they_are_written(string line, string secret)
    {
        DiagLog.Info("Test", line);
        Assert.DoesNotContain(secret, Text());
        Assert.DoesNotContain(secret, string.Join("\n", DiagLog.Tail()));
    }

    [Fact]
    public void Ordinary_status_text_is_left_alone()
    {
        DiagLog.Info("Lighting", "Logitech: Connected through G HUB, keyboard lit key by key");
        Assert.Contains("keyboard lit key by key", Text());
    }

    [Fact]
    public void Tail_returns_the_newest_lines_last()
    {
        for (var i = 0; i < 5; i++) DiagLog.Info("T", "line " + i);
        var tail = DiagLog.Tail(2);
        Assert.Equal(2, tail.Count);
        Assert.EndsWith("line 4", tail[1]);
    }

    [Fact]
    public void Old_files_are_pruned_and_new_ones_kept()
    {
        Directory.CreateDirectory(_dir);
        var old = Path.Combine(_dir, "ember-20200101.log");
        var fresh = Path.Combine(_dir, "ember-20990101.log");
        File.WriteAllText(old, "x"); File.WriteAllText(fresh, "x");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
        DiagLog.Prune();
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void An_unwritable_folder_never_throws()
    {
        DiagLog.Use(Path.Combine(_dir, "a\0b"));
        var ex = Record.Exception(() => DiagLog.Error("Test", "still fine"));
        Assert.Null(ex);
    }
}

[CollectionDefinition("DiagLog", DisableParallelization = true)]
public class DiagLogCollection;
