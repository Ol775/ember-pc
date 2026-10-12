using System.Windows;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();
    readonly CancellationTokenSource _cts = new();
    readonly DispatcherTimer _refresh;
    readonly Task _run;

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
            (_, _) => _vm.Refresh(DateTimeOffset.UtcNow), Dispatcher);
        _refresh.Start();

        _run = Task.Run(() => connection.RunAsync(_cts.Token));
        Closed += OnClosed;
    }

    void OnChartSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _vm.SetChartSize(e.NewSize.Width, e.NewSize.Height);
        _vm.Refresh(DateTimeOffset.UtcNow);
    }

    void OnClosed(object? sender, EventArgs e)
    {
        _refresh.Stop();
        _cts.Cancel();
        // RunAsync returns on cancellation and never needs the UI thread, so a short wait cannot deadlock.
        _run.Wait(TimeSpan.FromSeconds(2));
    }
}
