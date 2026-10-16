using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App.Tests;

/// <summary>A test that opens the real window. It runs only when EMBER_UI_TESTS=1, because it needs a desktop session.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("EMBER_UI_TESTS") != "1")
            Skip = "Set EMBER_UI_TESTS=1 to run the window tests (they open the real window off screen, on a scratch folder, with no devices, network or service).";
    }
}

/// <summary>
/// Runs Ember's own window on a thread of its own, as a simulation: its files are in a scratch folder (EMBER_DATA_DIR), nothing talks to
/// a device, the network or the Ember service (EMBER_SIMULATE=1), and the readings are made up here. The window sits off screen.
/// </summary>
static class UiHost
{
    static Dispatcher? _dispatcher;
    static MainWindow? _window;
    public static readonly List<Exception> Errors = [];
    public static string DataDir { get; private set; } = "";

    public static MainWindow Window { get { _ = Started.Value; return _window!; } }

    static readonly Lazy<bool> Started = new(Start);

    // Not a static constructor: the window thread uses this class, and would wait for the constructor to finish.
    static bool Start()
    {
        DataDir = Path.Combine(Path.GetTempPath(), "ember-ui-test");
        try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true); } catch (IOException) { }
        Directory.CreateDirectory(DataDir);
        Environment.SetEnvironmentVariable("EMBER_DATA_DIR", DataDir);
        Environment.SetEnvironmentVariable("EMBER_SIMULATE", "1");
        Directory.CreateDirectory(Path.Combine(DataDir, "Ember"));
        File.WriteAllText(Path.Combine(DataDir, "Ember", "app.json"), "{\"AcceptedTerms\":" + Legal.TermsVersion + ",\"Channel\":\"Beta\",\"AutoCheckUpdates\":false}");

        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _dispatcher.UnhandledException += (_, e) => { lock (Errors) Errors.Add(e.Exception); e.Handled = true; };
            var app = new global::Ember.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            typeof(global::Ember.App.App).GetMethod("UseNewLook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(app, null);
            _window = new MainWindow { Left = -3000, Top = 40, Width = 1280, Height = 800, ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
            _window.Closing += (_, e) => File.AppendAllText(Path.Combine(Path.GetTempPath(), "events.txt"), "closing " + Environment.StackTrace + Environment.NewLine);
            _window.Closed += (_, _) => File.AppendAllText(Path.Combine(Path.GetTempPath(), "events.txt"), "closed" + Environment.NewLine);
            var handle = new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "handle.txt"), handle.ToString());
            _window.Show();
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true, Name = "Ember UI test thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(60))) throw new TimeoutException("The window did not open in a minute.");
        return true;
    }

    public static void Run(Action action) => Disp().Invoke(action);
    public static T Run<T>(Func<T> func) => Disp().Invoke(func);

    static Dispatcher Disp() { _ = Started.Value; return _dispatcher!; }

    /// <summary>Lets everything queued (layout, bindings, timers that are due) happen before the next step.</summary>
    public static void Settle()
    {
        for (var i = 0; i < 3; i++) Disp().Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Disp().Invoke(() => Window.UpdateLayout());
    }

    /// <summary>Feeds a reading as the service would send it.</summary>
    public static void Feed(double cpuTemp = 58, double gpuTemp = 49, double cpuLoad = 21, double gpuLoad = 14, double cpuW = 70, double gpuW = 26, bool stale = false)
    {
        var readings = new List<SensorReading>
        {
            new("cpu-t", "CPU Package", HardwareKind.Cpu, SensorKind.Temperature, cpuTemp),
            new("cpu-l", "CPU Total", HardwareKind.Cpu, SensorKind.Load, cpuLoad),
            new("cpu-c", "CPU Core #1", HardwareKind.Cpu, SensorKind.Clock, 4600),
            new("cpu-p", "CPU Package", HardwareKind.Cpu, SensorKind.Power, cpuW),
            new("gpu-t", "GPU Core", HardwareKind.Gpu, SensorKind.Temperature, gpuTemp),
            new("gpu-l", "GPU Core", HardwareKind.Gpu, SensorKind.Load, gpuLoad),
            new("gpu-c", "GPU Core", HardwareKind.Gpu, SensorKind.Clock, 1800),
            new("gpu-p", "GPU Package", HardwareKind.Gpu, SensorKind.Power, gpuW),
            new("mb-t", "System", HardwareKind.Motherboard, SensorKind.Temperature, 36),
            new("f1", "Fan #1", HardwareKind.Motherboard, SensorKind.FanRpm, 880),
            new("f2", "Fan #2", HardwareKind.Motherboard, SensorKind.FanRpm, 760),
            new("f3", "Fan #3", HardwareKind.Motherboard, SensorKind.FanRpm, 1210),
            new("f4", "Fan #4", HardwareKind.Motherboard, SensorKind.FanRpm, 1180),
        };
        var control = new FanControlStatus(FanMode.Normal, ModeSource.User, false, null,
            [new HeaderStatus("h1", "CPU fan", HeaderState.Enabled, true), new HeaderStatus("h2", "System fan 1", HeaderState.Enabled, true), new HeaderStatus("h3", "System fan 2", HeaderState.Controllable, false)],
            [], null);
        var snapshot = new Snapshot(DateTimeOffset.UtcNow, readings, stale, DriverStatus.Ok, control);
        Run(() => { Window.SimulateConnected(); Window.HandleSnapshot(snapshot); Window.Refresh(); });
    }

    /// <summary>Made-up games and play dates, so the Games page has something to show.</summary>
    public static void FeedGames()
    {
        var games = new List<LibraryGame>
        {
            new("Starfall Tactics", "Steam", "C:/Games/Starfall", ["starfall.exe"], "1"),
            new("Harbour Lights", "Steam", "C:/Games/Harbour", ["harbour.exe"], "2"),
            new("Iron Meridian", "Steam", "C:/Games/Meridian", ["meridian.exe"], "3"),
            new("Mossgrove", "Steam", "C:/Games/Moss", ["moss.exe"], "4"),
            new("Nightwire Racing", "Epic Games", "C:/Games/Nightwire", ["nightwire.exe"]),
            new("Cinder Keep", "Epic Games", "C:/Games/Cinder", ["cinder.exe"]),
            new("Paper Skies", "GOG", "C:/Games/Skies", ["skies.exe"]),
            new("Tidewatch", "EA app", "C:/Games/Tidewatch", ["tide.exe"]),
        };
        var history = new PlayHistory();
        history.Played("Starfall Tactics", DateTimeOffset.Now.AddDays(-2), 3600);
        history.Played("Harbour Lights", DateTimeOffset.Now.AddDays(-8), 3600);
        history.Played("Iron Meridian", DateTimeOffset.Now.AddDays(-21), 3600);
        history.Played("Nightwire Racing", DateTimeOffset.Now.AddDays(-1), 3600);
        history.Played("Cinder Keep", DateTimeOffset.Now.AddDays(-40), 3600);
        Run(() => Window.SimulateGames(new GameLibrary(DateTimeOffset.Now, games), history));
    }

    /// <summary>Saves what the window shows as a picture when EMBER_UI_SHOTS names a folder.</summary>
    public static void Shot(string name)
    {
        if (Environment.GetEnvironmentVariable("EMBER_UI_SHOTS") is not { Length: > 0 } dir) return;
        Settle();
        Run(() =>
        {
            Directory.CreateDirectory(dir);
            var w = Window;
            w.UpdateLayout();
            var content = (FrameworkElement)w.Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual();
            using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(w.Background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
            bitmap.Render(backdrop);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(dir, name + ".png"));
            encoder.Save(file);
        });
    }

    public static void ThrowIfErrors()
    {
        lock (Errors)
            if (Errors.Count > 0) throw new AggregateException("The window raised errors while it ran", Errors.ToList());
    }
}
