using System.Windows;
using Ember.Core;

namespace Ember.App;

public partial class App : Application
{
    SingleInstance? _instance;

    protected override void OnExit(ExitEventArgs e)
    {
        DiagLog.Info("App", "Ember closed, exit code " + e.ApplicationExitCode);
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Swaps the redesigned styles in for the old ones before any window asks for them.</summary>
    void UseNewLook()
    {
        try
        {
            var look = new ResourceDictionary { Source = new Uri("/Ember.App;component/NewLook.xaml", UriKind.Relative) };
            foreach (var key in look.Keys.Cast<object>().Where(Resources.Contains).ToList()) Resources.Remove(key);
            Resources.MergedDictionaries.Add(look);
        }
        catch (Exception ex) { DiagLog.Warn("App", "The new look could not be loaded; using the classic look", ex); }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // A simulation (the window tests) opens its own window; this start-up would find the running Ember and close the test.
        if (AppData.Simulated) { base.OnStartup(e); return; }
        // "Uninstall" in Windows Settings starts the program with this switch: no window, just the removal.
        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Uninstaller.Run();
            Shutdown();
            return;
        }
        DiagLog.Prune();
        // A failed click, timer tick or async handler is logged and the window carries on; only running out of memory ends the app.
        DispatcherUnhandledException += (_, args) =>
        {
            DiagLog.Error("App", "Unhandled error on the window thread", args.Exception);
            args.Handled = args.Exception is not OutOfMemoryException;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagLog.Error("App", "Unhandled error (ending: " + args.IsTerminating + ")", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => DiagLog.Warn("App", "Unobserved task error", args.Exception);
        DiagLog.Info("App", $"Starting Ember {AppInfo.Version}, Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, " +
                            (CompetitorCloser.IsElevated() ? "administrator" : "standard user") + ", args: " + string.Join(' ', e.Args));
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            DiagLog.Info("App", "Another Ember is already running: asked it to show itself and quit");
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown();
            return;
        }
        if (BuildFeatures.NewLook) UseNewLook();
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        _instance.ListenForShow(() => window.Dispatcher.InvokeAsync(window.ShowFromTray));
        // Started at sign-in with --minimised: stay in the tray, unless the terms still need an answer.
        if (LaunchOptions.Parse(e.Args).Minimised && window.CanStayHidden) return;
        window.Show();
    }
}
