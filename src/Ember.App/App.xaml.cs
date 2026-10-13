using System.Windows;
using Ember.Core;

namespace Ember.App;

public partial class App : Application
{
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
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        // Started at sign-in with --minimised: stay in the tray, unless the terms still need an answer.
        if (LaunchOptions.Parse(e.Args).Minimised && window.CanStayHidden) return;
        window.Show();
    }
}
