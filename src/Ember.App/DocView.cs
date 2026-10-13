using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ember.Core;

namespace Ember.App;

/// <summary>Shows an embedded Markdown document (the changelog, the roadmap) as headings, bullets and text.</summary>
public static class DocView
{
    public static string? ReadEmbedded(string name)
    {
        using var s = typeof(DocView).Assembly.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static void Fill(Panel host, string? markdown)
    {
        host.Children.Clear();
        var lines = MarkdownLite.Parse(markdown);
        if (lines.Count == 0) { host.Children.Add(Block("Nothing to show.", muted: true)); return; }
        var first = true;
        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case MdKind.Heading:
                    var h = Block(line.Text, size: 17, weight: FontWeights.SemiBold);
                    h.Margin = new Thickness(0, first ? 0 : 20, 0, 4);
                    host.Children.Add(h);
                    break;
                case MdKind.Subheading:
                    var sh = Block(line.Text, muted: true, size: 13, weight: FontWeights.SemiBold);
                    sh.Margin = new Thickness(0, 10, 0, 2);
                    host.Children.Add(sh);
                    break;
                case MdKind.Bullet:
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var dot = Block("\u2022", muted: true);
                    dot.Margin = new Thickness(2, 0, 8, 0);
                    dot.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(Block(line.Text));
                    host.Children.Add(row);
                    break;
                default:
                    var p = Block(line.Text, muted: true);
                    p.Margin = new Thickness(0, 2, 0, 6);
                    host.Children.Add(p);
                    break;
            }
            first = false;
        }
    }

    static TextBlock Block(string text, bool muted = false, double size = 14, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        if (weight is { } w) t.FontWeight = w;
        return t;
    }
}
