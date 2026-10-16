using System.Text.Json;

namespace Ember.Core;

/// <summary>When Ember tells the person something is wrong. Temperatures are in degrees Celsius whatever unit the app shows.</summary>
public sealed record AlertSettings(bool Enabled = false, int CpuTempC = 85, int GpuTempC = 85, bool FanStopped = true)
{
    public const int MinTempC = 60, MaxTempC = 95;
    public static AlertSettings Default { get; } = new();
}

public sealed record Alert(string Key, string Title, string Text);

/// <summary>
/// Watches the readings and says when a component has stayed hot or a fan that was turning has stopped. Each alert is sent once, then
/// stays quiet until the problem has gone and come back (or ten minutes have passed).
/// </summary>
public sealed class AlertEngine
{
    public static readonly TimeSpan HotFor = TimeSpan.FromSeconds(5), StoppedFor = TimeSpan.FromSeconds(10), Repeat = TimeSpan.FromMinutes(10);
    const double SpinningRpm = 300, StoppedRpm = 50, CoolMargin = 5, IdleZeroFanBelowC = 55;

    readonly Dictionary<string, DateTimeOffset> _since = [];   // when each problem began
    readonly Dictionary<string, DateTimeOffset> _sent = [];    // when each alert was last sent
    readonly HashSet<string> _spun = [];                        // fans seen turning at some point this run

    public IReadOnlyList<Alert> Evaluate(DateTimeOffset now, DashboardSummary summary, bool stale, AlertSettings settings)
    {
        var alerts = new List<Alert>();
        if (!settings.Enabled || stale) { _since.Clear(); return alerts; }

        Check(alerts, now, "cpu-hot", summary.CpuTemp, settings.CpuTempC, "CPU is hot", "CPU");
        Check(alerts, now, "gpu-hot", summary.GpuTemp, settings.GpuTempC, "Graphics card is hot", "The graphics card");

        var hottest = new[] { summary.CpuTemp, summary.GpuTemp }.Where(t => t is not null).Select(t => t!.Value).DefaultIfEmpty(0).Max();
        foreach (var fan in summary.Fans)
        {
            var key = "fan-stopped:" + fan.Name;
            if (fan.Rpm >= SpinningRpm) _spun.Add(fan.Name);
            // A fan that was turning, now reads almost nothing, while the PC is warm enough that a fan should be running. A cool PC may
            // legitimately stop its fans (zero-RPM mode), and a header with no fan on it never turned in the first place.
            var stopped = settings.FanStopped && _spun.Contains(fan.Name) && fan.Rpm < StoppedRpm && hottest >= IdleZeroFanBelowC;
            Raise(alerts, now, key, stopped, StoppedFor, "A fan has stopped", $"{fan.Name} reads {fan.Rpm:0} RPM while the PC is at {hottest:0} °C. Check that it is plugged in and not blocked.");
        }
        if (!settings.FanStopped) foreach (var k in _since.Keys.Where(k => k.StartsWith("fan-stopped:", StringComparison.Ordinal)).ToList()) _since.Remove(k);
        return alerts;
    }

    void Check(List<Alert> alerts, DateTimeOffset now, string key, double? temp, int limit, string title, string who)
    {
        // Once hot, it counts as cooled only a few degrees below the limit, so a reading hovering at the limit does not flicker.
        var hot = temp is { } t && (t >= limit || (_since.ContainsKey(key) && t >= limit - CoolMargin));
        Raise(alerts, now, key, hot, HotFor, title, $"{who} is at {temp:0} °C (your limit is {limit} °C). Check the fans and the airflow.");
    }

    void Raise(List<Alert> alerts, DateTimeOffset now, string key, bool problem, TimeSpan after, string title, string text)
    {
        if (!problem) { _since.Remove(key); _sent.Remove(key); return; }
        if (!_since.TryGetValue(key, out var since)) { _since[key] = since = now; }
        if (now - since < after) return;
        if (_sent.TryGetValue(key, out var last) && now - last < Repeat) return;
        _sent[key] = now;
        alerts.Add(new Alert(key, title, text));
    }
}

public sealed class AlertStore(string path)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "alerts.json");

    public AlertSettings Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 10_000) return AlertSettings.Default;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            bool Flag(string key, bool fallback) => root.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
            int Temp(string key, int fallback) => root.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, AlertSettings.MinTempC, AlertSettings.MaxTempC) : fallback;
            return new AlertSettings(Flag("Enabled", false), Temp("CpuTempC", 85), Temp("GpuTempC", 85), Flag("FanStopped", true));
        }
        catch (Exception) { return AlertSettings.Default; }
    }

    public bool TrySave(AlertSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception) { return false; }
    }
}
