using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>The PC's specs, read once when the page is first opened (Refresh reads them again), with a button that copies them as text.</summary>
public sealed class SpecsPage : UserControl
{
    static readonly FontFamily Mono = Fonts.Mono;

    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _content = new();
    readonly TextBlock _status = Text("", muted: true, size: 13);
    SpecReport _report = SpecReport.Empty;
    int _filter;   // 0 all, 1 internal, 2 external
    bool _loading, _loaded;

    /// <summary>Raised on the window thread whenever the specs have been read (the dashboard's drive tile uses it).</summary>
    public event Action<SpecReport>? ReportReady;

    public SpecsPage()
    {
        Content = _root;
        Focusable = false;
        if (BuildFeatures.NewLook) _root.Margin = new Thickness(32, 30, 32, 28);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Pill("Refresh", () => _ = LoadAsync(force: true), "Read the specs again"));
        var copy = Pill("Copy specs", Copy, "Copy the specs as text");
        copy.Style = Sty("PrimaryButton");
        copy.Margin = new Thickness(0, 0, 0, 6);
        buttons.Children.Add(copy);
        if (!BuildFeatures.NewLook)
        {
            DockPanel.SetDock(buttons, Dock.Right);
            head.Children.Add(buttons);
        }
        if (BuildFeatures.NewLook)
        {
            buttons.VerticalAlignment = VerticalAlignment.Top;
            _root.Children.Add(Ui.Heading("Specs", "What this PC is made of. Copy it into a support request; it never includes the PC name or your user name.", buttons));
            head = null!;
        }
        else
        {
            var title = new TextBlock { Text = "Specs", FontSize = 24, FontWeight = FontWeights.SemiBold };
            AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
            head.Children.Add(title);
            _root.Children.Add(head);
            _root.Children.Add(Text("What this PC is made of. Copy it into a support request; it never includes the PC name or your user name.", muted: true, size: 13));
        }
        _status.Margin = new Thickness(0, 6, 0, 0);
        _root.Children.Add(_status);
        _root.Children.Add(WithMargin(_content, 0, BuildFeatures.NewLook ? 0 : 14, 0, 0));
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
            _report = await Task.Run(SystemSpecs.Collect);
            _loaded = true;
            _status.Text = _report.Groups.Count == 0 ? "Windows would not tell Ember anything about this PC." : "";
            Show();
            ReportReady?.Invoke(_report);
        }
        catch (Exception ex)
        {
            DiagLog.Warn("Specs", "Reading the specs failed", ex);
            _status.Text = "The specs could not be read. Try Refresh.";
        }
        finally { _loading = false; }
    }

    // ---- drawing ----

    static Border Card(UIElement child, bool accent = false)
    {
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 16, 20, 16), BorderThickness = new Thickness(1), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, accent ? "AccentSoftBrush" : "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, accent ? "AccentBrush" : "PanelBorderBrush");
        return card;
    }

    static TextBlock Heading(string text)
    {
        var h = Text(text.ToUpperInvariant(), size: 11.5, weight: FontWeights.SemiBold);
        h.SetResourceReference(TextBlock.ForegroundProperty, BuildFeatures.NewLook ? "AccentTextBrush" : "MutedBrush");
        h.Margin = new Thickness(0, 0, 0, 8);
        return h;
    }

    void Show()
    {
        _content.Children.Clear();
        if (_report.Groups.Count == 0) return;
        _content.Children.Add(Summary());

        var cards = new UniformGrid { Columns = 3, Margin = new Thickness(-6, 12, -6, 6) };
        cards.Children.Add(KeyValues("Processor and memory", [
            ("Name", _report.Value("Processor", "Name")),
            ("Threads", Threads()),
            ("Memory", _report.Value("Memory", "Installed"))]));
        cards.Children.Add(KeyValues("Graphics", [
            ("Card", _report.Value("Graphics", "Card")),
            ("Video memory", _report.Value("Graphics", "Video memory")),
            ("Driver", _report.Value("Graphics", "Driver")),
            ("Displays", Displays())]));
        cards.Children.Add(KeyValues("Board and Windows", [
            ("Board", Join(_report.Value("Motherboard", "Maker"), _report.Value("Motherboard", "Model"))),
            ("BIOS", _report.Value("Motherboard", "BIOS")),
            ("Windows", Join(_report.Value("Windows", "Edition"), _report.Value("Windows", "Version"))),
            ("Build", _report.Value("Windows", "Build"))]));
        _content.Children.Add(cards);

        if (_report.Drives.Count > 0) _content.Children.Add(WithMargin(DrivesCard(), 0, 6, 0, 0));
    }

    string? Threads()
    {
        var threads = _report.Value("Processor", "Threads");
        var speed = _report.Value("Processor", "Base speed");
        return threads is null ? null : speed is null ? threads : $"{threads} · {speed} base";
    }

    string? Displays()
    {
        var items = _report.Group("Displays")?.Items;
        if (items is null || items.Count == 0) return null;
        var main = items.FirstOrDefault(i => i.Value.Contains("(main)")) ?? items[0];
        return items.Count == 1 ? main.Value.Replace(" (main)", "") : $"{items.Count} displays, main {main.Value.Replace(" (main)", "")}";
    }

    static string? Join(params string?[] parts)
    {
        var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }

    FrameworkElement Summary()
    {
        var cpu = _report.Value("Processor", "Name") ?? "This PC";
        var line = string.Join("  ·  ", new[] { _report.Value("Graphics", "Card"), _report.Value("Memory", "Installed") is { } m ? m + " memory" : null, Join(_report.Value("Windows", "Edition")) }
            .Where(s => !string.IsNullOrEmpty(s)));

        var left = new StackPanel();
        left.Children.Add(Text(cpu, size: BuildFeatures.NewLook ? 22 : 20, weight: FontWeights.SemiBold));
        left.Children.Add(WithMargin(Text(line, muted: true, size: 13), 0, 3, 0, 0));

        var icon = new Border { Width = BuildFeatures.NewLook ? 56 : 46, Height = BuildFeatures.NewLook ? 56 : 46, CornerRadius = new CornerRadius(BuildFeatures.NewLook ? 16 : 13), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        icon.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        var chip = new Viewbox { Width = 24, Height = 24, Child = new Canvas { Width = 24, Height = 24, Children = { Outline("M8 6h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z M9 2v4 M15 2v4 M9 18v4 M15 18v4 M2 9h4 M2 15h4 M18 9h4 M18 15h4", "AccentTextBrush") } } };
        icon.Child = chip;

        var counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        counts.Children.Add(Count(_report.Drives.Count.ToString(), "drives", null));
        counts.Children.Add(Count(_report.HealthyDrives.ToString(), "healthy", ThemeManager.Good));
        counts.Children.Add(Count(_report.NearlyFullDrives.ToString(), "nearly full", _report.NearlyFullDrives > 0 ? ThemeManager.Warn : null));

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(counts, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(counts);
        row.Children.Add(left);
        return Card(row, accent: true);
    }

    static FrameworkElement Count(string number, string label, Brush? colour)
    {
        var s = new StackPanel { Margin = new Thickness(24, 0, 0, 0) };
        var n = new TextBlock { Text = number, FontFamily = Mono, FontSize = BuildFeatures.NewLook ? 24 : 20 };
        if (colour is not null) n.Foreground = colour; else n.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        s.Children.Add(n);
        s.Children.Add(Text(label, muted: true, size: 11.5));
        return s;
    }

    static System.Windows.Shapes.Path Outline(string data, string brushKey)
    {
        var p = new System.Windows.Shapes.Path { Data = Geometry.Parse(data), StrokeThickness = 1.8, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
        return p;
    }

    FrameworkElement KeyValues(string title, (string Label, string? Value)[] rows)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var row = 0;
        foreach (var (label, value) in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)))
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = Text(label, muted: true, size: 12.5);
            l.Margin = new Thickness(0, 3, 8, 3);
            var v = ReadOnlyValue(value!, label);
            Grid.SetRow(l, row);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            grid.Children.Add(l);
            grid.Children.Add(v);
            row++;
        }
        var body = new StackPanel();
        body.Children.Add(Heading(title));
        body.Children.Add(grid);
        var card = Card(body);
        card.Margin = new Thickness(6);
        return card;
    }

    /// <summary>A value that can be selected with the mouse.</summary>
    static TextBox ReadOnlyValue(string text, string name)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0, 3, 0, 3) };
        if (BuildFeatures.NewLook)
        {
            box.FontWeight = FontWeights.Bold;
            if (name is "Threads" or "Memory" or "Video memory" or "Driver" or "Displays" or "BIOS" or "Build") box.FontFamily = Fonts.Mono;
        }
        box.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(box, name);
        return box;
    }

    // ---- drives ----

    static readonly GridLength[] Columns = [new(2.1, GridUnitType.Star), new(2.2, GridUnitType.Star), new(1.5, GridUnitType.Star), new(60), new(112), new(110)];

    FrameworkElement DrivesCard()
    {
        var shown = _report.Drives.Where(d => _filter == 0 || (_filter == 1 ? !d.External : d.External)).ToList();
        var allHealthy = _report.HealthyDrives == _report.Drives.Count;

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var filter = Segments(["All", "Internal", "External"], _filter, i => { _filter = i; Show(); }, "DriveFilter");
        filter.Margin = new Thickness(0);
        filter.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(filter, Dock.Right);
        top.Children.Add(filter);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Heading("Drives and S.M.A.R.T. health"));
        ((TextBlock)title.Children[0]).Margin = new Thickness(0, 0, 12, 0);
        ((TextBlock)title.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(Chip(allHealthy ? $"All {_report.Drives.Count} healthy" : $"{_report.Drives.Count - _report.HealthyDrives} need attention", allHealthy));
        top.Children.Add(title);

        var list = new StackPanel();
        list.Children.Add(HeaderRow());
        foreach (var d in shown) list.Children.Add(DriveRow(d));
        if (shown.Count == 0) list.Children.Add(WithMargin(Text("No drives of this kind.", muted: true, size: 13), 0, 8, 0, 8));

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(list);
        return Card(body);
    }

    static Grid RowGrid()
    {
        var g = new Grid();
        foreach (var w in Columns) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    static FrameworkElement HeaderRow()
    {
        var g = RowGrid();
        string[] names = ["Drive", "Space", "Health", "Temp", "Powered on", "Life left"];
        for (var i = 0; i < names.Length; i++)
        {
            var t = Text(names[i].ToUpperInvariant(), muted: true, size: 11);
            t.Margin = new Thickness(i == 0 ? 4 : 8, 0, 0, 6);
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    FrameworkElement DriveRow(DriveSpec d)
    {
        var g = RowGrid();
        var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 8, 0, 8), Child = g };
        line.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");

        void Put(FrameworkElement e, int col, double left = 8) { e.Margin = new Thickness(left, 0, 0, 0); e.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(e, col); g.Children.Add(e); }

        // Drive: icon, model and what kind it is.
        var name = new StackPanel { Orientation = Orientation.Horizontal };
        var tile = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        var glyph = d.Kind.Contains("SSD") || d.Kind.StartsWith("NVMe") ? "M4 7h16v10H4z M8 7V4 M12 7V4 M16 7V4"
            : d.External ? "M7 4h10v16H7z M10 8h4" : "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0 M10 12a2 2 0 1 0 4 0 2 2 0 1 0-4 0";
        tile.Child = new Viewbox { Width = 16, Height = 16, Child = new Canvas { Width = 24, Height = 24, Children = { Outline(glyph, d.Kind.Contains("SSD") || d.Kind.StartsWith("NVMe") ? "AccentTextBrush" : "MutedBrush") } } };
        name.Children.Add(tile);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var model = Text(d.Name, size: 13, weight: FontWeights.SemiBold);
        var what = Text($"{(d.External ? "External" : "Internal")} · {d.Kind}{(d.Volumes.Length > 0 ? " · " + d.Volumes : "")}".Replace(" ·  ·", " ·"), muted: true, size: 11.5);
        foreach (var tb in new[] { model, what }) { tb.TextWrapping = TextWrapping.NoWrap; tb.TextTrimming = TextTrimming.CharacterEllipsis; tb.ToolTip = tb.Text; }
        labels.Children.Add(model);
        labels.Children.Add(what);
        name.Children.Add(labels);
        Put(name, 0, 4);

        // Space: a bar of how much is used, and how much is free.
        var space = new StackPanel();
        if (d.Volumes.Length > 0)
        {
            var bar = new Grid { Height = 6 };
            var track = new Border { CornerRadius = new CornerRadius(3) };
            track.SetResourceReference(Border.BackgroundProperty, "PanelBorderBrush");
            bar.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
            if (d.AlmostFull) fill.Background = ThemeManager.Warn; else fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            bar.Children.Add(fill);
            bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * d.UsedFraction;
            space.Children.Add(bar);
            var note = Text($"{SpecSheet.Bytes(d.FreeBytes)} free of {SpecSheet.Bytes(d.TotalBytes)}{(d.AlmostFull ? " · almost full" : "")}", muted: !d.AlmostFull, size: 11.5);
            if (d.AlmostFull) note.Foreground = ThemeManager.Warn;
            note.Margin = new Thickness(0, 4, 0, 0);
            space.Children.Add(note);
        }
        else space.Children.Add(Text($"{SpecSheet.Bytes(d.TotalBytes)} · no drive letter", muted: true, size: 11.5));
        Put(space, 1);

        // Health: a green tag, or an amber one when something is wrong.
        Put(Chip(d.Health.Length > 0 ? d.Health : "Unknown", d.HealthOk && d.Health.Length > 0), 2);

        var temp = new TextBlock { Text = d.TempC is { } t ? t + "°" : "–", FontFamily = Mono, FontSize = 12.5 };
        temp.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        Put(temp, 3);

        var hours = new StackPanel { Orientation = Orientation.Horizontal };
        var h = new TextBlock { Text = d.PowerOnHours is { } ph and > 0 ? $"{ph:N0} h" : "–", FontFamily = Mono, FontSize = 12.5 };
        h.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        hours.Children.Add(h);
        if (d.Age.Length > 0) hours.Children.Add(WithMargin(Text(d.Age, muted: true, size: 11), 6, 1, 0, 0));
        Put(hours, 4);

        if (d.WearPercent is { } wear)
        {
            var left = Math.Clamp(100 - wear, 0, 100);
            var life = new StackPanel();
            life.Children.Add(new TextBlock { Text = left + "%", FontFamily = Mono, FontSize = 12, Foreground = left < 20 ? ThemeManager.Warn : ThemeManager.Good });
            var bar = new Grid { Height = 6, Margin = new Thickness(0, 3, 0, 0) };
            var track = new Border { CornerRadius = new CornerRadius(3) };
            track.SetResourceReference(Border.BackgroundProperty, "PanelBorderBrush");
            bar.Children.Add(track);
            var fill = new Border { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = left < 20 ? ThemeManager.Warn : ThemeManager.Good };
            bar.Children.Add(fill);
            bar.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * left / 100;
            life.Children.Add(bar);
            Put(life, 5);
        }
        else Put(Text("Not reported", muted: true, size: 12), 5);
        return line;
    }

    static Border Chip(string text, bool good)
    {
        var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        if (good) { chip.Background = ThemeManager.GoodSoft; label.Foreground = ThemeManager.Good; }
        else { chip.SetResourceReference(Border.BackgroundProperty, "BannerBrush"); label.Foreground = ThemeManager.Warn; }
        chip.Child = label;
        return chip;
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }

    void Copy()
    {
        if (_report.Groups.Count == 0) { _status.Text = "Nothing to copy yet."; return; }
        // The clipboard can be held by another program for a moment; say so instead of failing.
        try { Clipboard.SetText(SpecSheet.ToText(_report.Groups)); _status.Text = "Copied. Paste it into a support request or a problem report."; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException) { _status.Text = "Another program is using the clipboard. Try again."; }
    }
}
