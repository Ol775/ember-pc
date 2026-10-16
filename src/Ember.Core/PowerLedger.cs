using System.Text.Json;

namespace Ember.Core;

/// <summary>
/// How much energy the PC used each day, split into CPU, graphics card and the rest, kept for the last two months so the Power page
/// can show daily use and estimate a bill. Energy is added up from the watt readings Ember already takes; a silence longer than a few
/// seconds (Ember closed, PC asleep) is a gap and adds nothing.
/// </summary>
public sealed class PowerLedger
{
    public sealed record Day(DateOnly Date, double CpuWh, double GpuWh, double RestWh, double Seconds)
    {
        public double Wh => CpuWh + GpuWh + RestWh;
        public double Kwh => Wh / 1000;
        public double AverageW => Seconds > 0 ? Wh * 3600 / Seconds : 0;
    }

    /// <summary>Average use per day over the recent days, by part, in kWh.</summary>
    public sealed record Averages(double CpuKwh, double GpuKwh, double RestKwh, double CpuW, double GpuW, double RestW)
    {
        public double TotalKwh => CpuKwh + GpuKwh + RestKwh;
        public double TotalW => CpuW + GpuW + RestW;
    }

    public const int KeepDays = 60;
    const double LongestGapSeconds = 10;
    const double MinSecondsForADay = 600;   // a day with less than ten minutes of readings says nothing about a normal day
    static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    readonly Dictionary<DateOnly, Day> _days = [];
    DateTimeOffset? _last;
    double _lastCpu, _lastGpu, _lastRest;

    public bool Dirty { get; private set; }

    /// <summary>Adds one reading. Any part that is not measured is left out (counts as nothing).</summary>
    public void Add(DateTimeOffset at, double? cpuW, double? gpuW, double? restW)
    {
        static double Ok(double? v) => v is { } w && double.IsFinite(w) && w >= 0 ? w : 0;
        var cpu = Ok(cpuW); var gpu = Ok(gpuW); var rest = Ok(restW);
        if (_last is { } prev && (at - prev).TotalSeconds is > 0 and var dt && dt <= LongestGapSeconds)
        {
            var date = DateOnly.FromDateTime(at.ToLocalTime().DateTime);
            var d = _days.GetValueOrDefault(date) ?? new Day(date, 0, 0, 0, 0);
            _days[date] = d with
            {
                CpuWh = d.CpuWh + (cpu + _lastCpu) / 2 * dt / 3600,
                GpuWh = d.GpuWh + (gpu + _lastGpu) / 2 * dt / 3600,
                RestWh = d.RestWh + (rest + _lastRest) / 2 * dt / 3600,
                Seconds = d.Seconds + dt,
            };
            Dirty = true;
        }
        _last = at;
        (_lastCpu, _lastGpu, _lastRest) = (cpu, gpu, rest);
    }

    public Day Today(DateOnly today) => _days.GetValueOrDefault(today) ?? new Day(today, 0, 0, 0, 0);

    /// <summary>The last <paramref name="count"/> days ending today, oldest first; a day with no readings is an empty one.</summary>
    public IReadOnlyList<Day> Last(int count, DateOnly today) =>
        Enumerable.Range(0, count).Select(i => today.AddDays(i - count + 1)).Select(d => _days.GetValueOrDefault(d) ?? new Day(d, 0, 0, 0, 0)).ToList();

    /// <summary>
    /// What a normal day looks like: the average of the earlier days of the last week that have enough readings, or, when there are none yet,
    /// today so far scaled up to 24 hours.
    /// </summary>
    public Averages Typical(DateOnly today)
    {
        var days = Last(8, today).Where(d => d.Date != today && d.Seconds >= MinSecondsForADay).ToList();
        if (days.Count > 0)
        {
            var secs = days.Sum(d => d.Seconds);
            double Wh(Func<Day, double> pick) => days.Sum(pick);
            return new Averages(Wh(d => d.CpuWh) / days.Count / 1000, Wh(d => d.GpuWh) / days.Count / 1000, Wh(d => d.RestWh) / days.Count / 1000,
                Wh(d => d.CpuWh) * 3600 / secs, Wh(d => d.GpuWh) * 3600 / secs, Wh(d => d.RestWh) * 3600 / secs);
        }
        var t = Today(today);
        if (t.Seconds < 300) return new Averages(0, 0, 0, 0, 0, 0);
        var scale = 86400 / t.Seconds / 1000;   // Wh used in t.Seconds, scaled to a day, in kWh
        return new Averages(t.CpuWh * scale, t.GpuWh * scale, t.RestWh * scale, t.CpuWh * 3600 / t.Seconds, t.GpuWh * 3600 / t.Seconds, t.RestWh * 3600 / t.Seconds);
    }

    // ---- file ----

    public static string DefaultPath => Path.Combine(Ember.Core.AppData.Roaming, "Ember", "power-days.json");

    sealed record Row(string D, double Cpu, double Gpu, double Rest, double S);

    /// <summary>A missing or damaged file is an empty ledger; this never throws.</summary>
    public static PowerLedger Load(string path)
    {
        var ledger = new PowerLedger();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1_000_000) return ledger;
            foreach (var r in JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path), Options) ?? [])
                if (DateOnly.TryParseExact(r.D, "yyyy-MM-dd", out var date) && new[] { r.Cpu, r.Gpu, r.Rest, r.S }.All(v => double.IsFinite(v) && v >= 0))
                    ledger._days[date] = new Day(date, r.Cpu, r.Gpu, r.Rest, r.S);
        }
        catch (Exception) { /* start again */ }
        ledger.Dirty = false;
        return ledger;
    }

    /// <summary>Best effort, like the other settings files: a read-only profile must not stop the app.</summary>
    public bool TrySave(string path, DateOnly today)
    {
        try
        {
            var keep = _days.Values.Where(d => d.Date > today.AddDays(-KeepDays)).OrderBy(d => d.Date)
                .Select(d => new Row(d.Date.ToString("yyyy-MM-dd"), Math.Round(d.CpuWh, 3), Math.Round(d.GpuWh, 3), Math.Round(d.RestWh, 3), Math.Round(d.Seconds)));
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(dir);
            var temp = Path.Combine(dir, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(keep, Options));
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            Dirty = false;
            return true;
        }
        catch (Exception) { return false; }
    }
}

/// <summary>One plain, true sentence about where to save: the part of the PC that draws the most, and what a tenth less of it is worth.</summary>
public static class PowerTip
{
    public static string? For(PowerLedger.Averages a, double pricePerKwh, System.Globalization.CultureInfo? culture = null)
    {
        if (a.TotalKwh <= 0) return null;
        var parts = new[] { ("CPU", a.CpuW, a.CpuKwh), ("graphics card", a.GpuW, a.GpuKwh), ("rest of the PC", a.RestW, a.RestKwh) };
        var (name, watts, kwh) = parts.OrderByDescending(p => p.Item3).First();
        var saved = PowerCost.Of(kwh * 0.1 * PowerCost.DaysPerMonth, pricePerKwh);
        var share = kwh / a.TotalKwh * 100;
        return $"The {name} takes the most, about {share:0} % ({watts:0} W on average). Using a tenth less of it would save roughly {PowerCost.Format(saved, culture)} a month.";
    }
}

/// <summary>Turns energy into money. The price is the person's own, per kWh; the result is an estimate, not a bill.</summary>
public static class PowerCost
{
    public const double DaysPerMonth = 30, DaysPerYear = 365;

    public static double Of(double kwh, double pricePerKwh) => Math.Max(0, kwh) * Math.Max(0, pricePerKwh);

    /// <summary>"£0.53", with the symbol of the person's own region.</summary>
    public static string Format(double amount, System.Globalization.CultureInfo? culture = null)
    {
        culture ??= System.Globalization.CultureInfo.CurrentCulture;
        var symbol = culture.NumberFormat.CurrencySymbol;
        return amount >= 100 ? symbol + amount.ToString("N0", culture) : symbol + amount.ToString("0.00", culture);
    }
}
