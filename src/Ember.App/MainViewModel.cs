using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Thin WPF wrapper over <see cref="DashboardState"/>. UI thread only: the window marshals
/// connection events onto the dispatcher before calling in.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    static readonly Brush Running = Frozen(Color.FromRgb(0x5c, 0xc8, 0x7a));
    static readonly Brush NotRunning = Frozen(Color.FromRgb(0x9c, 0x91, 0x89));

    readonly DashboardState _state = new();
    bool _dirty = true;
    bool _connected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;
    public double ChartWidth { get; private set; }
    public double ChartHeight { get; private set; }

    public string ConnectionText { get; private set; } = "Connecting…";
    public Brush ConnectionBrush => _connected ? Running : NotRunning;
    public string CpuText { get; private set; } = "—";
    public string GpuText { get; private set; } = "—";
    public string MotherboardText { get; private set; } = "—";
    public string FanAverageText { get; private set; } = "—";
    public ObservableCollection<FanBar> Fans { get; } = new();
    public PointCollection CpuLine { get; private set; } = new();
    public PointCollection GpuLine { get; private set; } = new();
    public string? DriverBannerText { get; private set; }
    public Visibility DriverBannerVisibility => DriverBannerText is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StaleNoteVisibility { get; private set; } = Visibility.Collapsed;
    public Visibility NoFansVisibility => Fans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public void SetConnection(ConnectionState state)
    {
        _state.SetConnection(state);
        _connected = state == ConnectionState.Connected;
        _dirty = true;
    }

    public void Add(Snapshot snapshot)
    {
        _state.Add(snapshot);
        _dirty = true;
    }

    public void SetChartSize(double width, double height)
    {
        ChartWidth = width;
        ChartHeight = height;
        _dirty = true;
    }

    /// <summary>Called by a timer, so the UI redraws at most at the timer rate however fast snapshots come.</summary>
    public void Refresh(DateTimeOffset now)
    {
        if (!_dirty) return;
        _dirty = false;
        var v = _state.Render(now, ChartWidth, ChartHeight, Unit);
        ConnectionText = v.ConnectionText;
        CpuText = v.CpuText;
        GpuText = v.GpuText;
        MotherboardText = v.MotherboardText;
        FanAverageText = v.FanAverageText;
        DriverBannerText = v.DriverBannerText;
        StaleNoteVisibility = v.ShowStaleNote ? Visibility.Visible : Visibility.Collapsed;
        CpuLine = Points(v.CpuLine);
        GpuLine = Points(v.GpuLine);
        if (!Fans.SequenceEqual(v.Fans))
        {
            Fans.Clear();
            foreach (var f in v.Fans) Fans.Add(f);
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // every property
    }

    static PointCollection Points(IReadOnlyList<(double X, double Y)> line)
    {
        var points = new PointCollection(line.Select(p => new Point(p.X, p.Y)));
        points.Freeze();
        return points;
    }

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
