namespace Ember.Core;

/// <summary>What the command line asked of the app. Unknown arguments are ignored.</summary>
public sealed record LaunchOptions(bool Minimised)
{
    public const string MinimisedSwitch = "--minimised";

    public static LaunchOptions Parse(IEnumerable<string> args) =>
        new(args.Any(a => a.Equals(MinimisedSwitch, StringComparison.OrdinalIgnoreCase)
                       || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The command the Windows Run key starts at sign-in: the program in quotes, with the switch when it should open in the tray.</summary>
    public static string StartupCommand(string exePath, bool minimised) =>
        "\"" + exePath + "\"" + (minimised ? " " + MinimisedSwitch : "");
}
