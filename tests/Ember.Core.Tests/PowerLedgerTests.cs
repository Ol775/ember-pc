using System.Globalization;
using Ember.Core;
using Xunit;

namespace Ember.Core.Tests;

public class PowerLedgerTests
{
    static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    static DateOnly DayOf(DateTimeOffset t) => DateOnly.FromDateTime(t.ToLocalTime().DateTime);

    [Fact]
    public void Energy_is_added_up_by_part()
    {
        var l = new PowerLedger();
        for (var s = 0; s <= 3600; s += 5) l.Add(Noon.AddSeconds(s), 100, 50, 25);   // one hour at 175 W
        var d = l.Today(DayOf(Noon));
        Assert.Equal(100, d.CpuWh, 1);
        Assert.Equal(50, d.GpuWh, 1);
        Assert.Equal(25, d.RestWh, 1);
        Assert.Equal(175, d.AverageW, 0);
    }

    [Fact]
    public void A_long_silence_adds_nothing()
    {
        var l = new PowerLedger();
        l.Add(Noon, 100, 0, 0);
        l.Add(Noon.AddMinutes(30), 100, 0, 0);   // Ember was closed in between
        Assert.Equal(0, l.Today(DayOf(Noon)).Wh);
    }

    [Fact]
    public void Missing_readings_count_as_nothing()
    {
        var l = new PowerLedger();
        l.Add(Noon, null, double.NaN, -5);
        l.Add(Noon.AddSeconds(5), null, double.NaN, -5);
        Assert.Equal(0, l.Today(DayOf(Noon)).Wh);
    }

    [Fact]
    public void Typical_day_averages_earlier_days_and_ignores_thin_ones()
    {
        var l = new PowerLedger();
        var today = DayOf(Noon);
        void Fill(int daysAgo, double w, int seconds)
        {
            var start = Noon.AddDays(-daysAgo);
            for (var s = 0; s <= seconds; s += 5) l.Add(start.AddSeconds(s), w, 0, 0);
            l.Add(start.AddDays(5), null, null, null); // break the run
        }
        Fill(1, 100, 7200);    // 0.2 kWh
        Fill(2, 300, 7200);    // 0.6 kWh
        Fill(3, 900, 60);      // too thin: ignored
        var t = l.Typical(today);
        Assert.Equal(0.4, t.CpuKwh, 2);
        Assert.Equal(200, t.CpuW, 0);
    }

    [Fact]
    public void With_no_earlier_days_today_is_scaled_to_a_full_day()
    {
        var l = new PowerLedger();
        for (var s = 0; s <= 3600; s += 5) l.Add(Noon.AddSeconds(s), 100, 0, 0);
        Assert.Equal(2.4, l.Typical(DayOf(Noon)).CpuKwh, 2);   // 100 W for 24 hours
        Assert.Equal(0, new PowerLedger().Typical(DayOf(Noon)).TotalKwh);
    }

    [Fact]
    public void Last_days_are_filled_and_ordered()
    {
        var l = new PowerLedger();
        var days = l.Last(7, DayOf(Noon));
        Assert.Equal(7, days.Count);
        Assert.Equal(DayOf(Noon), days[^1].Date);
        Assert.Equal(DayOf(Noon).AddDays(-6), days[0].Date);
    }

    [Fact]
    public void Saves_and_loads_and_drops_old_days()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-ledger-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var l = new PowerLedger();
            for (var s = 0; s <= 600; s += 5) l.Add(Noon.AddSeconds(s), 100, 20, 30);
            for (var s = 0; s <= 600; s += 5) l.Add(Noon.AddDays(-90).AddSeconds(s), 100, 20, 30);
            Assert.True(l.TrySave(path, DayOf(Noon)));
            var back = PowerLedger.Load(path);
            Assert.Equal(l.Today(DayOf(Noon)).Wh, back.Today(DayOf(Noon)).Wh, 2);
            Assert.Equal(0, back.Today(DayOf(Noon.AddDays(-90))).Wh);   // older than 60 days: gone
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_damaged_file_is_an_empty_ledger()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-ledger-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Equal(0, PowerLedger.Load(path).Today(DayOf(Noon)).Wh);
            Assert.Equal(0, PowerLedger.Load(path + ".missing").Today(DayOf(Noon)).Wh);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Cost_and_format()
    {
        Assert.Equal(1.24, PowerCost.Of(4.42, 0.28), 2);
        Assert.Equal(0, PowerCost.Of(-1, 0.28));
        var uk = CultureInfo.GetCultureInfo("en-GB");
        Assert.Equal("£0.53", PowerCost.Format(0.53, uk));
        Assert.Equal("£1,613", PowerCost.Format(1613, uk));
    }
}

public class PowerTipAndCoarseTests
{
    static readonly System.Globalization.CultureInfo Uk = new("en-GB");

    [Fact]
    public void The_tip_names_the_biggest_part_and_what_a_tenth_less_is_worth()
    {
        var a = new PowerLedger.Averages(1.0, 0.5, 2.0, 40, 20, 80);
        var tip = PowerTip.For(a, 0.30, Uk)!;
        Assert.Contains("rest of the PC", tip);
        Assert.Contains("57 %", tip);        // 2.0 of 3.5 kWh
        Assert.Contains("£1.80", tip);       // 0.2 kWh x 30 days x 0.30
        Assert.Null(PowerTip.For(new PowerLedger.Averages(0, 0, 0, 0, 0, 0), 0.3, Uk));
    }

    [Fact]
    public void The_track_keeps_half_minute_averages()
    {
        var t = new PowerTrack();
        var start = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 90; i++) t.Add(start.AddSeconds(i), i < 30 ? 100 : 200);
        Assert.Equal(3, t.Coarse.Count);
        Assert.Equal(100, t.Coarse[0]);
        Assert.Equal(200, t.Coarse[1]);
    }
}

public class PowerCurrencyTests
{
    [Fact]
    public void A_chosen_currency_sets_the_symbol_and_auto_follows_the_region()
    {
        var uk = new System.Globalization.CultureInfo("en-GB");
        Assert.Equal("$0.53", PowerCost.Format(0.531, uk, "USD"));
        Assert.Equal("€1,234", PowerCost.Format(1234.4, new System.Globalization.CultureInfo("en-GB"), "EUR"));
        Assert.Equal("£0.53", PowerCost.Format(0.531, uk));
        Assert.Equal("£0.53", PowerCost.Format(0.531, uk, "nonsense"));
    }

    [Fact]
    public void Only_known_codes_are_kept_in_the_settings()
    {
        Assert.True(PowerCurrency.IsValid("Auto"));
        Assert.True(PowerCurrency.IsValid("JPY"));
        Assert.False(PowerCurrency.IsValid("XXX"));
        Assert.False(PowerCurrency.IsValid(null));
    }
}

public class PowerExportTests
{
    [Fact]
    public void The_csv_has_a_header_and_one_row_per_day_in_date_order()
    {
        var days = new[]
        {
            new PowerLedger.Day(new DateOnly(2026, 10, 17), 1000, 500, 500, 7200),
            new PowerLedger.Day(new DateOnly(2026, 10, 16), 0, 0, 2000, 3600),
        };
        var lines = PowerExport.Csv(days, 0.30, "GBP").Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Date,Hours watched,Average W,kWh,CPU kWh,GPU kWh,Rest kWh,Cost (GBP)", lines[0]);
        Assert.StartsWith("2026-10-16,1.00,2000.0,2.000,0.000,0.000,2.000,0.60", lines[1]);
        Assert.StartsWith("2026-10-17,2.00,1000.0,2.000", lines[2]);
        Assert.Equal(3, lines.Length);
    }
}
