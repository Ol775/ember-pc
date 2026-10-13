using System;
using System.IO;
using System.Windows;

namespace Ember.Setup
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            var options = SetupOptions.Parse(args);
            if (options.Silent) return RunSilent(options);
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            return app.Run(new SetupWindow(options));
        }

        static int RunSilent(SetupOptions options)
        {
            var logPath = Path.Combine(Path.GetTempPath(), "EmberSetup.log");
            using (var log = new StreamWriter(logPath, false))
            {
                void Write(string line) { lock (log) { log.WriteLine(line); log.Flush(); } }
                if (!options.Accepted)
                {
                    Write("Nothing was installed: a silent install needs /ACCEPT, to say you have read the Terms of Use (TERMS.md).");
                    return 2;
                }
                try
                {
                    var installer = new Installer(options, (percent, text) => Write("[" + percent + "%] " + text), Write);
                    installer.Run();
                    Write("Ember was installed in " + options.InstallDir + (installer.Warnings ? " (with warnings: see above)." : "."));
                    if (options.FromUpdate) Installer.OpenEmber(options.InstallDir);
                    return 0;
                }
                catch (Exception ex)
                {
                    Write("FAILED: " + ex.Message);
                    if (options.FromUpdate)
                    {
                        MessageBox.Show("Ember could not be updated: " + ex.Message + Environment.NewLine + Environment.NewLine + "The log is at " + logPath, "Ember update", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Installer.OpenEmber(options.InstallDir);
                    }
                    return 1;
                }
            }
        }
    }
}
