using System.Text.Json;

namespace Ember.Core;

/// <summary>How hot one part of the PC usually runs: a count of readings for every whole degree from <see cref="MinC"/> to <see cref="MaxC"/>.</summary>
public sealed class TemperatureHistogram
{
    public const int MinC = 20, MaxC = 100;
    public const int Bins = MaxC - MinC + 1;

    readonly double[] _counts;

    public TemperatureHistogram(double[]? counts = null)
    {
        _counts = new double[Bins];
        if (counts is null) return;
        for (var i = 0; i < Math.Min(counts.Length, Bins); i++) _counts[i] = double.IsFinite(counts[i]) && counts[i] > 0 ? Math.Min(counts[i], 1e9) : 0;
    }

    public double Total => _counts.Sum();
    public IReadOnlyList<double> Counts => _counts;

    /// <summary>Counts one reading. Readings outside 20 to 100 °C, or not numbers, are ignored (a sensor glitch must not shape a curve).</summary>
    public void Add(double tempC)
    {
        if (!double.IsFinite(tempC) || tempC < MinC || tempC >= MaxC + 1) return;
        _counts[(int)Math.Floor(tempC) - MinC] += 1;
    }

    /// <summary>Makes every count <paramref name="factor"/> times as big (0 to 1): older readings count for less.</summary>
    public void Fade(double factor)
    {
        factor = Math.Clamp(double.IsFinite(factor) ? factor : 1, 0, 1);
        for (var i = 0; i < _counts.Length; i++) _counts[i] *= factor;
    }

    /// <summary>The temperature below which <paramref name="fraction"/> (0 to 1) of the readings fall, or null with no readings.</summary>
    public double? Percentile(double fraction)
    {
        var total = Total;
        if (total <= 0) return null;
        var target = Math.Clamp(fraction, 0, 1) * total;
        var run = 0.0;
        for (var i = 0; i < _counts.Length; i++)
        {
            run += _counts[i];
            if (run >= target && _counts[i] > 0) return MinC + i + 0.5;
        }
        return MaxC;
    }

    public double? Highest
    {
        get
        {
            for (var i = _counts.Length - 1; i >= 0; i--) if (_counts[i] > 0) return MinC + i + 0.5;
            return null;
        }
    }
}

/// <summary>
/// What Ember has learned about this PC: how hot the CPU and the case usually run, and for how long it has been watching.
/// It holds temperatures only (a count per degree), never what the PC was doing, and lives in a small file on this PC.
/// </summary>
public sealed class ThermalProfile
{
    /// <summary>One reading is kept every this many seconds, however fast snapshots arrive.</summary>
    public const int SampleSeconds = 10;

    /// <summary>Each day, older readings count this much less (so the profile follows a new cooler, a new season, a new PC).</summary>
    public const double DailyFade = 0.95;

    public TemperatureHistogram Cpu { get; }
    public TemperatureHistogram Case { get; }
    public DateTimeOffset? LastSample { get; private set; }
    public DateTimeOffset? FirstSample { get; private set; }

    public ThermalProfile(TemperatureHistogram? cpu = null, TemperatureHistogram? @case = null, DateTimeOffset? first = null, DateTimeOffset? last = null)
    {
        Cpu = cpu ?? new TemperatureHistogram();
        Case = @case ?? new TemperatureHistogram();
        FirstSample = first;
        LastSample = last;
    }

    /// <summary>Hours of watching the profile rests on (after fading).</summary>
    public double Hours => Math.Max(Cpu.Total, Case.Total) * SampleSeconds / 3600.0;

    /// <summary>Takes a reading if enough time has passed since the last one. Returns whether it was kept.</summary>
    public bool Add(DateTimeOffset at, double? cpuTemp, double? caseTemp)
    {
        if (cpuTemp is null && caseTemp is null) return false;
        if (LastSample is { } last)
        {
            if (at < last) return false;                                        // the clock went back: ignore until it catches up
            if ((at - last).TotalSeconds < SampleSeconds) return false;
            var days = (at - last).TotalDays;
            if (days >= 1) { var f = Math.Pow(DailyFade, Math.Min(days, 365)); Cpu.Fade(f); Case.Fade(f); }
        }
        if (cpuTemp is { } c) Cpu.Add(c);
        if (caseTemp is { } k) Case.Add(k);
        FirstSample ??= at;
        LastSample = at;
        return true;
    }

    public void Clear()
    {
        Cpu.Fade(0);
        Case.Fade(0);
        FirstSample = LastSample = null;
    }
}

/// <summary>The profile as a small JSON file. A missing, oversized or damaged file is an empty profile, and nothing here throws.</summary>
public sealed class ThermalStore(string path)
{
    const int MaxBytes = 65_536;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ember", "thermal.json");

    sealed class Dto
    {
        public double[]? Cpu { get; set; }
        public double[]? Case { get; set; }
        public DateTimeOffset? First { get; set; }
        public DateTimeOffset? Last { get; set; }
    }

    public ThermalProfile Load()
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return new ThermalProfile();
            var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(path));
            if (dto is null) return new ThermalProfile();
            return new ThermalProfile(new TemperatureHistogram(dto.Cpu), new TemperatureHistogram(dto.Case), dto.First, dto.Last);
        }
        catch (Exception) { return new ThermalProfile(); }
    }

    public bool TrySave(ThermalProfile profile)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(new Dto
                {
                    Cpu = profile.Cpu.Counts.Select(Round).ToArray(), Case = profile.Case.Counts.Select(Round).ToArray(),
                    First = profile.FirstSample, Last = profile.LastSample,
                }));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return true;
        }
        catch (Exception) { return false; }
    }

    public void Delete()
    {
        try { File.Delete(path); } catch (Exception) { /* nothing to delete, or it is locked: the next save overwrites it */ }
    }

    static double Round(double v) => Math.Round(v, 2);
}
