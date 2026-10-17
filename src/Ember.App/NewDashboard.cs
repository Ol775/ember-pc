using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Ember.Core;
using static Ember.App.Ui;

namespace Ember.App;

/// <summary>What the dashboard of the redesign needs besides the view model: the latest sensor summary and a few facts from other pages.</summary>
public sealed record DashboardInputs(DashboardSummary? Summary, PowerSummary? Power, double? WallWatts, bool WallFromPlug,
    string SceneName, bool LightsOn, int DevicesLit, string? Game, bool GameDetection, string ModeLabel);

/// <summary>The dashboard of the redesign: temperature rings, the fan mode list, the last ten minutes, the fans and four summary tiles.</summary>
public sealed class NewDashboard : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(32, 30, 32, 28) };
    readonly TextBlock _subtitle = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly Border _chip;
    readonly TextBlock _chipText;
    readonly Border _banner = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    readonly TextBlock _bannerText = new() { TextWrapping = TextWrapping.Wrap };

    // CPU and GPU cards
    readonly RingGauge _cpuRing = new() { Width = 104, Height = 104, Thickness = 10 }, _gpuRing = new() { Width = 104, Height = 104, Thickness = 10 };
    readonly TextBlock _cpuState = State(), _cpuNote = Note(), _gpuState = State(), _gpuNote = Note();
    readonly TextBlock[] _cpuStats = [Value(), Value(), Value()], _gpuStats = [Value(), Value(), Value()];

    // Mode, chart, fans
    readonly StackPanel _modes = new(), _fans = new();
    readonly TextBlock _fansMore = Muted("", 12);
    readonly Canvas _chart = new() { Height = 168, ClipToBounds = true };
    readonly TextBlock _chartTitle;
    string? _modesKey, _fansKey;
    double _chartW, _chartH;

    // Tiles
    readonly TextBlock _tilePower = TileTitle(), _tilePowerSub = TileSub(), _tileScene = TileTitle(), _tileSceneSub = TileSub(),
        _tileGame = TileTitle(), _tileGameSub = TileSub(), _tileDrives = TileTitle(), _tileDrivesSub = TileSub();

    public event Action<FanMode>? ModePicked;
    /// <summary>The chart changed size: the view model needs to know, to lay its lines out for it.</summary>
    public event Action<double, double>? ChartResized;

    static TextBlock State() { var t = Text("–", 15, "TextBrush", mono: true).WithOpacity(0.86); t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; return t; }
    static TextBlock Note() => Muted("", 12.5);
    static TextBlock Value() => Text("–", 15, mono: true);
    static TextBlock TileTitle() => Text("–", 15, bold: true);
    static TextBlock TileSub() { var t = Muted("", 12.5); t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; return t; }

    public NewDashboard()
    {
        Content = _root;
        Focusable = false;

        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        (_chip, _chipText) = LiveChip();
        var title = Text("Dashboard", 28, bold: true);
        title.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        _chip.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(_chip, Dock.Right);
        heading.Children.Add(_chip);
        var titles = new StackPanel();
        titles.Children.Add(title);
        titles.Children.Add(_subtitle);
        heading.Children.Add(titles);
        _root.Children.Add(heading);

        _banner.Child = _bannerText;
        _banner.SetResourceReference(Border.BackgroundProperty, "BannerBrush");
        _banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        AutomationProperties.SetLiveSetting(_bannerText, AutomationLiveSetting.Assertive);
        _root.Children.Add(_banner);

        // Row 1: CPU, GPU, fan mode.
        ApplyRingGradient();
        _gpuRing.SetResourceReference(RingGauge.StrokeProperty, "GpuBrush");
        var row1 = Columns((TempCard("CPU", "MutedBrush", _cpuRing, _cpuState, _cpuNote, _cpuStats, ["Load", "Clock", "Power"]), 1),
                           (TempCard("GPU", "GpuBrush", _gpuRing, _gpuState, _gpuNote, _gpuStats, ["Load", "Clock", "Power"]), 1),
                           (ModeCard(), 1.15));
        row1.Margin = new Thickness(0, 0, 0, 16);
        _root.Children.Add(row1);

        // Row 2: the last ten minutes and the fans.
        _chartTitle = Label("Last 10 minutes");
        var row2 = Columns((ChartCard(), 2), (FansCard(), 1));
        row2.Margin = new Thickness(0, 0, 0, 16);
        _root.Children.Add(row2);

        // Row 3: four tiles.
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-8, 0, -8, 0) };
        tiles.Children.Add(Tile(BoltIcon, _tilePower, _tilePowerSub, "Wall power"));
        tiles.Children.Add(Tile(BulbIcon, _tileScene, _tileSceneSub, "Lights"));
        tiles.Children.Add(Tile(GamepadIcon, _tileGame, _tileGameSub, "Game"));
        tiles.Children.Add(Tile(CheckIcon, _tileDrives, _tileDrivesSub, "Drives"));
        _root.Children.Add(tiles);

        _chart.SizeChanged += (_, e) =>
        {
            (_chartW, _chartH) = (e.NewSize.Width, e.NewSize.Height);
            ChartResized?.Invoke(_chartW, _chartH);
        };
        ThemeManager.Changed += () => { _chartKey = null; _modesKey = null; _fansKey = null; ApplyRingGradient(); };
    }

    void ApplyRingGradient()
    {
        var accent = ((SolidColorBrush)Res("AccentBrush")).Color;
        static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        _cpuRing.Stroke = new LinearGradientBrush(Mix(accent, Colors.White, 0.22), Mix(accent, Colors.Black, 0.12), new Point(0, 0), new Point(1, 1));
    }

    static (Border, TextBlock) LiveChip()
    {
        var chip = Chip("Live, updated just now", dot: true);
        var text = (TextBlock)((StackPanel)chip.Child).Children[1];
        return (chip, text);
    }

    // ---- cards ----

    static FrameworkElement TempCard(string label, string labelBrush, RingGauge ring, TextBlock state, TextBlock note, TextBlock[] stats, string[] names)
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Label(label, labelBrush));

        var middle = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ring.VerticalAlignment = VerticalAlignment.Center;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        words.Children.Add(state);
        words.Children.Add(note);
        Grid.SetColumn(words, 1);
        middle.Children.Add(ring);
        middle.Children.Add(words);
        body.Children.Add(middle);

        var mini = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        for (var i = 0; i < names.Length; i++)
        {
            mini.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cell = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
            cell.Children.Add(Muted(names[i], 12));
            stats[i].Margin = new Thickness(0, 2, 0, 0);
            cell.Children.Add(stats[i]);
            Grid.SetColumn(cell, i);
            mini.Children.Add(cell);
        }
        body.Children.Add(mini);
        var card = Card(body, new Thickness(20, 16, 20, 16), name: label + " temperature");
        card.Height = 252;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    FrameworkElement ModeCard()
    {
        var body = new StackPanel();
        body.Children.Add(Ui.Label("Fan mode"));
        _modes.Margin = new Thickness(-4, 10, -4, 0);
        body.Children.Add(_modes);
        var card = Card(body, new Thickness(16, 16, 16, 12), name: "Fan mode");
        card.Height = 252;
        return card;
    }

    FrameworkElement ChartCard()
    {
        var head = new DockPanel();
        var legend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(LegendItem("CPU", "AccentBrush"));
        legend.Children.Add(LegendItem("GPU", "GpuBrush"));
        DockPanel.SetDock(legend, Dock.Right);
        head.Children.Add(legend);
        head.Children.Add(_chartTitle);
        var body = new StackPanel();
        body.Children.Add(head);
        _chart.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(_chart);
        var card = Card(body, new Thickness(20, 16, 20, 16), name: "Temperature history, last 10 minutes");
        card.Height = 232;
        card.VerticalAlignment = VerticalAlignment.Top;
        return card;
    }

    static FrameworkElement LegendItem(string name, string brushKey)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, brushKey);
        row.Children.Add(dot);
        row.Children.Add(Text(name, 12.5, "TextBrush"));
        return row;
    }

    FrameworkElement FansCard()
    {
        var body = new StackPanel();
        var fansHead = new DockPanel();
        DockPanel.SetDock(_fansMore, Dock.Right);
        fansHead.Children.Add(_fansMore);
        fansHead.Children.Add(Ui.Label("Fans"));
        body.Children.Add(fansHead);
        _fans.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(_fans);
        var card = Card(body, new Thickness(20, 16, 20, 16), name: "Fans");
        card.MinHeight = 232;
        return card;
    }

    static FrameworkElement Tile(string icon, TextBlock title, TextBlock sub, string name)
    {
        // A grid, so the texts get what is left of the tile and trim there instead of running past its edge.
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tile = IconTile(icon);
        tile.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(tile);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(title);
        texts.Children.Add(sub);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        var card = Card(row, new Thickness(18, 15, 18, 15), name: name);
        card.Margin = new Thickness(8, 0, 8, 0);
        return card;
    }

    // ---- updates ----

    string? _chartKey;

    public void Update(MainViewModel vm, DashboardInputs d)
    {
        var s = d.Summary;
        var unit = vm.Unit;
        var connected = vm.ConnectionText.Contains("Connected", StringComparison.OrdinalIgnoreCase) || vm.StaleNoteVisibility != Visibility.Visible;
        _chipText.Text = vm.StaleNoteVisibility == Visibility.Visible ? "Sensor data is stale" : connected ? "Live, updated just now" : vm.ConnectionText;

        var hot = s?.CpuTemp is >= 85 || s?.GpuTemp is >= 85;
        _subtitle.Text = (hot ? "Running warm. " : "All calm. ") + $"Fans are on {d.ModeLabel} and the lights are {(d.LightsOn ? "on " + d.SceneName : "off")}.";

        var banner = vm.DriverBannerText;
        _banner.Visibility = banner is null ? Visibility.Collapsed : Visibility.Visible;
        _bannerText.Text = banner ?? "";

        // CPU
        var cpu = s?.CpuTemp;
        _cpuRing.Fraction = cpu is { } c ? c / 100 : 0;
        _cpuRing.Label = cpu is { } c1 ? Temperature.Convert(c1, unit).ToString("0") + "°" : "–";
        var limit = Temperature.Convert(90, unit).ToString("0");
        (_cpuState.Text, _cpuNote.Text) = cpu switch
        {
            null => ("No reading", "Waiting for the sensors"),
            < 70 => ("Healthy", $"Well under the {limit}° limit"),
            < 85 => ("Warm", "The fans are ramping up"),
            _ => ("Hot", $"Close to the {limit}° limit"),
        };
        _cpuStats[0].Text = s?.CpuLoad is { } l ? $"{l:0}%" : "–";
        _cpuStats[1].Text = s?.CpuClockMhz is { } mhz ? $"{mhz / 1000:0.0} GHz" : "–";
        _cpuStats[2].Text = d.Power?.CpuW is { } cw ? $"{cw:0} W" : "–";

        // GPU
        var gpu = s?.GpuTemp;
        _gpuRing.Fraction = gpu is { } g ? g / 100 : 0;
        _gpuRing.Label = gpu is { } g1 ? Temperature.Convert(g1, unit).ToString("0") + "°" : "–";
        var gpuFanStopped = s?.Fans.Any(f => f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) && f.Rpm <= 0) == true;
        var idle = (s?.GpuLoad ?? 0) < 10;
        (_gpuState.Text, _gpuNote.Text) = gpu switch
        {
            null => ("No reading", "Waiting for the sensors"),
            _ when idle => ("Idle", gpuFanStopped ? "Fans stopped, as designed" : "Nothing demanding is running"),
            < 80 => ("Working", "Under load"),
            _ => ("Hot", "Running hot"),
        };
        _gpuStats[0].Text = s?.GpuLoad is { } gl ? $"{gl:0}%" : "–";
        _gpuStats[1].Text = s?.GpuClockMhz is { } gc ? $"{gc:0} MHz" : "–";
        _gpuStats[2].Text = d.Power?.GpuW is { } gw ? $"{gw:0} W" : "–";

        UpdateModes(vm.FansPage.Modes);
        DrawChart(vm.CpuLine, vm.GpuLine);
        UpdateFans(vm.Fans);

        _tilePower.Text = d.WallWatts is { } w ? $"{w:0} W" : "–";
        _tilePowerSub.Text = d.WallFromPlug ? "At the wall" : d.WallWatts is null ? "Waiting for the sensors" : "CPU and GPU only";
        _tileScene.Text = d.LightsOn ? d.SceneName : "Off";
        _tileSceneSub.Text = !d.LightsOn ? "Lights are off" : d.DevicesLit == 1 ? "1 device lit" : $"{d.DevicesLit} devices lit";
        _tileGame.Text = d.Game ?? "No game running";
        _tileGameSub.Text = !d.GameDetection ? "Detection is off" : d.Game is null ? "Detection on" : "Game mode is on";
    }

    /// <summary>The drives tile, filled in once the specs have been read.</summary>
    public void SetDrives(SpecReport report)
    {
        if (report.Drives.Count == 0) { _tileDrives.Text = "–"; _tileDrivesSub.Text = "No drive information"; return; }
        var sick = report.Drives.Count - report.HealthyDrives;
        _tileDrives.Text = sick == 0 ? "Drives healthy" : sick == 1 ? "1 drive needs a look" : $"{sick} drives need a look";
        _tileDrivesSub.Text = report.NearlyFullDrives > 0
            ? $"{report.HealthyDrives} of {report.Drives.Count} pass · {report.NearlyFullDrives} nearly full"
            : $"{report.HealthyDrives} of {report.Drives.Count} pass S.M.A.R.T.";
    }

    static string Descriptor(FanMode mode) => mode switch
    {
        FanMode.SuperQuiet => "silent", FanMode.Quiet => "everyday", FanMode.Normal => "balanced", FanMode.Performance => "cooler",
        FanMode.Extreme => "full speed", FanMode.Manual => "your curve", _ => "adaptive",
    };

    void UpdateModes(IReadOnlyList<ModeItem> modes)
    {
        var key = string.Join("|", modes.Select(m => $"{m.Mode}{m.Selected}{m.Enabled}"));
        if (key == _modesKey) return;
        _modesKey = key;
        _modes.Children.Clear();
        foreach (var m in modes.Where(m => (m.Mode != FanMode.Ai && m.Mode != FanMode.Manual) || m.Selected))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = m.Mode == FanMode.Ai ? "AI" : m.Label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            var hint = new TextBlock { Text = Descriptor(m.Mode), FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(hint, 1);
            row.Children.Add(hint);
            var mode = m.Mode;
            var radio = new RadioButton { Content = row, GroupName = "DashboardMode", IsChecked = m.Selected, IsEnabled = m.Enabled, Style = (Style)Application.Current.FindResource("ModeRow"), Margin = new Thickness(0, 2, 0, 2) };
            AutomationProperties.SetName(radio, m.Label);
            radio.Click += (_, _) => ModePicked?.Invoke(mode);
            _modes.Children.Add(radio);
        }
    }

    void UpdateFans(IReadOnlyList<FanBar> fans)
    {
        var key = string.Join("|", fans.Select(f => f.Name + f.RpmText + f.Fraction.ToString("0.00")));
        if (key == _fansKey) return;
        _fansKey = key;
        _fans.Children.Clear();
        if (fans.Count == 0) _fans.Children.Add(Muted("No fan readings", 13));
        var shown = fans.OrderByDescending(f => f.Fraction > 0 || f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase)).ThenBy(f => fans.ToList().IndexOf(f)).Take(4).ToList();
        foreach (var f in shown)
        {
            var stopped = f.Fraction <= 0 && f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase);
            var row = new Grid { Margin = new Thickness(0, 6, 0, 8) };
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Text(f.Name, 14);
            name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var value = Text(stopped ? "stopped" : f.RpmText.Replace("RPM", "rpm"), 13, stopped ? "GpuBrush" : "TextBrush", bold: true, mono: true);
            Grid.SetColumn(value, 1);
            var bar = Bar(f.Fraction, stopped || f.Name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ? "GpuBrush" : "AccentBrush");
            bar.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(bar, 1);
            Grid.SetColumnSpan(bar, 2);
            row.Children.Add(name);
            row.Children.Add(value);
            row.Children.Add(bar);
            AutomationProperties.SetName(row, f.ToString());
            _fans.Children.Add(row);
        }
        _fansMore.Text = fans.Count > shown.Count ? $"+{fans.Count - shown.Count} more on the Fans page" : "";
    }

    // ---- the last ten minutes ----

    void DrawChart(PointCollection cpu, PointCollection gpu)
    {
        if (_chartW < 40 || _chartH < 40) return;
        var key = $"{_chartW:0}x{_chartH:0}|{cpu.Count}|{(cpu.Count > 0 ? cpu[^1].ToString() : "")}|{gpu.Count}|{(gpu.Count > 0 ? gpu[^1].ToString() : "")}";
        if (key == _chartKey) return;
        _chartKey = key;
        _chart.Children.Clear();
        for (var i = 1; i <= 3; i++)
        {
            var y = _chartH * i / 4;
            var line = new Line { X1 = 0, X2 = _chartW, Y1 = y, Y2 = y, StrokeThickness = 1, IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, "PanelBorderBrush");
            _chart.Children.Add(line);
        }
        if (cpu.Count >= 2)
        {
            var area = new Path { Data = Ui.Smooth(Ui.Thin(cpu), _chartH), IsHitTestVisible = false };
            var accent = ((SolidColorBrush)Res("AccentBrush")).Color;
            area.Fill = new LinearGradientBrush([new GradientStop(Color.FromArgb(0x59, accent.R, accent.G, accent.B), 0), new GradientStop(Color.FromArgb(0, accent.R, accent.G, accent.B), 1)], 90);
            _chart.Children.Add(area);
        }
        foreach (var (points, brush) in new[] { (cpu, "AccentBrush"), (gpu, "GpuBrush") })
        {
            if (points.Count < 2) continue;
            var line = new Path { Data = Ui.Smooth(Ui.Thin(points)), StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
            line.SetResourceReference(Shape.StrokeProperty, brush);
            _chart.Children.Add(line);
        }
    }
}

static class TextBlockExtensions
{
    public static TextBlock WithOpacity(this TextBlock t, double opacity) { t.Opacity = opacity; return t; }
    public static TextBlock WithMargin(this TextBlock t, double l, double top, double r, double b) { t.Margin = new Thickness(l, top, r, b); return t; }
}
