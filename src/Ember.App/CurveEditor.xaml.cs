using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Draws the fan curve of the mode in force (CPU fan or system fans) with a marker for the temperature right now.
/// In Manual mode the points can be dragged, or moved with the arrow keys, and added or removed. A curve is only sent
/// to the service when it passes <see cref="ManualCurveRules"/>; while it does not, or while a change is on its way,
/// the editor keeps the user's version instead of the service's.
/// </summary>
public partial class CurveEditor : UserControl
{
    const double MinT = ManualCurveRules.MinTempC, MaxT = ManualCurveRules.MaxTempC;
    const double Left = 46, Right = 14, Top = 10, Bottom = 28;
    static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(500), HoldFor = TimeSpan.FromSeconds(3);

    /// <summary>A valid edited curve for a group, ready to send.</summary>
    public event Action<CurveGroup, IReadOnlyList<CurvePoint>>? Committed;
    /// <summary>The user wants the shown preset curve copied into Manual (both groups) and Manual picked.</summary>
    public event Action<IReadOnlyList<CurvePoint>>? CopyRequested;

    readonly DispatcherTimer _commit;
    readonly List<Thumb> _thumbs = [];
    List<CurvePoint> _shown = [];
    FanMode _mode = FanMode.Normal;
    CurveGroup _group = CurveGroup.Cpu;
    ManualCurves _server = ManualCurves.Default;
    double? _cpuTemp, _caseTemp;
    bool _editable, _dragging, _invalid;
    DateTime _holdUntil = DateTime.MinValue;
    int _selected = -1;
    double _dragT, _dragP, _accX, _accY;
    Polyline? _line;
    Line? _marker;
    TextBlock? _markerLabel;

    public CurveEditor()
    {
        InitializeComponent();
        _commit = new DispatcherTimer(CommitDelay, DispatcherPriority.Normal, (_, _) => Commit(), Dispatcher) { IsEnabled = false };
    }

    /// <summary>Called on every redraw of the Fans page; cheap when nothing changed.</summary>
    public void SetState(FanMode mode, ManualCurves server, double? cpuTemp, double? caseTemp)
    {
        _server = server;
        _cpuTemp = cpuTemp;
        _caseTemp = caseTemp;
        var editable = mode == FanMode.Manual;
        var modeChanged = mode != _mode || editable != _editable;
        _mode = mode;
        _editable = editable;

        var holding = _dragging || _invalid || DateTime.UtcNow < _holdUntil;
        var wanted = Wanted();
        if (holding && !modeChanged)
        {
            if (wanted.SequenceEqual(_shown)) { _holdUntil = DateTime.MinValue; holding = _dragging || _invalid; }
        }
        if (!holding || modeChanged)
        {
            if (modeChanged || !wanted.SequenceEqual(_shown))
            {
                _shown = wanted;
                _invalid = false;
                _selected = -1;
                Rebuild();
            }
        }
        UpdateTexts();
        UpdateMarker();
    }

    List<CurvePoint> Wanted() => (_editable ? _server.For(_group) : ModePresets.Get(_mode == FanMode.Ai ? FanMode.Normal : _mode).Points).ToList();

    double? CurrentTemp => _group == CurveGroup.Cpu ? _cpuTemp : _caseTemp;

    double PlotW => Math.Max(10, Plot.ActualWidth - Left - Right);
    double PlotH => Math.Max(10, Plot.ActualHeight - Top - Bottom);
    double X(double t) => Left + (t - MinT) / (MaxT - MinT) * PlotW;
    double Y(double p) => Top + (1 - p / 100) * PlotH;

    void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Rebuild();

    void OnGroupChecked(object sender, RoutedEventArgs e)
    {
        if (CaseGroup is null || CpuGroup is null) return; // fires during InitializeComponent
        _group = CaseGroup.IsChecked == true ? CurveGroup.Case : CurveGroup.Cpu;
        _commit.Stop();
        _holdUntil = DateTime.MinValue;
        _invalid = false;
        _shown = Wanted();
        _selected = -1;
        Problem.Text = "";
        Rebuild();
        UpdateTexts();
        UpdateMarker();
    }

    // ---- drawing ----

    void Rebuild()
    {
        if (Plot.ActualWidth < 60) return;
        Plot.Children.Clear();
        _thumbs.Clear();
        var muted = (Brush)FindResource("MutedBrush");
        var grid = (Brush)FindResource("PanelBorderBrush");

        for (var t = (int)MinT; t <= MaxT; t += 10)
        {
            Plot.Children.Add(new Line { X1 = X(t), X2 = X(t), Y1 = Y(0), Y2 = Y(100), Stroke = grid, StrokeThickness = 1 });
            if ((t - (int)MinT) % 20 == 0) Label($"{t}°", X(t) - 10, Y(0) + 4, muted);
        }
        for (var p = 0; p <= 100; p += 25)
        {
            Plot.Children.Add(new Line { X1 = X(MinT), X2 = X(MaxT), Y1 = Y(p), Y2 = Y(p), Stroke = grid, StrokeThickness = 1 });
            Label($"{p}%", 2, Y(p) - 9, muted);
        }

        _line = new Polyline { Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        Plot.Children.Add(_line);

        _marker = new Line { Y1 = Y(0), Y2 = Y(100), Stroke = (Brush)FindResource("GpuBrush"), StrokeThickness = 2, StrokeDashArray = [4, 3], Visibility = Visibility.Collapsed };
        Plot.Children.Add(_marker);
        _markerLabel = Label("", 0, Top, (Brush)FindResource("GpuBrush"));
        _markerLabel.Visibility = Visibility.Collapsed;

        if (_editable)
        {
            for (var i = 0; i < _shown.Count; i++)
            {
                var thumb = new Thumb { Style = (Style)FindResource("PointThumb"), Tag = i };
                thumb.DragStarted += OnDragStarted;
                thumb.DragDelta += OnDragDelta;
                thumb.DragCompleted += OnDragCompleted;
                thumb.GotKeyboardFocus += (_, _) => { _selected = (int)thumb.Tag; UpdateTexts(); };
                thumb.PreviewKeyDown += OnThumbKey;
                Plot.Children.Add(thumb);
                _thumbs.Add(thumb);
            }
        }
        PositionAll();
    }

    TextBlock Label(string text, double x, double y, Brush brush)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = 11, IsHitTestVisible = false };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        Plot.Children.Add(tb);
        return tb;
    }

    void PositionAll()
    {
        if (_line is null) return;
        var pts = new PointCollection { new Point(X(MinT), Y(_shown[0].Percent)) };
        foreach (var p in _shown) pts.Add(new Point(X(p.TempC), Y(p.Percent)));
        pts.Add(new Point(X(MaxT), Y(_shown[^1].Percent)));
        _line.Points = pts;
        for (var i = 0; i < _thumbs.Count && i < _shown.Count; i++)
        {
            Canvas.SetLeft(_thumbs[i], X(_shown[i].TempC) - 9);
            Canvas.SetTop(_thumbs[i], Y(_shown[i].Percent) - 9);
            AutomationProperties.SetName(_thumbs[i], $"Point {i + 1} of {_shown.Count}: {_shown[i].TempC:0} °C, {_shown[i].Percent:0} percent");
        }
        AutomationProperties.SetName(Plot, $"{GroupName} fan curve. " + string.Join(", ", _shown.Select(p => $"{p.TempC:0} °C {p.Percent:0}%")));
    }

    string GroupName => _group == CurveGroup.Cpu ? "CPU" : "System";

    void UpdateMarker()
    {
        if (_marker is null || _markerLabel is null) return;
        if (CurrentTemp is not { } t || _shown.Count < 2 || t < MinT || t > MaxT)
        {
            _marker.Visibility = _markerLabel.Visibility = Visibility.Collapsed;
            return;
        }
        _marker.X1 = _marker.X2 = X(t);
        _marker.Visibility = _markerLabel.Visibility = Visibility.Visible;
        _markerLabel.Text = $"now {t:0}°";
        Canvas.SetLeft(_markerLabel, Math.Min(X(t) + 4, Plot.ActualWidth - 60));
    }

    void UpdateTexts()
    {
        var what = _group == CurveGroup.Cpu ? "the CPU fan" : "the system fans";
        Hint.Text = _editable
            ? $"Manual curve for {what}. Drag a point, or select one and use the arrow keys (Shift moves by 5). Fans never go below 30% (CPU) or 20% (system), and the curve must reach 60% by 80 °C and 80% by 85 °C."
            : $"The {_mode} curve for {what}. Choose Manual above to change it, or copy it with the button below.";
        if (_group == CurveGroup.Cpu) Hint.Text += " The CPU fan also ramps up early above 70 °C, whatever the curve says.";
        // Points that break the shape rules (mid-edit) cannot be evaluated; the hot-end rules do not matter for a number.
        var curve = _shown.Count >= 2 ? TryCurve(_shown) : null;
        NowText.Text = curve is not null && CurrentTemp is { } t
            ? $"Now: {t:0} °C, so the curve asks for {curve.Evaluate(t):0}%."
            : "No temperature reading yet.";
        Readout.Text = _editable && _selected >= 0 && _selected < _shown.Count
            ? $"Point {_selected + 1} of {_shown.Count}: {_shown[_selected].TempC:0} °C → {_shown[_selected].Percent:0}%"
            : "";
        AddButton.Visibility = RemoveButton.Visibility = ResetButton.Visibility = _editable ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = _editable ? Visibility.Collapsed : Visibility.Visible;
        AddButton.IsEnabled = _shown.Count < 10 && FindGap() >= 0;
        RemoveButton.IsEnabled = _shown.Count > 2 && _selected >= 0;
        CopyButton.IsEnabled = _mode != FanMode.Ai;
    }

    static FanCurve? TryCurve(IReadOnlyList<CurvePoint> points)
    {
        try { return new FanCurve(points); }
        catch (ArgumentException) { return null; }
    }

    // ---- editing ----

    void OnDragStarted(object sender, DragStartedEventArgs e)
    {
        var i = (int)((Thumb)sender).Tag;
        _dragging = true;
        _selected = i;
        _dragT = _shown[i].TempC;
        _dragP = _shown[i].Percent;
        _accX = _accY = 0;
    }

    void OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var i = (int)((Thumb)sender).Tag;
        _accX += e.HorizontalChange;
        _accY += e.VerticalChange;
        MovePoint(i, _dragT + _accX / PlotW * (MaxT - MinT), _dragP - _accY / PlotH * 100);
    }

    void OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        Touched();
    }

    void OnThumbKey(object sender, KeyEventArgs e)
    {
        var i = (int)((Thumb)sender).Tag;
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
        var (dt, dp) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, step),
            Key.Down => (0, -step),
            _ => (0, 0),
        };
        if (e.Key == Key.Delete) { e.Handled = true; Remove(i); return; }
        if (dt == 0 && dp == 0) return;
        e.Handled = true;
        MovePoint(i, _shown[i].TempC + dt, _shown[i].Percent + dp);
        Touched();
    }

    /// <summary>Moves one point, keeping it between its neighbours so the curve stays in order.</summary>
    void MovePoint(int i, double t, double p)
    {
        var loT = i > 0 ? _shown[i - 1].TempC + 1 : MinT;
        var hiT = i < _shown.Count - 1 ? _shown[i + 1].TempC - 1 : MaxT;
        var loP = i > 0 ? _shown[i - 1].Percent : 0;
        var hiP = i < _shown.Count - 1 ? _shown[i + 1].Percent : 100;
        _shown[i] = new CurvePoint(Math.Clamp(Math.Round(t), loT, hiT), Math.Clamp(Math.Round(p), loP, hiP));
        PositionAll();
        UpdateTexts();
        UpdateMarker();
    }

    int FindGap()
    {
        var best = -1;
        var width = 1.0;
        for (var i = 0; i + 1 < _shown.Count; i++)
        {
            var gap = _shown[i + 1].TempC - _shown[i].TempC;
            if (gap > width + 1 || (gap >= 2 && best < 0)) { width = gap; best = i; }
        }
        return best;
    }

    void OnAdd(object sender, RoutedEventArgs e)
    {
        var i = FindGap();
        if (_shown.Count >= 10 || i < 0) return;
        var a = _shown[i];
        var b = _shown[i + 1];
        _shown.Insert(i + 1, new CurvePoint(Math.Round((a.TempC + b.TempC) / 2), Math.Round((a.Percent + b.Percent) / 2)));
        _selected = i + 1;
        Rebuild();
        Touched();
        if (_selected < _thumbs.Count) _thumbs[_selected].Focus();
    }

    void OnRemove(object sender, RoutedEventArgs e) => Remove(_selected);

    void Remove(int i)
    {
        if (_shown.Count <= 2 || i < 0 || i >= _shown.Count) return;
        _shown.RemoveAt(i);
        _selected = Math.Min(i, _shown.Count - 1);
        Rebuild();
        Touched();
        if (_selected < _thumbs.Count) _thumbs[_selected].Focus();
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        _shown = ModePresets.Get(FanMode.Normal).Points.ToList();
        _selected = -1;
        Rebuild();
        Touched();
    }

    void OnCopy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke(_shown.ToList());

    /// <summary>A local change was made: show what is wrong with it, or send it after a short pause.</summary>
    void Touched()
    {
        var why = ManualCurveRules.Check(_shown);
        _invalid = why is not null;
        Problem.Text = why is null ? "" : "Not sent yet: " + why + ".";
        _commit.Stop();
        if (!_invalid) _commit.Start();
        _holdUntil = DateTime.UtcNow + HoldFor;
        UpdateTexts();
    }

    void Commit()
    {
        _commit.Stop();
        if (ManualCurveRules.Check(_shown) is not null) return;
        _holdUntil = DateTime.UtcNow + HoldFor;
        Committed?.Invoke(_group, _shown.ToList());
    }

    /// <summary>The service refused the curve (or could not be reached): say so and go back to what it has.</summary>
    public void ReportFailure(string message)
    {
        Problem.Text = message;
        _holdUntil = DateTime.MinValue;
        _invalid = false;
    }
}
