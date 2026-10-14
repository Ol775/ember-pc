using System.Windows;
using Ember.Core;

namespace Ember.App;

public partial class App : Application
{
    SingleInstance? _instance;

    protected override void OnExit(ExitEventArgs e)
    {
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
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
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
