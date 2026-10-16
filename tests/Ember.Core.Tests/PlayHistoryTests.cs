using Ember.Core;

namespace Ember.Core.Tests;

public class PlayHistoryTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Playing_adds_time_and_remembers_the_last_day()
    {
        var h = new PlayHistory();
        h.Played("A", Now.AddDays(-3), 30);
        h.Played("A", Now.AddDays(-2), 30);
        Assert.Equal(60, h.Of("A")!.Seconds);
        Assert.Equal("Last played 2 days ago", h.Describe("A", Now));
        Assert.Equal("Not played yet", h.Describe("B", Now));
        Assert.True(h.Dirty);
    }

    [Theory]
    [InlineData(0, "Last played today")]
    [InlineData(1, "Last played yesterday")]
    [InlineData(8, "Last played last week")]
    [InlineData(21, "Last played 3 weeks ago")]
    [InlineData(40, "Last played last month")]
    [InlineData(100, "Last played 3 months ago")]
    [InlineData(500, "Last played over a year ago")]
    public void The_wording_follows_the_days(int days, string expected)
    {
        var h = new PlayHistory();
        h.Played("A", Now.AddDays(-days), 30);
        Assert.Equal(expected, h.Describe("A", Now));
    }

    [Fact]
    public void Bad_input_is_ignored_and_the_file_round_trips()
    {
        var h = new PlayHistory();
        h.Played("", Now, 30);
        h.Played("A", Now, double.NaN);
        h.Played("A", Now, -5);
        Assert.Null(h.Of("A"));

        var path = Path.Combine(Path.GetTempPath(), "ember-play-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            h.Played("Game", Now, 90);
            Assert.True(h.TrySave(path));
            var back = PlayHistory.Load(path);
            Assert.Equal(90, back.Of("game")!.Seconds);   // names are not case sensitive
            File.WriteAllText(path, "{ not json");
            Assert.Null(PlayHistory.Load(path).Of("Game"));
        }
        finally { File.Delete(path); }
    }
}
