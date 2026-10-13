using System.Windows;

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
    }
}
