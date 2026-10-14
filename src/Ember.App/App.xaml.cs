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

    protected override void OnStartup(StartupEventArgs e)
    {
        // "Uninstall" in Windows Settings starts the program with this switch: no window, just the removal.
        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Uninstaller.Run();
            Shutdown();
            return;
        }
        DiagLog.Prune();
        DispatcherUnhandledException += (_, args) => DiagLog.Error("App", "Unhandled error on the window thread", args.Exception);
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
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        _instance.ListenForShow(() => window.Dispatcher.InvokeAsync(window.ShowFromTray));
        // Started at sign-in with --minimised: stay in the tray, unless the terms still need an answer.
        if (LaunchOptions.Parse(e.Args).Minimised && window.CanStayHidden) return;
        window.Show();
    }
}
