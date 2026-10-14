using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Ember.Core;

namespace Ember.App;

/// <summary>
/// Installs the bundled Stream Deck plugin (beta) by handing the .streamDeckPlugin file to the Stream Deck app, which asks the
/// person to confirm. Ember writes its own path into the plugin so the "Open Ember" key can start the app when it is closed.
/// </summary>
static class StreamDeckInstaller
{
    const string PluginFolderName = "dev.ember.pc.sdPlugin";
    const string DevOverride = "EMBER_STREAMDECK_PLUGIN";

    public sealed record Detected(StreamDeckState State, string? Installed, string? Bundled);

    static string StreamDeckFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck");
    static string PluginFolder => Path.Combine(StreamDeckFolder, "Plugins", PluginFolderName);

    /// <summary>The plugin file next to the installed app (Ember\StreamDeck), or the path in EMBER_STREAMDECK_PLUGIN for a development run.</summary>
    public static string? BundledFile()
    {
        var env = Environment.GetEnvironmentVariable(DevOverride);
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "StreamDeck", "Ember.streamDeckPlugin"));
        return File.Exists(file) ? file : null;
    }

    static DateTime _at;
    static Detected? _cached;

    /// <summary>What is on this PC now. Cached for a few seconds because the Devices page asks twice a second.</summary>
    public static Detected Detect(bool fresh = false)
    {
        if (!fresh && _cached is not null && DateTime.UtcNow - _at < TimeSpan.FromSeconds(3)) return _cached;
        var bundled = BundledFile() is { } f ? BundledVersion(f) : null;
        string? installed = null;
        try
        {
            var manifest = Path.Combine(PluginFolder, "manifest.json");
            if (File.Exists(manifest)) installed = StreamDeckInstallState.ReadManifestVersion(File.ReadAllText(manifest)) ?? "";
        }
        catch (Exception) { installed = ""; }
        var state = bundled is null
            ? StreamDeckState.NotFound // no plugin file to install: the card offers nothing
            : StreamDeckInstallState.Evaluate(Directory.Exists(StreamDeckFolder), installed, bundled);
        _cached = new Detected(state, installed, bundled);
        _at = DateTime.UtcNow;
        return _cached;
    }

    static string? BundledVersion(string file)
    {
        try
        {
            using var zip = ZipFile.OpenRead(file);
            var entry = zip.GetEntry(PluginFolderName + "/manifest.json");
            if (entry is null) return null;
            using var reader = new StreamReader(entry.Open());
            return StreamDeckInstallState.ReadManifestVersion(reader.ReadToEnd());
        }
        catch (Exception) { return null; }
    }

    /// <summary>Opens the plugin file in the Stream Deck app. Returns a short message for the person.</summary>
    public static string Install()
    {
        try
        {
            var source = BundledFile();
            if (source is null) return "The Stream Deck plugin file is not in this installation.";
            var temp = Path.Combine(Path.GetTempPath(), "EmberStreamDeck");
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, "Ember.streamDeckPlugin");
            File.Copy(source, copy, true);
            using (var zip = ZipFile.Open(copy, ZipArchiveMode.Update))
            {
                zip.GetEntry(PluginFolderName + "/ember-path.txt")?.Delete();
                var entry = zip.CreateEntry(PluginFolderName + "/ember-path.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(Environment.ProcessPath);
            }
            Process.Start(new ProcessStartInfo(copy) { UseShellExecute = true })?.Dispose();
            _cached = null;
            return "Confirm the install in the Stream Deck window.";
        }
        catch (Exception ex)
        {
            DiagLog.Warn("StreamDeck", "Could not hand the plugin to Stream Deck", ex);
            return "Could not open the plugin in Stream Deck.";
        }
    }

    /// <summary>Best effort: stops the plugin program and deletes its folder. Returns a short message for the person.</summary>
    public static string Remove()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("EmberStreamDeck")) { try { p.Kill(); } catch (Exception) { /* already gone */ } p.Dispose(); }
            if (Directory.Exists(PluginFolder)) Directory.Delete(PluginFolder, true);
            _cached = null;
            return "Removed. If the keys are still in Stream Deck, restart Stream Deck.";
        }
        catch (Exception ex)
        {
            DiagLog.Warn("StreamDeck", "Could not remove the plugin folder", ex);
            _cached = null;
            return "Could not remove it while Stream Deck is using it. Quit Stream Deck and try again.";
        }
    }
}
