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
    string? _announcedConnection, _announcedBanner;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // ServiceConnection raises events on its background loop thread: queue each one onto the UI thread.
        // InvokeAsync is the non-blocking BeginInvoke, so the loop never waits for the UI.
        var connection = new ServiceConnection(PipeStreamConnector.ConnectAsync, ServiceConnection.DefaultBackoff);
        connection.StateChanged += s => Dispatcher.InvokeAsync(() => _vm.SetConnection(s));
        connection.SnapshotReceived += s => Dispatcher.InvokeAsync(() => _vm.Add(s));

        // Throttle: redraw at most twice a second, whatever the snapshot rate.
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            (_, _) => Refresh(), Dispatcher);
        _refresh.Start();

        _run = Task.Run(() => connection.RunAsync(_cts.Token));
        Closed += OnClosed;
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
    }

    static void Announce(UIElement element)
    {
        if (!AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        UIElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    void OnClosed(object? sender, EventArgs e)
    {
        _refresh.Stop();
        _cts.Cancel();
        // RunAsync returns on cancellation and never needs the UI thread, so a short wait cannot deadlock.
        _run.Wait(TimeSpan.FromSeconds(2));
    }
}
