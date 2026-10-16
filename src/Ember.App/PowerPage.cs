using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
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
        if (BuildFeatures.NewLook) _root.Margin = new Thickness(32, 30, 32, 28);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        _sourceChip.Child = _sourceChipText;
        if (BuildFeatures.NewLook)
        {
            var range = Segments(["10 min", "1 hour"], 0, i => { _rangePoints = i == 0 ? 600 : 3600; DrawChartNew(); }, "PowerRange");
            range.Margin = new Thickness(0);
            _root.Children.Add(Ui.Heading("Power", "What your PC draws at the wall, where it goes, and roughly what it costs.", range));
        }
        // The redesign shows the source chip inside the hero card; the classic page keeps it in the heading.
        if (!BuildFeatures.NewLook)
        {
            DockPanel.SetDock(_sourceChip, Dock.Right);
            head.Children.Add(_sourceChip);
        }
        if (!BuildFeatures.NewLook)
        {
            var title = new TextBlock { Text = "Power", FontSize = 24, FontWeight = FontWeights.SemiBold };
            AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
            head.Children.Add(title);
            _root.Children.Add(head);
        }

        var cards = BuildCards();   // always built: Update fills them, even when the redesign leaves them out
        if (BuildFeatures.NewLook)
        {
            // Redesign: the live figure with its chart beside the cost estimate, then the breakdown beside the daily chart.
            _root.Children.Add(TwoColumns(BuildNewHero(), BuildCostCard(), 1.35, 1));
            _root.Children.Add(TwoColumns(BuildBreakdown(), BuildDailyCard(), 1.2, 1));
        }
        else
        {
            _root.Children.Add(BuildHero());
            _root.Children.Add(BuildChart());
            _root.Children.Add(cards);
        }

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
            if (brushKey == "GoodBrush") dot.Fill = Rest; else dot.SetResourceReference(Shape.FillProperty, brushKey);
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

    // ---- cost estimate, daily use and breakdown (redesign) ----

    /// <summary>The colour of the part of the draw nobody measures: a quiet brown-grey in the redesign, green in the classic page.</summary>
    static Brush Rest => BuildFeatures.NewLook ? (Brush)Application.Current.FindResource("ThumbHoverBrush") : ThemeManager.Good;

    PowerLedger _ledger = new();
    Func<double> _price = () => 0.28;
    Action<double> _savePrice = _ => { };
    readonly TextBlock _costToday = Text("–", size: 24, weight: FontWeights.SemiBold), _costMonth = Text("–", size: 24, weight: FontWeights.SemiBold), _costYear = Text("–", size: 24, weight: FontWeights.SemiBold);
    readonly TextBlock _kwhToday = Text("", muted: true, size: 12), _kwhMonth = Text("", muted: true, size: 12), _kwhYear = Text("", muted: true, size: 12);
    readonly TextBlock _costNote = Text("", muted: true, size: 12);
    readonly TextBox _priceBox = new() { Style = Sty("TextField"), Width = 78, TextAlignment = TextAlignment.Right, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
    readonly Canvas _daily = new() { Height = 168, ClipToBounds = true };
    readonly Grid _table = new();
    string? _dailyKey, _tableKey;

    /// <summary>Gives the page the energy ledger and the person's price per kWh (and where to keep a change to it).</summary>
    public void AttachCost(PowerLedger ledger, Func<double> price, Action<double> savePrice)
    {
        (_ledger, _price, _savePrice) = (ledger, price, savePrice);
        _priceBox.Text = price().ToString("0.00##", System.Globalization.CultureInfo.CurrentCulture);
    }

    public void SaveLedger()
    {
        if (_ledger.Dirty) _ledger.TrySave(PowerLedger.DefaultPath, DateOnly.FromDateTime(DateTime.Now));
    }

    static FrameworkElement TwoColumns(FrameworkElement left, FrameworkElement right, double leftWeight, double rightWeight)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star), MinWidth = 260 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWeight, GridUnitType.Star), MinWidth = 260 });
        left.Margin = new Thickness(0, 0, 6, 0);
        right.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }

    /// <summary>The redesign's big card: "Whole PC, right now", the figure in large type with the average and peak, and the live chart under it.</summary>
    FrameworkElement BuildNewHero()
    {
        var top = new DockPanel();
        DockPanel.SetDock(_sourceChip, Dock.Right);
        top.Children.Add(_sourceChip);
        var label = Text("WHOLE PC, RIGHT NOW", size: 11.5, weight: FontWeights.SemiBold);
        label.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        top.Children.Add(label);

        _heroWatts.FontFamily = Fonts.Mono;
        _heroWatts.FontSize = 54;
        _heroWatts.FontWeight = FontWeights.Medium;
        var figure = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        figure.Children.Add(_heroWatts);
        _heroStats.VerticalAlignment = VerticalAlignment.Bottom;
        _heroStats.Margin = new Thickness(16, 0, 0, 10);
        figure.Children.Add(_heroStats);

        _area.Opacity = 1;
        _line.SetResourceReference(Shape.StrokeProperty, CpuKey);
        _chart.Children.Add(_area);
        _chart.Children.Add(_line);
        _chart.Height = 96;

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(figure);
        body.Children.Add(_heroSource);
        body.Children.Add(WithMargin(_chart, 0, 10, 0, 0));
        var hero = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 16, 20, 14), BorderThickness = new Thickness(1), Child = body };
        hero.SetResourceReference(Border.BackgroundProperty, "SoftCardBrush");
        hero.SetResourceReference(Border.BorderBrushProperty, "SoftCardBorderBrush");
        _sourceChip.CornerRadius = new CornerRadius(18);
        _sourceChip.BorderThickness = new Thickness(1);
        _sourceChip.Padding = new Thickness(14, 5, 14, 6);
        _sourceChip.Background = Brushes.Transparent;
        _sourceChip.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        _sourceChipText.FontSize = 13;
        return hero;
    }

    FrameworkElement BuildCostCard()
    {
        var symbol = new TextBlock { Text = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.CurrencySymbol, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0), FontFamily = Fonts.Mono };
        symbol.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(_priceBox, "Price per kilowatt hour");
        _priceBox.LostKeyboardFocus += (_, _) => CommitPrice();
        _priceBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { CommitPrice(); System.Windows.Input.Keyboard.ClearFocus(); } };
        // The price sits in the row of the mockup: its label on the left, the figure on the right, no box around the number.
        _priceBox.Style = null;
        _priceBox.BorderThickness = new Thickness(0);
        _priceBox.Background = Brushes.Transparent;
        _priceBox.FontFamily = Fonts.Mono;
        _priceBox.FontWeight = FontWeights.Bold;
        _priceBox.FontSize = 13;
        _priceBox.Width = 52;
        _priceBox.Padding = new Thickness(0);
        _priceBox.SetResourceReference(TextBox.ForegroundProperty, "TextBrush");
        _priceBox.SetResourceReference(TextBox.CaretBrushProperty, "TextBrush");
        var priceInput = new StackPanel { Orientation = Orientation.Horizontal };
        priceInput.Children.Add(symbol);
        priceInput.Children.Add(_priceBox);
        var priceRow = new DockPanel();
        DockPanel.SetDock(priceInput, Dock.Right);
        priceRow.Children.Add(priceInput);
        priceRow.Children.Add(Ui.Muted("Price per kWh", 13));
        var priceBox = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 14, 0, 0), Child = priceRow };
        priceBox.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        priceBox.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");

        foreach (var t in new[] { _costToday, _costMonth, _costYear }) { t.FontFamily = Fonts.Mono; t.FontSize = 22; }
        foreach (var t in new[] { _kwhToday, _kwhMonth, _kwhYear }) { t.FontFamily = Fonts.Mono; t.FontSize = 11.5; }
        _costMonth.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        var figures = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        figures.Children.Add(Figure("Today so far", _costToday, _kwhToday));
        figures.Children.Add(Figure("This month", _costMonth, _kwhMonth));
        figures.Children.Add(Figure("Per year", _costYear, _kwhYear));
        var body = new StackPanel();
        body.Children.Add(CardHead("Cost estimate", Ui.Chip("Based on your average", false)));
        body.Children.Add(figures);
        body.Children.Add(priceBox);
        body.Children.Add(WithMargin(_costNote, 0, 8, 0, 0));
        return Ui.Card(body, name: "Cost estimate");
    }

    /// <summary>The top line of a card: the small capital heading on the left and a control on the right.</summary>
    static FrameworkElement CardHead(string title, FrameworkElement? right)
    {
        var head = new DockPanel();
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        head.Children.Add(Ui.Label(title));
        return head;
    }

    FrameworkElement BuildDailyCard()
    {
        var legend = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, key) in new[] { ("CPU", CpuKey), ("GPU", GpuKey), ("Rest", "GoodBrush") })
        {
            var dot = new Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            if (key == "GoodBrush") dot.Fill = Rest; else dot.SetResourceReference(Shape.FillProperty, key);
            legend.Children.Add(dot);
            legend.Children.Add(WithMargin(Ui.Text(name, 12.5), 0, 0, 14, 0));
        }
        var body = new StackPanel();
        body.Children.Add(CardHead("Daily use", legend));
        _daily.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(_daily);
        _daily.SizeChanged += (_, _) => { _dailyKey = null; DrawDaily(); };
        return Ui.Card(body, name: "Daily use");
    }

    /// <summary>"Where the power goes": the split bar of the live draw, then the week's average per part.</summary>
    FrameworkElement BuildBreakdown()
    {
        var body = new StackPanel();
        body.Children.Add(CardHead("Where the power goes", Ui.Muted("Average over 7 days", 12)));
        _split.Margin = new Thickness(0, 14, 0, 10);
        body.Children.Add(_split);
        body.Children.Add(_table);
        return Ui.Card(body, name: "Where the power goes");
    }

    static FrameworkElement Figure(string label, TextBlock value, TextBlock kwh)
    {
        var s = new StackPanel();
        s.Children.Add(Text(label, muted: true, size: 12));
        s.Children.Add(WithMargin(value, 0, 4, 0, 0));
        s.Children.Add(WithMargin(kwh, 0, 2, 0, 0));
        return s;
    }

    void CommitPrice()
    {
        if (double.TryParse(_priceBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var v) && v is >= 0 and <= 10)
        {
            if (Math.Abs(v - _price()) > 1e-9) _savePrice(v);
            _dailyKey = _tableKey = null;
        }
        _priceBox.Text = _price().ToString("0.00##", System.Globalization.CultureInfo.CurrentCulture);   // a bad entry goes back to the last good price
    }

    void UpdateCost()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var price = _price();
        var t = _ledger.Today(today);
        var typical = _ledger.Typical(today);
        _costToday.Text = t.Seconds > 0 ? PowerCost.Format(PowerCost.Of(t.Kwh, price)) : "–";
        _kwhToday.Text = t.Seconds > 0 ? $"{t.Kwh:0.00} kWh" : "No readings yet";
        var daily = typical.TotalKwh;
        _costMonth.Text = daily > 0 ? PowerCost.Format(PowerCost.Of(daily * PowerCost.DaysPerMonth, price)) : "–";
        _kwhMonth.Text = daily > 0 ? $"{daily * PowerCost.DaysPerMonth:0} kWh" : "";
        _costYear.Text = daily > 0 ? PowerCost.Format(PowerCost.Of(daily * PowerCost.DaysPerYear, price)) : "–";
        _kwhYear.Text = daily > 0 ? $"{daily * PowerCost.DaysPerYear:#,0} kWh" : "";
        _costNote.Text = daily == 0 ? "The estimate appears once Ember has watched the PC for a few minutes."
            : _totalFromPlug ? "An estimate from your own use, measured at the wall. Not a bill."
            : "An estimate from the CPU and graphics card only. Link your PC's plug for the whole PC.";

        DrawDaily();
        var key = $"{today}|{price}|{typical}";
        if (key != _tableKey) { _tableKey = key; FillTable(typical, price); }
    }

    void DrawDaily()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = _ledger.Last(8, today);
        var key = $"{_daily.ActualWidth:0}|{string.Join(",", days.Select(d => d.Wh.ToString("0")))}";
        if (key == _dailyKey || _daily.ActualWidth < 100) return;
        _dailyKey = key;
        _daily.Children.Clear();
        const double labelH = 18, valueH = 16;
        var plotH = _daily.Height - labelH - valueH;
        var max = Math.Max(0.1, days.Max(d => d.Kwh));
        var slot = _daily.ActualWidth / days.Count;
        var barW = Math.Min(40, slot - 10);
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var x = i * slot + (slot - barW) / 2;
            var isToday = d.Date == today;
            var y = _daily.Height - labelH;
            foreach (var (wh, brushKey) in new[] { (d.RestWh, "GoodBrush"), (d.GpuWh, GpuKey), (d.CpuWh, CpuKey) })
            {
                var h = d.Wh <= 0 ? 0 : wh / 1000 / max * plotH;
                if (h < 0.5) continue;
                var seg = new Border { Width = barW, Height = h, CornerRadius = new CornerRadius(2), Opacity = isToday ? 0.6 : 1 };
                if (brushKey == "GoodBrush") seg.Background = Rest; else seg.SetResourceReference(Border.BackgroundProperty, brushKey);
                Canvas.SetLeft(seg, x);
                Canvas.SetTop(seg, y - h);
                _daily.Children.Add(seg);
                y -= h + 1;
            }
            var value = Text(d.Seconds > 0 ? d.Kwh.ToString("0.0") : "", size: 11);
            value.FontFamily = Fonts.Mono;
            value.Width = slot; value.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(value, i * slot);
            Canvas.SetTop(value, Math.Max(0, y - valueH));
            _daily.Children.Add(value);
            var name = Text(isToday ? "Today" : d.Date.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture), muted: !isToday, size: 11);
            name.Width = slot; name.TextAlignment = TextAlignment.Center;
            Canvas.SetLeft(name, i * slot);
            Canvas.SetTop(name, _daily.Height - labelH + 2);
            _daily.Children.Add(name);
        }
    }

    void FillTable(PowerLedger.Averages a, double price)
    {
        _table.Children.Clear();
        _table.RowDefinitions.Clear();
        _table.ColumnDefinitions.Clear();
        foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(66), new GridLength(86), new GridLength(104) })
            _table.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        if (a.TotalKwh <= 0)
        {
            _table.RowDefinitions.Add(new RowDefinition());
            _table.Children.Add(Text("This fills in once Ember has watched the PC for a few minutes.", muted: true, size: 13));
            return;
        }
        var mono = Fonts.Mono;
        void Cell(string text, int row, int col, bool muted = false, bool bold = false, Brush? brush = null)
        {
            var t = Text(text, muted: muted, size: col == 0 ? 13 : 12.5, weight: bold ? FontWeights.SemiBold : null);
            if (col > 0) { t.FontFamily = mono; t.TextAlignment = TextAlignment.Right; }
            if (brush is not null) t.Foreground = brush;
            t.Margin = new Thickness(0, 6, col == 3 ? 0 : 8, 6);
            Grid.SetRow(t, row); Grid.SetColumn(t, col);
            _table.Children.Add(t);
        }
        var rows = new (string Name, double W, double Kwh, bool Bold)[]
        {
            ("CPU", a.CpuW, a.CpuKwh, false), ("Graphics card", a.GpuW, a.GpuKwh, false), ("Board and the rest", a.RestW, a.RestKwh, false), ("Total", a.TotalW, a.TotalKwh, true),
        };
        string[] heads = ["Part", "Average", "kWh a day", "Cost a month"];
        _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < 4; c++) Cell(heads[c].ToUpperInvariant(), 0, c, muted: true);
        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            _table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false };
            line.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
            Grid.SetRow(line, i + 1); Grid.SetColumnSpan(line, 4);
            _table.Children.Add(line);
            Cell(r.Name, i + 1, 0, bold: r.Bold);
            if (i < 3)
            {
                var dot = new Ellipse { Width = 9, Height = 9, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
                if (i == 2) dot.Fill = Rest; else dot.SetResourceReference(Shape.FillProperty, i == 0 ? CpuKey : GpuKey);
                Grid.SetRow(dot, i + 1);
                _table.Children.Add(dot);
                ((TextBlock)_table.Children[^2]).Margin = new Thickness(18, 6, 8, 6);
            }
            Cell($"{r.W:0} W", i + 1, 1, bold: r.Bold);
            Cell($"{r.Kwh:0.00}", i + 1, 2, bold: r.Bold);
            var money = PowerCost.Format(PowerCost.Of(r.Kwh * PowerCost.DaysPerMonth, price));
            if (r.Bold) Cell(money, i + 1, 3, bold: true, brush: Application.Current.FindResource("AccentTextBrush") as Brush);
            else Cell(money, i + 1, 3);
        }
    }

    public void Attach(IReadOnlyList<IPlugProvider> providers, PlugLinkStore store)
    {
        _providers = providers;
        _store = store;
        _linkState = store.Load();
    }

    public PlugLinks Links => _linkState;

    /// <summary>The whole-PC draw right now (from the wall plug when one is linked), and whether it comes from the plug.</summary>
    public double? TotalWatts => _total.Now;
    public bool TotalFromPlug => _totalFromPlug;

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
        _ledger.Add(at, sensors.CpuW, sensors.GpuW, wall is { } wallW && sensors.MeasuredW is not null ? Math.Max(0, wallW - (sensors.CpuW ?? 0) - (sensors.GpuW ?? 0)) : sensors.BoardW);
    }

    public void Update()
    {
        UpdateHero();
        UpdateCost();
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

    /// <summary>The figure as a large number with a small unit beside it.</summary>
    void ShowWatts(double? watts)
    {
        _heroWatts.Inlines.Clear();
        _heroWatts.Inlines.Add(new Run(watts is { } w ? w.ToString("0") : "–"));
        if (watts is not null) _heroWatts.Inlines.Add(new Run(" ") { FontSize = 10 });
        if (watts is not null) _heroWatts.Inlines.Add(new Run("W") { FontSize = 20, FontWeight = FontWeights.Normal, Foreground = (Brush)Application.Current.FindResource("MutedBrush") });
    }

    void UpdateHero()
    {
        if (BuildFeatures.NewLook) ShowWatts(_total.Now);
        else _heroWatts.Text = _total.Now is { } w ? $"{w:0} W" : "–";
        _heroSource.Text = _totalFromPlug ? "Measured at the wall by your smart plug" : _total.Now is null ? "Waiting for the sensors" : "CPU and GPU only. Link your PC's plug for the whole PC";
        _heroStats.Text = _total.Average is { } a ? (BuildFeatures.NewLook ? $"avg {a:0} W  ·  peak {_total.Peak:0} W" : $"average {a:0} W   ·   peak {_total.Peak:0} W   ·   {_total.Kwh:0.000} kWh used") : "";
        _sourceChipText.Text = _totalFromPlug ? "Wall plug" : "Sensors only";
        if (BuildFeatures.NewLook)
        {
            var plugName = _providers.SelectMany(p => p.Plugs).FirstOrDefault(p => p.Uuid == _linkState.PcPlug)?.Name;
            _sourceChipText.Text = _totalFromPlug && plugName is not null ? $"Measured by plug \"{plugName}\"" : _totalFromPlug ? "Measured at the wall" : "CPU and GPU sensors only";
            _sourceChipText.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        }
        else
        {
            _sourceChip.Background = _totalFromPlug ? ThemeManager.GoodSoft : (Brush)Application.Current.FindResource("AccentSoftBrush");
            _sourceChipText.Foreground = _totalFromPlug ? ThemeManager.Good : (Brush)Application.Current.FindResource("AccentTextBrush");
        }

        // The split bar and its legend: CPU, GPU and the rest of the wall draw (or nothing when only CPU and GPU are known).
        var parts = new (string Name, double? W, string? Key, Brush? Fixed)[]
        {
            ("CPU", _cpu.Now, CpuKey, null), ("GPU", _gpu.Now, GpuKey, null),
            ("Motherboard and the rest", _totalFromPlug ? _rest.Now : null, null, Rest),
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
        if (BuildFeatures.NewLook) { DrawChartNew(); return; }
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

    int _rangePoints = 600;   // the chart shows the last ten minutes or the last hour

    /// <summary>The live chart of the redesign: a smooth line with a soft area under it, no axis labels.</summary>
    void DrawChartNew()
    {
        _chart.Children.Clear();
        var all = _total.Recent;
        var pts = all.Count > _rangePoints ? all.Skip(all.Count - _rangePoints).ToList() : all.ToList();
        var w = _chart.ActualWidth;
        var h = _chart.Height;
        if (w < 10 || pts.Count < 2) { _chartNote.Text = "Waiting for readings…"; return; }
        var top = Math.Max(20, Math.Ceiling(pts.Max() * 1.25 / 20) * 20);
        var step = w / (Math.Max(pts.Count, 60) - 1);
        var pc = new List<Point>(pts.Count);
        for (var i = 0; i < pts.Count; i++) pc.Add(new Point(i * step, h - 3 - pts[i] / top * (h - 10)));
        var color = ((SolidColorBrush)Application.Current.FindResource(CpuKey)).Color;
        var area = new Path { Data = Ui.Smooth(pc, h), IsHitTestVisible = false, Fill = new LinearGradientBrush(Color.FromArgb(0x70, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), 90) };
        var line = new Path { Data = Ui.Smooth(pc), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        line.SetResourceReference(Shape.StrokeProperty, CpuKey);
        _chart.Children.Add(area);
        _chart.Children.Add(line);
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
