using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Ember.Setup
{
    public sealed class SetupException : Exception
    {
        public SetupException(string message) : base(message) { }
    }

    /// <summary>
    /// Does the installing: checks the PC, installs the Microsoft runtimes Ember needs if they are missing, unpacks Ember and runs
    /// scripts\install-ember.ps1 (the same script the zip release uses), which sets up the services, shortcuts and registry entries.
    /// </summary>
    public sealed class Installer
    {
        public const int TermsVersion = 1; // keep in step with Ember.Core.Legal.TermsVersion

        readonly SetupOptions _o;
        readonly Action<int, string> _progress;
        readonly Action<string> _log;

        public bool Warnings { get; private set; }

        public Installer(SetupOptions options, Action<int, string> progress, Action<string> log)
        {
            _o = options;
            _progress = progress;
            _log = log;
        }

        public static string Version
        {
            get
            {
                var v = typeof(Installer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
                var plus = v.IndexOf('+');
                return plus >= 0 ? v.Substring(0, plus) : v;
            }
        }

        public static bool HasPayload => Has("payload.zip");

        static bool Has(string name) => typeof(Installer).Assembly.GetManifestResourceInfo(name) != null;

        /// <summary>Where Ember is already installed, or null.</summary>
        public static string? ExistingInstall()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Ember"))
                    return key?.GetValue("InstallDir") as string;
            }
            catch (Exception) { return null; }
        }

        public static string Text(string resource)
        {
            using (var s = typeof(Installer).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        public void Run()
        {
            _progress(1, "Checking this PC");
            Preflight();

            // Everything is unpacked into a folder only administrators can touch, because it is run with administrator rights.
            var work = CreateSecureWorkFolder();
            try
            {
                _progress(5, "Unpacking Ember");
                var source = Path.Combine(work, "ember");
                Extract("payload.zip", Path.Combine(work, "payload.zip"));
                ZipFile.ExtractToDirectory(Path.Combine(work, "payload.zip"), source);
                File.Delete(Path.Combine(work, "payload.zip"));

                if (_o.InstallRuntimes)
                {
                    _progress(25, "Checking the Visual C++ runtime (used by OpenRGB)");
                    EnsureVcRuntime(work);
                    _progress(40, "Checking the .NET 8 Desktop Runtime");
                    EnsureDotNet(work);
                }

                _progress(55, "Installing Ember: services, shortcuts and settings");
                RunInstallScript(source);
                _progress(100, "Done");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch (Exception) { /* a leftover temporary folder is harmless */ }
            }
        }

        void Preflight()
        {
            if (!Environment.Is64BitOperatingSystem) throw new SetupException("Ember needs 64-bit Windows 10 or 11.");
            if (Environment.OSVersion.Version.Major < 10) throw new SetupException("Ember needs Windows 10 or 11.");
            if (!HasPayload) throw new SetupException("This setup program has no Ember files inside it (it was built without them). Download the installer again from the Ember releases page.");
            var root = Path.GetPathRoot(Path.GetFullPath(_o.InstallDir));
            try
            {
                var drive = new DriveInfo(root);
                if (drive.AvailableFreeSpace < 600L * 1024 * 1024) throw new SetupException("There is not enough free space on " + root + ". Ember needs about 600 MB while installing.");
            }
            catch (ArgumentException) { throw new SetupException("The install folder is not valid: " + _o.InstallDir); }
            if (!Path.IsPathRooted(_o.InstallDir)) throw new SetupException("Choose a full folder path for Ember, such as C:\\Program Files\\Ember.");
        }

        static string CreateSecureWorkFolder()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Ember-setup-" + Guid.NewGuid().ToString("N"));
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(path, security);
            return path;
        }

        static void Extract(string resource, string to)
        {
            using (var s = typeof(Installer).Assembly.GetManifestResourceStream(resource))
            {
                if (s == null) throw new SetupException("The installer is missing " + resource + ".");
                using (var f = File.Create(to)) s.CopyTo(f, 1 << 20);
            }
        }

        // ---- runtimes ----

        static bool VcRuntimeInstalled()
        {
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                return key != null && key.GetValue("Installed") is int i && i == 1 && key.GetValue("Major") is int major && major >= 14;
        }

        /// <summary>
        /// The .NET installers register themselves in the 32-bit view of the registry, so both views are looked at, and the
        /// runtime's own folder is the last word.
        /// </summary>
        public static bool DotNetDesktop8Installed()
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetValueNames())
                            if (name.StartsWith("8.", StringComparison.Ordinal)) return true;
                    }
                }
                catch (Exception) { /* fall through to the folder check */ }
            }
            try
            {
                var shared = Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.WindowsDesktop.App");
                if (Directory.Exists(shared)) foreach (var dir in Directory.GetDirectories(shared, "8.*")) if (File.Exists(Path.Combine(dir, "PresentationFramework.dll"))) return true;
            }
            catch (Exception) { }
            return false;
        }

        void EnsureVcRuntime(string work)
        {
            if (VcRuntimeInstalled()) { _log("The Visual C++ runtime is already installed."); return; }
            if (!Has("vc-redist.exe")) { Warn("The Visual C++ runtime is missing and this setup does not include it; OpenRGB lighting may not start."); return; }
            _log("Installing the Visual C++ runtime from Microsoft...");
            var exe = Path.Combine(work, "vc_redist.x64.exe");
            Extract("vc-redist.exe", exe);
            var code = RunAndLog(exe, "/install /quiet /norestart");
            if (code == 0 || code == 1638 || code == 3010) _log("Visual C++ runtime ready" + (code == 3010 ? " (a restart is needed to finish it)." : "."));
            else Warn("The Visual C++ runtime installer returned " + code + ". OpenRGB lighting may not start until it is installed.");
        }

        void EnsureDotNet(string work)
        {
            if (DotNetDesktop8Installed()) { _log("The .NET 8 Desktop Runtime is already installed."); return; }
            if (!Has("dotnet-runtime.exe")) throw new SetupException("The .NET 8 Desktop Runtime is missing and this setup does not include it. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 and run setup again.");
            _log("Installing the .NET 8 Desktop Runtime from Microsoft (this takes a minute)...");
            var exe = Path.Combine(work, "dotnet-desktop-runtime-x64.exe");
            Extract("dotnet-runtime.exe", exe);
            var code = RunAndLog(exe, "/install /quiet /norestart");
            if (code != 0 && code != 1638 && code != 3010) throw new SetupException("The .NET 8 Desktop Runtime installer failed with code " + code + ". Ember cannot run without it.");
            if (!DotNetDesktop8Installed() && code != 3010) throw new SetupException("The .NET 8 Desktop Runtime did not install. Ember cannot run without it.");
            _log(".NET 8 Desktop Runtime ready" + (code == 3010 ? " (a restart is needed to finish it)." : "."));
        }

        // ---- Ember ----

        void RunInstallScript(string source)
        {
            var script = Path.Combine(source, "scripts", "install-ember.ps1");
            if (!File.Exists(script)) throw new SetupException("The installer is missing scripts\\install-ember.ps1.");
            var args = new StringBuilder("-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -SourceDir \"" + source + "\" -InstallDir \"" + _o.InstallDir.TrimEnd('\\') + "\" -AcceptedTerms " + TermsVersion);
            if (!_o.InstallPawnIO) args.Append(" -SkipPawnIO");
            if (_o.DesktopShortcut) args.Append(" -DesktopShortcut");
            var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var code = RunAndLog(ps, args.ToString());
            if (code != 0) throw new SetupException("Installing Ember failed (the install script returned " + code + "). The messages above say why.");
        }

        public static void OpenEmber(string installDir)
        {
            var exe = Path.Combine(installDir, "App", "Ember.App.exe");
            if (!File.Exists(exe)) return;
            // Through Explorer, so Ember starts as the signed-in user and not with this installer's administrator rights.
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + exe + "\"") { UseShellExecute = false });
        }

        void Warn(string text)
        {
            Warnings = true;
            _log("WARNING: " + text);
        }

        /// <summary>Runs a program, copies everything it prints into the log, and returns its exit code. Output that says WARNING counts as a warning.</summary>
        int RunAndLog(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = new Process { StartInfo = psi })
            {
                void Line(string line)
                {
                    if (line == null) return;
                    if (line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)) Warnings = true;
                    _log(line);
                }
                p.OutputDataReceived += (_, e) => Line(e.Data);
                p.ErrorDataReceived += (_, e) => Line(e.Data);
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                p.WaitForExit(); // flush the output events
                return p.ExitCode;
            }
        }
    }
}
