namespace Ember.Core;

public enum MdKind { Heading, Subheading, Bullet, Paragraph }

public sealed record MdLine(MdKind Kind, string Text);

/// <summary>
/// Reads the small part of Markdown that CHANGELOG.md and ROADMAP.md use (headings, bullets, paragraphs) so the app can
/// show them. Anything else is shown as plain text; nothing is ever executed or fetched.
/// </summary>
public static class MarkdownLite
{
    public static IReadOnlyList<MdLine> Parse(string? text)
    {
        var lines = new List<MdLine>();
        if (string.IsNullOrWhiteSpace(text)) return lines;
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count > 0) lines.Add(new MdLine(MdKind.Paragraph, Clean(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) { Flush(); continue; }
            if (line.StartsWith("### ", StringComparison.Ordinal)) { Flush(); lines.Add(new MdLine(MdKind.Subheading, Clean(line[4..]))); }
            else if (line.StartsWith("## ", StringComparison.Ordinal)) { Flush(); lines.Add(new MdLine(MdKind.Heading, Clean(line[3..]))); }
            else if (line.StartsWith("# ", StringComparison.Ordinal)) { Flush(); } // the document title: the page has its own
            else if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) { Flush(); lines.Add(new MdLine(MdKind.Bullet, Clean(line[2..]))); }
            else paragraph.Add(line.Trim());
        }
        Flush();
        return lines;
    }

    /// <summary>Drops the emphasis and code markers and turns [text](link) into text.</summary>
    static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '[')
            {
                var close = s.IndexOf("](", i, StringComparison.Ordinal);
                var end = close < 0 ? -1 : s.IndexOf(')', close);
                if (close > 0 && end > 0) { sb.Append(s, i + 1, close - i - 1); i = end; continue; }
            }
            if (c is '*' or '`') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
