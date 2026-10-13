namespace Ember.Core.Tests;

public class MarkdownLiteTests
{
    [Fact]
    public void Headings_bullets_and_paragraphs_are_read_and_the_title_is_skipped()
    {
        var lines = MarkdownLite.Parse("# Changelog\n\nSome intro\nacross two lines.\n\n## 0.1.0 (2026-10-13)\n\n### Added\n\n- **Fans:** curves\n- Lights\n");
        Assert.Equal(
            [(MdKind.Paragraph, "Some intro across two lines."), (MdKind.Heading, "0.1.0 (2026-10-13)"), (MdKind.Subheading, "Added"),
             (MdKind.Bullet, "Fans: curves"), (MdKind.Bullet, "Lights")],
            lines.Select(l => (l.Kind, l.Text)));
    }

    [Fact]
    public void Links_keep_their_text_and_lose_the_address() =>
        Assert.Equal("See the roadmap for more.", MarkdownLite.Parse("See the [roadmap](ROADMAP.md) for more.")[0].Text);

    [Fact]
    public void Windows_line_endings_work() => Assert.Equal(2, MarkdownLite.Parse("## A\r\n- b\r\n").Count);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   \n  ")]
    public void Nothing_in_gives_nothing_out(string? text) => Assert.Empty(MarkdownLite.Parse(text));

    [Fact]
    public void An_unclosed_link_is_left_as_text() => Assert.Equal("a [b(c", MarkdownLite.Parse("a [b(c")[0].Text);
}
