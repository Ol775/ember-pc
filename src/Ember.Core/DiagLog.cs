using System.Text;
using System.Text.RegularExpressions;

namespace Ember.Core;

/// <summary>
/// A small diagnostic log for finding out what went wrong: one text file per day in %APPDATA%\Ember\logs, kept for a week,
/// never sent anywhere. It records what Ember did and why a device or integration failed, never colours, keys or passwords:
/// anything that looks like a secret is blanked before it is written. Logging can never throw or slow Ember down noticeably.
/// </summary>
public static class DiagLog
{
    public const int KeepDays = 7;
    const long MaxFileBytes = 2 * 1024 * 1024;

    static readonly object Gate = new();
    static readonly Queue<string> Recent = new();
    static string _folder = DefaultFolder;
    static readonly Dictionary<string, string> LastByKey = [];

    public static string DefaultFolder => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "logs");
    public static string Folder { get { lock (Gate) return _folder; } }

    /// <summary>Points the log somewhere else (tests) and forgets what was remembered.</summary>
    public static void Use(string folder)
    {
        lock (Gate) { _folder = folder; Recent.Clear(); LastByKey.Clear(); }
    }

    public static void Info(string area, string message) => Write("INFO", area, message, null);
    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN", area, message, ex);
    public static void Error(string area, string message, Exception? ex = null) => Write("ERROR", area, message, ex);

    /// <summary>Logs only when <paramref name="value"/> differs from what was logged last for this key, so a status polled every few seconds is one line, not thousands.</summary>
    public static void Changed(string area, string key, string value)
    {
        lock (Gate)
        {
            if (LastByKey.TryGetValue(area + "|" + key, out var last) && last == value) return;
            LastByKey[area + "|" + key] = value;
        }
        Write("INFO", area, key + ": " + value, null);
    }

    /// <summary>The last lines written in this run (newest last), for the diagnostic report.</summary>
    public static IReadOnlyList<string> Tail(int count = 150)
    {
        lock (Gate) return Recent.TakeLast(count).ToList();
    }

    /// <summary>Blanks anything that looks like a secret: API keys and tokens, the Hue application key, and the user name in paths.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = Secret.Replace(text, m => m.Groups[1].Value + "=<hidden>");
        text = LongToken.Replace(text, "<hidden>");
        var user = Environment.UserName;
        if (!string.IsNullOrEmpty(user) && user.Length > 2) text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    static readonly Regex Secret = new(@"(?i)\b(api[-_ ]?key|apikey|token|auth[-_]?token|password|secret|username|key)\s*[=:]\s*[""']?[^\s""',;&]+", RegexOptions.Compiled);
    static readonly Regex LongToken = new(@"\b[A-Za-z0-9_\-]{32,}\b", RegexOptions.Compiled);

    static void Write(string level, string area, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').Append(level.PadRight(5)).Append(' ').Append(area).Append(": ").Append(message);
            if (ex is not null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            var line = Scrub(sb.ToString().Replace("\r", " ").Replace("\n", " "));
            lock (Gate)
            {
                Recent.Enqueue(line);
                while (Recent.Count > 500) Recent.Dequeue();
                Directory.CreateDirectory(_folder);
                var path = Path.Combine(_folder, $"ember-{DateTime.Now:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes) File.Move(path, Path.Combine(_folder, $"ember-{DateTime.Now:yyyyMMdd-HHmmss}.log"), true);
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception) { /* logging must never break Ember */ }
    }

    /// <summary>Deletes log files older than <see cref="KeepDays"/> days. Called once at start.</summary>
    public static void Prune()
    {
        try
        {
            string folder;
            lock (Gate) folder = _folder;
            if (!Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder, "ember-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-KeepDays)) File.Delete(f);
        }
        catch (Exception) { /* old logs stay: harmless */ }
    }
}
