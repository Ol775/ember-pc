using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>The PC's specs, read once when the page is first opened (Refresh reads them again), with a button that copies them as text.</summary>
public sealed class SpecsPage : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _groups = new();
    readonly TextBlock _status = Text("", muted: true, size: 13);
    IReadOnlyList<SpecGroup> _specs = [];
    bool _loading, _loaded;

    public SpecsPage()
    {
        Content = _root;
        Focusable = false;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Pill("Refresh", () => _ = LoadAsync(force: true), "Read the specs again"));
        buttons.Children.Add(Pill("Copy specs", Copy, "Copy the specs as text"));
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var title = new TextBlock { Text = "Specs", FontSize = 24, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        head.Children.Add(title);
        _root.Children.Add(head);
        _root.Children.Add(Text("What this PC is made of. Copy it to paste into a support request or a problem report; it never includes the PC name or your user name.", muted: true, size: 13));
        _status.Margin = new Thickness(0, 6, 0, 0);
        _root.Children.Add(_status);
        _root.Children.Add(WithMargin(_groups, 0, 16, 0, 0));
        ThemeManager.Changed += () => { if (_loaded) Show(); };
    }

    /// <summary>Called whenever the page is opened: the first time it reads the specs, later times it keeps what it has.</summary>
    public Task LoadAsync(bool force = false)
    {
        if (_loading || (_loaded && !force)) return Task.CompletedTask;
        return ReadAsync();
    }

    async Task ReadAsync()
    {
        _loading = true;
        _status.Text = "Reading the specs...";
        try
        {
            _specs = await Task.Run(SystemSpecs.Collect);
            _loaded = true;
            _status.Text = _specs.Count == 0 ? "Windows would not tell Ember anything about this PC." : "";
            Show();
        }
        catch (Exception ex)
        {
            DiagLog.Warn("Specs", "Reading the specs failed", ex);
            _status.Text = "The specs could not be read. Try Refresh.";
        }
        finally { _loading = false; }
    }

    /// <summary>The headline of the page: the processor, graphics card, memory and Windows in one glance.</summary>
    FrameworkElement Summary()
    {
        string? Find(string group, string label) => _specs.FirstOrDefault(g => g.Title == group)?.Items.FirstOrDefault(i => i.Label == label)?.Value;
        var line = string.Join("  ·  ", new[] { Find("Graphics", "Card"), Find("Memory", "Installed") is { } m ? m + " memory" : null, Find("Windows", "Edition") }.Where(s => !string.IsNullOrEmpty(s)));
        var stack = new StackPanel();
        stack.Children.Add(Text(Find("Processor", "Name") ?? "This PC", size: 22, weight: FontWeights.SemiBold));
        stack.Children.Add(WithMargin(Text(line, muted: true, size: 14), 0, 4, 0, 0));
        return Card(stack, accent: true);
    }

    static Border Card(UIElement child, bool accent = false)
    {
        var card = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, accent ? "AccentSoftBrush" : "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return card;
    }

    void Show()
    {
        _groups.Children.Clear();
        if (_specs.Count == 0) return;
        _groups.Children.Add(Summary());
        foreach (var g in _specs)
        {
            var body = new StackPanel();
            var heading = Text(g.Title.ToUpperInvariant(), size: 11, weight: FontWeights.SemiBold);
            heading.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            heading.Margin = new Thickness(0, 0, 0, 8);
            body.Children.Add(heading);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < g.Items.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = Text(g.Items[i].Label, muted: true, size: 13);
                label.Margin = new Thickness(0, 3, 12, 3);
                // Read-only text boxes let the value be selected with the mouse.
                var value = new TextBox { Text = g.Items[i].Value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, FontSize = 13, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0, 3, 0, 3) };
                if (g.Items[i].Alert) value.Foreground = ThemeManager.Warn; else value.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
                AutomationProperties.SetName(value, g.Items[i].Label);
                Grid.SetRow(label, i);
                Grid.SetRow(value, i);
                Grid.SetColumn(value, 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }
            body.Children.Add(grid);
            _groups.Children.Add(Card(body));
        }
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }

    void Copy()
    {
        if (_specs.Count == 0) { _status.Text = "Nothing to copy yet."; return; }
        // The clipboard can be held by another program for a moment; say so instead of failing.
        try { Clipboard.SetText(SpecSheet.ToText(_specs)); _status.Text = "Copied. Paste it into a support request or a problem report."; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException) { _status.Text = "Another program is using the clipboard. Try again."; }
    }
}
