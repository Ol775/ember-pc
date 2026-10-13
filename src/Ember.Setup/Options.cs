using System;
using System.IO;

namespace Ember.Setup
{
    /// <summary>What the person chose in the wizard, or on the command line for a silent install.</summary>
    public sealed class SetupOptions
    {
        public string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ember");
        public bool DesktopShortcut = true;
        public bool InstallPawnIO = true;
        public bool InstallRuntimes = true;
        public bool LaunchWhenDone = true;
        public bool Silent;
        public bool Accepted;
        /// <summary>Started by Ember's "Update now": installs without the wizard when the terms were already accepted, then opens Ember again.</summary>
        public bool FromUpdate;
        public int AcceptedVersion;

        /// <summary>
        /// Silent install: <c>EmberSetup.exe /S /ACCEPT [/D=folder] [/NODESKTOP] [/NOPAWNIO] [/NORUNTIMES] [/NOLAUNCH]</c>.
        /// Silent means the person has already read the terms and says so with /ACCEPT; without it nothing is installed.
        /// </summary>
        public static SetupOptions Parse(string[] args)
        {
            var o = new SetupOptions();
            foreach (var raw in args)
            {
                var a = raw.Trim();
                if (a.Equals("/S", StringComparison.OrdinalIgnoreCase) || a.Equals("--silent", StringComparison.OrdinalIgnoreCase)) o.Silent = true;
                else if (a.Equals("/ACCEPT", StringComparison.OrdinalIgnoreCase)) o.Accepted = true;
                else if (a.Equals("/UPDATE", StringComparison.OrdinalIgnoreCase)) o.FromUpdate = true;
                else if (a.StartsWith("/ACCEPTED=", StringComparison.OrdinalIgnoreCase)) int.TryParse(a.Substring(10), out o.AcceptedVersion);
                else if (a.Equals("/NODESKTOP", StringComparison.OrdinalIgnoreCase)) o.DesktopShortcut = false;
                else if (a.Equals("/NOPAWNIO", StringComparison.OrdinalIgnoreCase)) o.InstallPawnIO = false;
                else if (a.Equals("/NORUNTIMES", StringComparison.OrdinalIgnoreCase)) o.InstallRuntimes = false;
                else if (a.Equals("/NOLAUNCH", StringComparison.OrdinalIgnoreCase)) o.LaunchWhenDone = false;
                else if (a.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) o.InstallDir = a.Substring(3).Trim('"');
            }
            // An update from inside Ember skips the wizard only when the person has already accepted these terms; otherwise the wizard asks.
            if (o.FromUpdate && o.AcceptedVersion >= Installer.TermsVersion) { o.Silent = true; o.Accepted = true; o.LaunchWhenDone = true; return o; }
            if (o.Silent) o.LaunchWhenDone = false; // a silent install never opens a window
            return o;
        }
    }
}
