using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ember.Core;
using Ember.Lighting.Meross;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// How much power the PC draws: CPU and GPU from the PC's own sensors, the whole PC from a linked smart plug, and the rest worked
/// out as the difference. Linked plugs are read only. Figures are kept for this session only.
/// </summary>
public sealed class PowerPage : UserControl
{
    // Where each part of the draw is drawn: the accent for the CPU and the GPU colour for the graphics card, as on the dashboard.
    const string CpuKey = "AccentBrush", GpuKey = "GpuBrush";

    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _plugRows = new();
    readonly Canvas _chart = new() { Height = 170, ClipToBounds = true };
    readonly Path _area = new();
    readonly Polyline _line = new() { StrokeThickness = 2.2, StrokeLineJoin = PenLineJoin.Round };
    readonly TextBlock _chartNote = Text("", muted: true, size: 12);
    readonly TextBlock _heroWatts = Text("–", size: 52, weight: FontWeights.SemiBold), _heroSource = Text("", muted: true, size: 13), _heroStats = Text("", muted: true, size: 13);
    readonly Border _sourceChip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _sourceChipText = new() { FontSize = 12 };
    readonly Grid _split = new() { Height = 12, Margin = new Thickness(0, 14, 0, 10) };
    readonly StackPanel _legend = new();
    readonly Dictionary<string, TextBlock> _plugWatts = [];
    readonly Dictionary<string, PowerTrack> _plugTracks = [];
    readonly Dictionary<string, Card> _cards = [];
    readonly Dictionary<string, FrameworkElement> _cardPanels = [];
    FrameworkElement _plugPanel = null!;
    readonly TextBlock _noPlugs = Text("No smart plugs connected. Add a Meross or Tapo account in Settings to measure the whole PC at the wall.", muted: true, size: 13);

    sealed record Card(TextBlock Watts, TextBlock Share, TextBlock Note, TextBlock Stats, Canvas Spark, Polyline SparkLine);

    PowerTrack _total = new(), _cpu = new(), _gpu = new(), _board = new(), _rest = new(), _other = new();
    bool _totalFromPlug;
    IReadOnlyList<IPlugProvider> _providers = [];
    PlugLinkStore? _store;
    PlugLinks _linkState = PlugLinks.Empty;
    string? _plugKey;
    double _lastMeasured;

    public PowerPage()
    {
        Content = _root;
        Focusable = false;

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _sourceChip.Child = _sourceChipText;
        DockPanel.SetDock(_sourceChip, Dock.Right);
        head.Children.Add(_sourceChip);
        var title = new TextBlock { Text = "Power", FontSize = 24, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        head.Children.Add(title);
        _root.Children.Add(head);

        _root.Children.Add(BuildHero());
        _root.Children.Add(BuildChart());
        _root.Children.Add(BuildCards());

        var plugBody = new StackPanel();
        plugBody.Children.Add(Text("Link a smart plug to see what your whole PC draws from the wall, and what other devices on plugs use. Ember only reads from a plug and never switches it. Only plugs that measure energy (Meross MSS310 and MSS315, Tapo P110 and P115) report power.", muted: true, size: 13));
        plugBody.Children.Add(_plugRows);
        _plugPanel = Panel("Smart plugs (read only, beta)", plugBody, new Thickness(0));
        _root.Children.Add(_plugPanel);
        _root.Children.Add(WithMargin(_noPlugs, 4, 0, 0, 0));
        ThemeManager.Changed += () => _plugKey = null;
    }

    FrameworkElement BuildHero()
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        left.Children.Add(Text("Whole PC", muted: true, size: 13));
        left.Children.Add(_heroWatts);
        left.Children.Add(_heroSource);
        left.Children.Add(WithMargin(_heroStats, 0, 4, 0, 0));

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(Text("Where it goes", muted: true, size: 13));
        right.Children.Add(_split);
        right.Children.Add(_legend);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);

        var hero = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(0, 0, 0, 12), BorderThickness = new Thickness(1), Child = grid };
        hero.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        hero.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return hero;
    }

    FrameworkElement BuildChart()
    {
        _area.Opacity = 1;
        _line.SetResourceReference(Shape.StrokeProperty, CpuKey);
        _chart.Children.Add(_area);
        _chart.Children.Add(_line);
        var body = new StackPanel();
        body.Children.Add(_chart);
        body.Children.Add(WithMargin(_chartNote, 0, 4, 0, 0));
        return Panel("Whole PC, last few minutes", body, new Thickness(0, 0, 0, 12));
    }

    FrameworkElement BuildCards()
    {
        var grid = new Grid { Margin = new Thickness(-6, 0, -6, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var specs = new[] { ("CPU", CpuKey), ("GPU", GpuKey), ("Motherboard and the rest", "GoodBrush"), ("Other plugs", "MutedBrush") };
        for (var i = 0; i < specs.Length; i++)
        {
            var (name, brushKey) = specs[i];
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (brushKey == "GoodBrush") dot.Fill = ThemeManager.Good; else dot.SetResourceReference(Shape.FillProperty, brushKey);
            var share = Text("", muted: true, size: 12);
            var titleRow = new DockPanel();
            DockPanel.SetDock(share, Dock.Right);
            titleRow.Children.Add(share);
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(dot);
            label.Children.Add(Text(name, weight: FontWeights.SemiBold, size: 14));
            titleRow.Children.Add(label);

            var watts = Text("–", size: 30, weight: FontWeights.SemiBold);
            var spark = new Canvas { Height = 38, Margin = new Thickness(0, 6, 0, 2), ClipToBounds = true };
            var sparkLine = new Polyline { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round };
            if (brushKey == "GoodBrush") sparkLine.Stroke = ThemeManager.Good; else sparkLine.SetResourceReference(Shape.StrokeProperty, brushKey);
            spark.Children.Add(sparkLine);
            var note = Text("", muted: true, size: 12);
            var stats = Text("", muted: true, size: 12);

            var body = new StackPanel();
            body.Children.Add(titleRow);
            body.Children.Add(WithMargin(watts, 0, 6, 0, 0));
            body.Children.Add(spark);
            body.Children.Add(stats);
            body.Children.Add(WithMargin(note, 0, 4, 0, 0));
            var panel = Panel(name, body, new Thickness(6, 6, 6, 6));
            panel.Header = null;
            Grid.SetColumn(panel, i % 2);
            Grid.SetRow(panel, i / 2);
            grid.Children.Add(panel);
            _cardPanels[name] = panel;
            _cards[name] = new Card(watts, share, note, stats, spark, sparkLine);
        }
        return grid;
    }

    public void Attach(IReadOnlyList<IPlugProvider> providers, PlugLinkStore store)
    {
        _providers = providers;
        _store = store;
        _linkState = store.Load();
    }

    public PlugLinks Links => _linkState;

    double? FreshWatts(string id)
    {
        foreach (var p in _providers)
            if (p.ReadingOf(id) is { Watts: { } w } r && DateTime.UtcNow - r.At < TimeSpan.FromSeconds(30)) return w;
        return null;
    }

    /// <summary>Called with each sensor snapshot, whether or not the page is showing, so the figures keep counting.</summary>
    public void Feed(DateTimeOffset at, PowerSummary sensors)
    {
        var wall = _linkState.PcPlug is { } pc ? FreshWatts(pc) : null;
        var fromPlug = wall is not null;
        if (fromPlug != _totalFromPlug) { _total = new PowerTrack(); _totalFromPlug = fromPlug; } // watts of a different thing: start a fresh count
        _cpu.Add(at, sensors.CpuW);
        _gpu.Add(at, sensors.GpuW);
        _board.Add(at, sensors.BoardW);
        _total.Add(at, wall ?? sensors.MeasuredW);
        // A board that has its own power sensor is shown directly; otherwise the rest is the wall draw minus what the CPU and GPU report.
        _rest.Add(at, wall is { } w && sensors.MeasuredW is { } m ? Math.Max(0, w - (sensors.CpuW ?? 0) - (sensors.GpuW ?? 0)) : sensors.BoardW);
        _lastMeasured = sensors.MeasuredW ?? 0;

        double? others = null;
        foreach (var p in _providers.SelectMany(p => p.Plugs).Where(p => _linkState.IsLinked(p.Uuid) && p.Uuid != _linkState.PcPlug))
            if (FreshWatts(p.Uuid) is { } pw)
            {
                others = (others ?? 0) + pw;
                if (!_plugTracks.TryGetValue(p.Uuid, out var t)) _plugTracks[p.Uuid] = t = new PowerTrack();
                t.Add(at, pw);
            }
        _other.Add(at, others);
    }

    public void Update()
    {
        UpdateHero();
        DrawChart();

        Fill("CPU", _cpu, _cpu.Now is null ? "No CPU power sensor found" : "Processor package", Share(_cpu));
        Fill("GPU", _gpu, _gpu.Now is null ? "No GPU power sensor found" : "Graphics card", Share(_gpu));
        var boardNote = _totalFromPlug ? "Board, memory, drives, fans and power supply losses: wall draw minus CPU and GPU"
            : _board.Now is not null ? "From the board's own power sensor"
            : "Boards have no power sensor, so this can only be measured at the wall. Link your PC's plug below";
        Fill("Motherboard and the rest", _rest, boardNote, Share(_rest));
        Fill("Other plugs", _other, _other.Now is null ? "Link other plugs below to see what devices on them draw" : "Everything else you linked: monitors, router, speakers", "");

        // Nothing is shown for plugs that are not there: no plugs at all hides the panel, no other linked plug hides its card.
        var anyPlugs = _providers.Any(p => p.Plugs.Count > 0);
        _plugPanel.Visibility = anyPlugs ? Visibility.Visible : Visibility.Collapsed;
        _noPlugs.Visibility = anyPlugs ? Visibility.Collapsed : Visibility.Visible;
        var hasOthers = _providers.SelectMany(p => p.Plugs).Any(p => _linkState.IsLinked(p.Uuid) && p.Uuid != _linkState.PcPlug);
        _cardPanels["Other plugs"].Visibility = hasOthers ? Visibility.Visible : Visibility.Collapsed;

        var key = string.Join(";", _providers.SelectMany(p => p.Plugs).Select(p => p.Source + p.Uuid + p.Name + _linkState.IsLinked(p.Uuid) + (_linkState.PcPlug == p.Uuid)))
            + "#" + string.Join(";", _providers.Select(p => p.Status));
        if (key != _plugKey) { _plugKey = key; BuildPlugRows(); }
        foreach (var (id, text) in _plugWatts) text.Text = PlugText(id);
    }

    string Share(PowerTrack t) => t.Now is { } v && _total.Now is { } all && all > 0 ? $"{Math.Min(100, v / all * 100):0}%" : "";

    void UpdateHero()
    {
        _heroWatts.Text = _total.Now is { } w ? $"{w:0} W" : "–";
        _heroSource.Text = _totalFromPlug ? "Measured at the wall by your smart plug" : _total.Now is null ? "Waiting for the sensors" : "CPU and GPU only. Link your PC's plug for the whole PC";
        _heroStats.Text = _total.Average is { } a ? $"average {a:0} W   ·   peak {_total.Peak:0} W   ·   {_total.Kwh:0.000} kWh used" : "";
        _sourceChipText.Text = _totalFromPlug ? "Wall plug" : "Sensors only";
        _sourceChip.Background = _totalFromPlug ? ThemeManager.GoodSoft : (Brush)Application.Current.FindResource("AccentSoftBrush");
        _sourceChipText.Foreground = _totalFromPlug ? ThemeManager.Good : (Brush)Application.Current.FindResource("AccentTextBrush");

        // The split bar and its legend: CPU, GPU and the rest of the wall draw (or nothing when only CPU and GPU are known).
        var parts = new (string Name, double? W, string? Key, Brush? Fixed)[]
        {
            ("CPU", _cpu.Now, CpuKey, null), ("GPU", _gpu.Now, GpuKey, null),
            ("Motherboard and the rest", _totalFromPlug ? _rest.Now : null, null, ThemeManager.Good),
        };
        var sum = parts.Sum(p => p.W ?? 0);
        _split.ColumnDefinitions.Clear();
        _split.Children.Clear();
        _legend.Children.Clear();
        var col = 0;
        foreach (var p in parts)
        {
            var v = p.W ?? 0;
            if (sum > 0 && v > 0)
            {
                _split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(v, GridUnitType.Star) });
                var seg = new Border { CornerRadius = new CornerRadius(4), Margin = new Thickness(col == 0 ? 0 : 2, 0, 0, 0) };
                if (p.Key is not null) seg.SetResourceReference(Border.BackgroundProperty, p.Key); else seg.Background = p.Fixed;
                Grid.SetColumn(seg, col++);
                _split.Children.Add(seg);
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (p.Key is not null) dot.SetResourceReference(Shape.FillProperty, p.Key); else dot.Fill = p.Fixed;
            row.Children.Add(dot);
            row.Children.Add(Text(p.Name + "   " + (p.W is { } x ? $"{x:0} W" + (sum > 0 ? $"  ({x / sum * 100:0}%)" : "") : "–"), size: 13));
            _legend.Children.Add(row);
        }
        if (_split.ColumnDefinitions.Count == 0)
        {
            _split.ColumnDefinitions.Add(new ColumnDefinition());
            var empty = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.35 };
            empty.SetResourceReference(Border.BackgroundProperty, "PanelBorderBrush");
            _split.Children.Add(empty);
        }
    }

    void Fill(string card, PowerTrack t, string note, string share)
    {
        var c = _cards[card];
        c.Watts.Text = t.Now is { } v ? $"{v:0} W" : "–";
        c.Share.Text = share.Length > 0 ? share + " of total" : "";
        c.Note.Text = note;
        c.Stats.Text = t.Average is { } a ? $"average {a:0} W  ·  peak {t.Peak:0} W  ·  {t.Kwh:0.000} kWh" : "";
        DrawSpark(c.Spark, c.SparkLine, t.Recent);
    }

    static void DrawSpark(Canvas canvas, Polyline line, IReadOnlyList<double> pts)
    {
        if (pts.Count < 2 || canvas.ActualWidth < 10) { line.Points.Clear(); return; }
        var top = Math.Max(5, pts.Max() * 1.1);
        var step = canvas.ActualWidth / (Math.Max(pts.Count, 60) - 1);
        var pc = new PointCollection(pts.Count);
        for (var i = 0; i < pts.Count; i++) pc.Add(new Point(i * step, canvas.Height - 2 - pts[i] / top * (canvas.Height - 4)));
        line.Points = pc;
    }

    void DrawChart()
    {
        _chart.Children.Clear();
        _chart.Children.Add(_area);
        var pts = _total.Recent;
        var w = _chart.ActualWidth;
        var h = _chart.Height;
        if (w < 10) return;
        var top = pts.Count < 2 ? 100 : Math.Max(20, Math.Ceiling(pts.Max() * 1.2 / 20) * 20);
        var grid = (Brush)Application.Current.FindResource("PanelBorderBrush");
        var muted = (Brush)Application.Current.FindResource("MutedBrush");
        for (var i = 0; i <= 3; i++)
        {
            var y = h - i / 3.0 * (h - 14) - 1;
            _chart.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, StrokeDashArray = [3, 4], IsHitTestVisible = false });
            if (i > 0) { var l = new TextBlock { Text = $"{top * i / 3:0} W", FontSize = 11, Foreground = muted, IsHitTestVisible = false }; Canvas.SetLeft(l, 2); Canvas.SetTop(l, y - 15); _chart.Children.Add(l); }
        }
        if (pts.Count < 2) { _line.Points.Clear(); _area.Data = null; _chart.Children.Add(_line); _chartNote.Text = "Waiting for readings…"; return; }

        var step = w / (Math.Max(pts.Count, 60) - 1); // fills from the left, then scrolls once a full window of points is held
        var pc = new PointCollection(pts.Count);
        for (var i = 0; i < pts.Count; i++) pc.Add(new Point(i * step, h - 1 - pts[i] / top * (h - 14)));
        _line.Points = pc;

        var fig = new PathFigure { StartPoint = new Point(0, h), IsClosed = true };
        foreach (var p in pc) fig.Segments.Add(new LineSegment(p, true));
        fig.Segments.Add(new LineSegment(new Point(pc[^1].X, h), true));
        _area.Data = new PathGeometry([fig]);
        var color = ((SolidColorBrush)Application.Current.FindResource(CpuKey)).Color;
        _area.Fill = new LinearGradientBrush(Color.FromArgb(110, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90);
        _chart.Children.Add(_line);
        _chartNote.Text = _totalFromPlug ? "Measured at the wall" : "CPU plus GPU";
    }

    string PlugText(string id)
    {
        var r = _providers.Select(p => p.ReadingOf(id)).FirstOrDefault(x => x is not null);
        if (r is null) return "Not read yet";
        if (r.Watts is { } w && DateTime.UtcNow - r.At < TimeSpan.FromSeconds(30))
            return _plugTracks.TryGetValue(id, out var t) && t.Average is { } a ? $"{w:0.0} W   ·   average {a:0.0} W   ·   {t.Kwh:0.000} kWh" : $"{w:0.0} W";
        return r.Note.Length > 0 ? r.Note : "No reading";
    }

    void BuildPlugRows()
    {
        _plugRows.Children.Clear();
        _plugWatts.Clear();
        foreach (var provider in _providers.Where(p => p.Plugs.Count > 0))
        {
            _plugRows.Children.Add(WithMargin(Text(provider.Name + ": " + provider.Status, muted: true, size: 12), 0, 12, 0, 2));
            foreach (var p in provider.Plugs.OrderBy(p => p.Name))
            {
                var id = p.Uuid;
                var linked = _linkState.IsLinked(id);
                var isPc = _linkState.PcPlug == id;
                var line = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8, 8, 4), Margin = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(1) };
                line.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
                if (linked) line.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
                var row = new DockPanel();
                var controls = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(controls, Dock.Right);
                if (linked)
                    controls.Children.Add(Pill(isPc ? "This is my PC's plug" : "Set as my PC's plug", () => SetPcPlug(isPc ? null : id), "Use " + p.Name + " as the PC's plug"));
                var toggle = Switch("", linked, on => SetLinked(id, on));
                toggle.Margin = new Thickness(0, 0, 4, 6);
                AutomationProperties.SetName(toggle, "Read power from " + p.Name);
                controls.Children.Add(toggle);
                row.Children.Add(controls);
                var info = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
                info.Children.Add(Text(p.Name + "  ·  " + p.Type, size: 14));
                if (linked)
                {
                    var watts = Text(PlugText(id), muted: true, size: 13);
                    _plugWatts[id] = watts;
                    info.Children.Add(watts);
                }
                row.Children.Add(info);
                line.Child = row;
                _plugRows.Children.Add(line);
            }
        }
    }

    void SetLinked(string id, bool on)
    {
        var linked = _linkState.Linked.Where(u => u != id).ToList();
        if (on) linked.Add(id);
        Save(new PlugLinks(linked, on || _linkState.PcPlug != id ? _linkState.PcPlug : null));
    }

    void SetPcPlug(string? id) => Save(_linkState with { PcPlug = id });

    void Save(PlugLinks links)
    {
        _linkState = links;
        _store?.TrySave(links);
        _plugKey = null;
        _total = new PowerTrack(); _rest = new PowerTrack(); _other = new PowerTrack(); // what these add up has changed: count afresh
        _plugTracks.Clear();
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
}
