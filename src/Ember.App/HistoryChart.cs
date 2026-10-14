using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// What is drawn around the two temperature lines of the Dashboard: faint gridlines every 20 degrees with their values, minute
/// marks along the bottom, and at the right end of each line a dot and a label with the temperature right now.
/// </summary>
sealed class HistoryChart(Canvas canvas)
{
    readonly List<UIElement> _static = [], _live = [];
    double _w, _h;
    TemperatureUnit _unit;
    bool _stale = true;

    /// <summary>Redraw the gridlines (the window changed size, or the unit or theme changed).</summary>
    public void Invalidate() => _stale = true;

    public void Update(double width, double height, TemperatureUnit unit, PointCollection cpu, PointCollection gpu, string cpuText, string gpuText)
    {
        if (width < 80 || height < 60) return;
        if (_stale || width != _w || height != _h || unit != _unit)
        {
            (_w, _h, _unit, _stale) = (width, height, unit, false);
            DrawStatic();
        }
        DrawLive(cpu, gpu, cpuText, gpuText);
    }

    double Y(double celsius) => (DashboardState.ChartMaxCelsius - celsius) / (DashboardState.ChartMaxCelsius - DashboardState.ChartMinCelsius) * _h;

    void DrawStatic()
    {
        foreach (var e in _static) canvas.Children.Remove(e);
        _static.Clear();
        var grid = (Brush)Application.Current.FindResource("PanelBorderBrush");
        var muted = (Brush)Application.Current.FindResource("MutedBrush");
        var at = 0; // drawn behind the temperature lines
        void Add(UIElement e) { canvas.Children.Insert(at++, e); _static.Add(e); }

        for (var c = 20; c <= 100; c += 20)
        {
            var y = Y(c);
            Add(new Line { X1 = 0, X2 = _w, Y1 = y, Y2 = y, Stroke = grid, StrokeThickness = 1, IsHitTestVisible = false });
            var label = Text(Temperature.Convert(c, _unit).ToString("0") + "°", muted);
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, c == 100 ? y + 2 : y - 15);
            Add(label);
        }
        for (var m = 2; m <= 8; m += 2)
        {
            var x = _w * (1 - m / 10.0);
            Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = _h, Stroke = grid, StrokeThickness = 1, StrokeDashArray = [2, 4], Opacity = 0.7, IsHitTestVisible = false });
            var label = Text($"{m} min ago", muted);
            Canvas.SetLeft(label, x + 4);
            Canvas.SetTop(label, _h - 16);
            Add(label);
        }
    }

    void DrawLive(PointCollection cpu, PointCollection gpu, string cpuText, string gpuText)
    {
        foreach (var e in _live) canvas.Children.Remove(e);
        _live.Clear();
        var items = new List<(Point P, string Text, Brush Brush)>();
        if (cpu.Count > 0) items.Add((cpu[^1], cpuText, (Brush)Application.Current.FindResource("AccentBrush")));
        if (gpu.Count > 0) items.Add((gpu[^1], gpuText, (Brush)Application.Current.FindResource("GpuBrush")));
        var panel = (Brush)Application.Current.FindResource("PanelBrush");
        var lastTop = double.NegativeInfinity;
        foreach (var (p, text, brush) in items.OrderBy(i => i.P.Y))
        {
            var dot = new Ellipse { Width = 11, Height = 11, Fill = brush, Stroke = panel, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(dot, Math.Min(p.X, _w - 6) - 5.5);
            Canvas.SetTop(dot, p.Y - 5.5);
            var label = new TextBlock { Text = text, Foreground = (Brush)Application.Current.FindResource("TextBrush"), FontSize = 12, FontWeight = FontWeights.SemiBold };
            var pill = new Border { Child = label, Background = panel, BorderBrush = brush, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), IsHitTestVisible = false };
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // left of the dot, kept inside the chart, and never on top of the other pill
            var top = Math.Clamp(p.Y - pill.DesiredSize.Height / 2, 2, _h - pill.DesiredSize.Height - 2);
            if (top < lastTop + pill.DesiredSize.Height + 3) top = Math.Min(lastTop + pill.DesiredSize.Height + 3, _h - pill.DesiredSize.Height - 2);
            lastTop = top;
            Canvas.SetLeft(pill, Math.Max(2, Math.Min(p.X, _w) - pill.DesiredSize.Width - 12));
            Canvas.SetTop(pill, top);
            canvas.Children.Add(dot); canvas.Children.Add(pill);
            _live.Add(dot); _live.Add(pill);
        }
    }

    static TextBlock Text(string text, Brush brush) => new() { Text = text, Foreground = brush, FontSize = 11, IsHitTestVisible = false };
}
