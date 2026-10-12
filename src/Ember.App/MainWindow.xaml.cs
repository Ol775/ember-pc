using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly CancellationTokenSource _cts = new();
    readonly DispatcherTimer _refresh;
    readonly Task _run;
    readonly GameDetection _detection = new([]);
    readonly DispatcherTimer _gameTimer;
    double? _gpuLoad;
    string? _announcedConnection, _announcedBanner, _announcedCompetitor, _announcedTest;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // ServiceConnection raises events on its background loop thread: queue each one onto the UI thread.
        // InvokeAsync is the non-blocking BeginInvoke, so the loop never waits for the UI.
        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.StateChanged += s => Dispatcher.InvokeAsync(() => _vm.SetConnection(s));
        connection.SnapshotReceived += s => Dispatcher.InvokeAsync(() =>
        {
            _vm.Add(s);
            _gpuLoad = s.Readings.FirstOrDefault(r => r.Hardware == HardwareKind.Gpu && r.Kind == SensorKind.Load && r.Name == "GPU Core")?.Value;
            if (s.Control is { } c) _detection.SetNotGames(c.NotGames);
        });

        // Throttle: redraw at most twice a second, whatever the snapshot rate.
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher);
        _refresh.Start();

        // Game detection: once a second look at the foreground program and report start, end and heartbeat to the service.
        _gameTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => DetectGame(), Dispatcher);
        _gameTimer.Start();

        _run = Task.Run(() => connection.RunAsync(_cts.Token));
        Closed += OnClosed;
    }

    async void DetectGame()
    {
        try
        {
            ControlMessage? message = _detection.Update(DateTimeOffset.UtcNow, GameDetectorWindows.Foreground(), _gpuLoad) switch
            {
                GameEvent.Started s => new GameStarted(s.Exe),
                GameEvent.Ended => new GameEnded(),
                GameEvent.Heartbeat => new DetectorHeartbeat(),
                _ => null,
            };
            if (message is not null) await ControlClient.SendAsync(message);
        }
        catch (Exception) { /* detection must never take the window down */ }
    }

    async void OnNotAGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string exe }) await SendAsync(new SetNotGame(exe, true));
    }

    async void OnUndoNotAGame(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string exe }) await SendAsync(new SetNotGame(exe, false));
    }

    void OnNavChanged(object sender, RoutedEventArgs e)
    {
        if (DashboardPage is null || FansPageView is null) return; // fires once during InitializeComponent
        var fans = NavFans.IsChecked == true;
        DashboardPage.Visibility = fans ? Visibility.Collapsed : Visibility.Visible;
        FansPageView.Visibility = fans ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnModePicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FanMode mode }) await SendAsync(new SetMode(mode));
    }

    async void OnTestHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new RunHeaderTest(id));
    }

    async void OnEnableHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new ConfirmHeader(id, true));
    }

    async void OnDisableHeader(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) await SendAsync(new ConfirmHeader(id, false));
    }

    async Task SendAsync(ControlMessage message)
    {
        var reply = await ControlClient.SendAsync(message);
        ActionLabel.Text = reply.Ok ? "" : reply.Error ?? "That did not work.";
        Announce(ActionLabel);
    }

    void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _vm.SetChartSize(e.NewSize.Width, e.NewSize.Height);
        Refresh();
    }

    void Refresh()
    {
        _vm.Refresh(DateTimeOffset.UtcNow);
        // WPF only announces a live region when the app raises LiveRegionChanged itself.
        if (_vm.ConnectionText != _announcedConnection)
        {
            _announcedConnection = _vm.ConnectionText;
            Announce(ConnectionLabel);
        }
        if (_vm.DriverBannerText != _announcedBanner)
        {
            _announcedBanner = _vm.DriverBannerText;
            if (_announcedBanner is not null) Announce(BannerLabel);
        }
        if (_vm.FansPage.CompetitorWarning != _announcedCompetitor)
        {
            _announcedCompetitor = _vm.FansPage.CompetitorWarning;
            if (_announcedCompetitor is not null) Announce(CompetitorLabel);
        }
        if (_vm.FansPage.TestMessage != _announcedTest)
        {
            _announcedTest = _vm.FansPage.TestMessage;
            if (_announcedTest is not null) Announce(TestLabel);
        }
    }

    static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    void OnClosed(object? sender, EventArgs e)
    {
        _refresh.Stop();
        _gameTimer.Stop();
        _cts.Cancel();
        // RunAsync returns on cancellation and never needs the UI thread, so a short wait cannot deadlock.
        _run.Wait(TimeSpan.FromSeconds(2));
    }
}
