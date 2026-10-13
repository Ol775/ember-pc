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

    /// <summary>
    /// Like <see cref="Fill"/>, but each "##" heading (a release, a roadmap group) is a row that folds its notes away, with a one-line
    /// summary on the right. By default the first stays open and the rest start closed; <paramref name="isOpen"/> decides otherwise.
    /// A long bullet shows two lines until "Show more" is pressed. Returns a function that opens or closes every section.
    /// </summary>
    public static Action<bool> FillFolding(Panel host, string? markdown, Func<int, string, bool>? isOpen = null, Func<List<MdLine>, string>? summarise = null)
    {
        host.Children.Clear();
        var lines = MarkdownLite.Parse(markdown);
        if (lines.Count == 0) { host.Children.Add(Block("Nothing to show.", muted: true)); return _ => { }; }

        var sections = new List<(string? Title, List<MdLine> Body)>();
        foreach (var line in lines)
        {
            if (line.Kind == MdKind.Heading || sections.Count == 0) sections.Add((line.Kind == MdKind.Heading ? line.Text : null, []));
            if (line.Kind != MdKind.Heading) sections[^1].Body.Add(line);
        }

        var setters = new List<Action<bool>>();
        var index = 0;
        foreach (var (title, body) in sections)
        {
            var panel = new StackPanel { Margin = new Thickness(18, 4, 0, 10) };
            AddBody(panel, body);
            if (title is null) { panel.Margin = new Thickness(0, 0, 0, 6); host.Children.Add(panel); continue; }

            var open = isOpen is null ? index == 0 : isOpen(index, title);
            index++;
            var chevron = Block(open ? "▾" : "▸", muted: true);
            chevron.Width = 18;
            var head = Block(title, size: 16, weight: FontWeights.SemiBold);
            var summary = Block((summarise ?? Summarise)(body), muted: true, size: 12);
            summary.VerticalAlignment = VerticalAlignment.Center;
            summary.TextAlignment = TextAlignment.Right;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(head, 1);
            Grid.SetColumn(summary, 2);
            row.Children.Add(chevron);
            row.Children.Add(head);
            row.Children.Add(summary);
            var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) };
            if (Application.Current.TryFindResource("FoldButton") is Style style) button.Style = style;
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            void Set(bool show)
            {
                panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = show ? "▾" : "▸";
            }
            setters.Add(Set);
            button.Click += (_, _) => Set(panel.Visibility != Visibility.Visible);
            host.Children.Add(button);
            host.Children.Add(panel);
        }
        return show => { foreach (var set in setters) set(show); };
    }

    // "3 added, 1 changed" from the sub-headings and bullets under a release.
    public static string Summarise(List<MdLine> body)
    {
        var parts = new List<string>();
        string? group = null;
        var count = 0;
        void Close() { if (group is not null && count > 0) parts.Add($"{count} {group.ToLowerInvariant()}"); count = 0; }
        foreach (var l in body)
        {
            if (l.Kind == MdKind.Subheading) { Close(); group = l.Text; }
            else if (l.Kind == MdKind.Bullet) { group ??= "changes"; count++; }
        }
        Close();
        return parts.Count == 0 ? "" : string.Join(", ", parts);
    }

    /// <summary>"12 items" for a section made of bullets (the roadmap); nothing for a section without any.</summary>
    public static string CountItems(List<MdLine> body)
    {
        var n = body.Count(l => l.Kind == MdKind.Bullet);
        return n == 0 ? "" : n == 1 ? "1 item" : $"{n} items";
    }

    const int ClampAbove = 200;

    static void AddBody(Panel panel, List<MdLine> body)
    {
        foreach (var line in body)
        {
            switch (line.Kind)
            {
                case MdKind.Subheading:
                    var sh = Block(line.Text, muted: true, size: 13, weight: FontWeights.SemiBold);
                    sh.Margin = new Thickness(0, 10, 0, 2);
                    panel.Children.Add(sh);
                    break;
                case MdKind.Bullet:
                    var dot = Block("•", muted: true);
                    dot.Margin = new Thickness(2, 0, 8, 0);
                    dot.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(dot, Dock.Left);
                    var text = Block(line.Text);
                    var holder = new StackPanel();
                    holder.Children.Add(text);
                    if (line.Text.Length > ClampAbove)
                    {
                        text.LineHeight = 20;
                        text.MaxHeight = 40;
                        text.TextTrimming = TextTrimming.CharacterEllipsis;
                        var more = Block("Show more", muted: true, size: 12);
                        more.Cursor = System.Windows.Input.Cursors.Hand;
                        more.Margin = new Thickness(0, 0, 0, 2);
                        more.TextDecorations = TextDecorations.Underline;
                        more.MouseLeftButtonUp += (_, _) =>
                        {
                            var expand = text.MaxHeight < double.PositiveInfinity;
                            text.MaxHeight = expand ? double.PositiveInfinity : 40;
                            more.Text = expand ? "Show less" : "Show more";
                        };
                        holder.Children.Add(more);
                    }
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    row.Children.Add(dot);
                    row.Children.Add(holder);
                    panel.Children.Add(row);
                    break;
                default:
                    var p = Block(line.Text, muted: true);
                    p.Margin = new Thickness(0, 2, 0, 6);
                    panel.Children.Add(p);
                    break;
            }
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
