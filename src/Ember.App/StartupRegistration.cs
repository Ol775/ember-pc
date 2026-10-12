using Microsoft.Win32;

namespace Ember.App;

/// <summary>"Start Ember when I sign in": a value under the current user's Run key. Nothing here needs administrator rights.</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Ember";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>False when the registry could not be written.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;
                key.SetValue(ValueName, "\"" + exe + "\"");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception) { return false; }
    }
}
