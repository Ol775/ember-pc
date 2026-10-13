using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace Ember.App;

/// <summary>
/// What "Uninstall" in Windows Settings runs: <c>Ember.App.exe --uninstall</c>. It asks once, copies the removal scripts to a
/// temporary folder (so they can delete the folder they came from) and runs them, then closes.
/// </summary>
static class Uninstaller
{
    public static void Run()
    {
        if (!IsAdministrator())
        {
            // The services and the program folder need administrator rights: ask Windows for them and start again.
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--uninstall") { UseShellExecute = true, Verb = "runas" }); }
            catch (Exception) { /* the user said no to the prompt */ }
            return;
        }
        var installDir = Directory.GetParent(AppContext.BaseDirectory.TrimEnd('\\'))?.FullName;
        var scripts = installDir is null ? null : Path.Combine(installDir, "scripts");
        if (scripts is null || !File.Exists(Path.Combine(scripts, "uninstall-ember.ps1")))
        {
            MessageBox.Show("The removal scripts were not found next to Ember, so it cannot be removed from here. You can remove it by running scripts\\uninstall-ember.ps1 from the release folder.", "Ember", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show("Remove Ember from this PC?\n\nThis stops the Ember services and deletes the program. Your fans return to the motherboard's own control when you restart the PC.", "Uninstall Ember",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var settings = MessageBox.Show("Also delete your Ember settings (fan modes, curves, lighting scenes and preferences)?\n\nChoose No to keep them in case you install Ember again.", "Uninstall Ember",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        var temp = Path.Combine(Path.GetTempPath(), "ember-uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        foreach (var f in Directory.GetFiles(scripts, "*.ps1")) File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{Path.Combine(temp, "uninstall-ember.ps1")}\" -InstallDir \"{installDir}\" -RemoveFiles -ShowResult" + (settings ? " -RemoveSettings" : "");
        Process.Start(new ProcessStartInfo("powershell.exe", args) { UseShellExecute = false, CreateNoWindow = true });
    }

    static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
