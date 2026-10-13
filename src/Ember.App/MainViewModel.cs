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

    readonly PreferencesStore _prefsStore = new(PreferencesStore.DefaultPath);

    public MainViewModel() => Prefs = _prefsStore.Load();

    public AppPreferences Prefs { get; private set; }

    public TemperatureUnit Unit => Prefs.Unit;

    public bool UseFahrenheit => Unit == TemperatureUnit.Fahrenheit;
    public bool UseCelsius => Unit == TemperatureUnit.Celsius;
    public bool StartOnFans => Prefs.StartPage == StartPage.Fans;
    public bool StartOnDashboard => Prefs.StartPage == StartPage.Dashboard;

    /// <summary>Changes and remembers a preference. False if it could not be saved (it still applies until the app closes).</summary>
    public bool Update(AppPreferences prefs)
    {
        Prefs = prefs;
        _dirty = true;
        var saved = _prefsStore.TrySave(prefs);
        Refresh(DateTimeOffset.UtcNow);
        return saved;
    }

    public string ChartTitle => Unit == TemperatureUnit.Fahrenheit
        ? "Temperature history (last 10 minutes, 68 to 212 °F)"
        : "Temperature history (last 10 minutes, 20 to 100 °C)";
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
    FansView _fansPage = FansViewState.Render(null, false);
    public FansView FansPage => _fansPage;
    public Visibility FansMessageVisibility => _fansPage.Message is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility CompetitorVisibility => _fansPage.CompetitorWarning is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SafetyVisibility => _fansPage.SafetyNote is null ? Visibility.Collapsed : Visibility.Visible;
    public bool IsElevated { get; } = CompetitorCloser.IsElevated();
    public Visibility NotElevatedVisibility => IsElevated ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BetaGamesVisibility => BuildFeatures.Current.GameDetection && _fansPage.Experimental ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ExperimentalAvailableVisibility => BuildFeatures.Current.GameDetection || BuildFeatures.Current.GpuFan ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BetaGpuVisibility => BuildFeatures.Current.GpuFan && _fansPage.Experimental ? Visibility.Visible : Visibility.Collapsed;
    public string ModeChipText => "Mode: " + FansViewState.Label(_fansPage.Mode);
    public string CpuFanText => Fans.FirstOrDefault(f => f.Name.StartsWith("CPU Fan", StringComparison.OrdinalIgnoreCase))?.RpmText ?? "—";
    public string SystemFanText { get { var s = Fans.Where(f => f.Name.StartsWith("System Fan", StringComparison.OrdinalIgnoreCase) && f.Fraction > 0).ToList(); return s.Count == 0 ? "—" : s.Count + (s.Count == 1 ? " fan" : " fans") + " spinning"; } }
    public string BuildLabel => BuildFeatures.Channel == "beta" ? "Beta build" : "Alpha build";
    public Visibility DetectedGameVisibility => _fansPage.DetectedGame is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility NotGamesVisibility => _fansPage.NotGames is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoHeadersVisibility => _fansPage.ServiceAvailable && _fansPage.Headers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        var fansView = FansViewState.Render(v.Control, _connected);
        if (!SameFans(_fansPage, fansView)) _fansPage = fansView; // keep the old instance when nothing changed, so focus is not lost
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

    static bool SameFans(FansView a, FansView b) =>
        a.ServiceAvailable == b.ServiceAvailable && a.Message == b.Message && a.SourceText == b.SourceText
        && a.CompetitorWarning == b.CompetitorWarning && a.SafetyNote == b.SafetyNote && a.TestMessage == b.TestMessage
        && a.Modes.SequenceEqual(b.Modes) && a.Headers.SequenceEqual(b.Headers)
        && a.Experimental == b.Experimental && a.Mode == b.Mode && a.Curves.Equals(b.Curves) && a.GpuControl == b.GpuControl && a.Competitors.SequenceEqual(b.Competitors) && a.DetectedGame == b.DetectedGame && (a.NotGames ?? []).SequenceEqual(b.NotGames ?? []);

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
