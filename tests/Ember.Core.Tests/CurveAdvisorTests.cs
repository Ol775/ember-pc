namespace Ember.Core.Tests;

public class ThermalProfileTests : IDisposable
{
    static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ember-thermal-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void One_reading_is_kept_every_ten_seconds_whatever_the_snapshot_rate()
    {
        var p = new ThermalProfile();
        var kept = 0;
        for (var i = 0; i < 100; i++) if (p.Add(T0.AddSeconds(i), 50, 40)) kept++;
        Assert.Equal(10, kept);
        Assert.Equal(10, p.Cpu.Total);
    }

    [Fact]
    public void Missing_or_absurd_readings_are_ignored()
    {
        var p = new ThermalProfile();
        Assert.False(p.Add(T0, null, null));
        p.Add(T0, double.NaN, 500);
        p.Add(T0.AddSeconds(20), -40, double.PositiveInfinity);
        Assert.Equal(0, p.Cpu.Total);
        Assert.Equal(0, p.Case.Total);
    }

    [Fact]
    public void A_clock_that_goes_back_is_ignored_until_it_catches_up()
    {
        var p = new ThermalProfile();
        p.Add(T0, 50, 50);
        Assert.False(p.Add(T0.AddHours(-3), 50, 50));
        Assert.Equal(1, p.Cpu.Total);
    }

    [Fact]
    public void Old_readings_fade_by_the_day()
    {
        var p = new ThermalProfile();
        for (var i = 0; i < 100; i++) p.Add(T0.AddSeconds(i * 10), 50, 50);
        var before = p.Cpu.Total;
        p.Add(T0.AddDays(2).AddHours(1), 60, 60);
        Assert.InRange(p.Cpu.Total, before * 0.89 + 1, before * 0.91 + 1);   // about two days: 0.95 squared, plus the new reading
    }

    [Fact]
    public void Percentiles_follow_the_readings()
    {
        var h = new TemperatureHistogram();
        for (var i = 0; i < 90; i++) h.Add(40);
        for (var i = 0; i < 10; i++) h.Add(80);
        Assert.Equal(40.5, h.Percentile(0.5));
        Assert.Equal(40.5, h.Percentile(0.9));
        Assert.Equal(80.5, h.Percentile(0.95));
        Assert.Equal(80.5, h.Highest);
        Assert.Null(new TemperatureHistogram().Percentile(0.5));
    }

    [Fact]
    public void It_survives_saving_and_loading()
    {
        var path = Path.Combine(_dir, "t.json");
        var p = new ThermalProfile();
        for (var i = 0; i < 50; i++) p.Add(T0.AddSeconds(i * 10), 45 + i % 5, 40);
        Assert.True(new ThermalStore(path).TrySave(p));
        var back = new ThermalStore(path).Load();
        Assert.Equal(p.Cpu.Counts, back.Cpu.Counts);
        Assert.Equal(p.Case.Total, back.Case.Total);
        Assert.Equal(p.LastSample, back.LastSample);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"Cpu\":\"x\"}")]
    [InlineData("{\"Cpu\":[1e999,-5,null]}")]
    public void A_damaged_file_gives_an_empty_or_clean_profile(string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, content);
        var p = new ThermalStore(path).Load();
        Assert.All(p.Cpu.Counts, c => Assert.True(double.IsFinite(c) && c >= 0));
    }

    [Fact]
    public void An_oversized_file_is_ignored_and_a_missing_one_is_empty()
    {
        Directory.CreateDirectory(_dir);
        var big = Path.Combine(_dir, "big.json");
        File.WriteAllText(big, new string(' ', 70_000));
        Assert.Equal(0, new ThermalStore(big).Load().Cpu.Total);
        Assert.Equal(0, new ThermalStore(Path.Combine(_dir, "none.json")).Load().Cpu.Total);
    }

    [Fact]
    public void Clearing_forgets_everything_and_delete_removes_the_file()
    {
        var path = Path.Combine(_dir, "c.json");
        var p = new ThermalProfile();
        p.Add(T0, 50, 50);
        var store = new ThermalStore(path);
        store.TrySave(p);
        p.Clear();
        Assert.Equal(0, p.Hours);
        Assert.Null(p.LastSample);
        store.Delete();
        Assert.False(File.Exists(path));
        store.Delete();                      // deleting nothing is fine
    }
}

public class CurveAdvisorTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A PC that idles around <paramref name="idle"/> and sometimes works at <paramref name="load"/>, watched for <paramref name="hours"/>.</summary>
    static ThermalProfile Watched(double hours, double idle = 45, double load = 70)
    {
        var p = new ThermalProfile();
        var n = (int)(hours * 3600 / ThermalProfile.SampleSeconds);
        for (var i = 0; i < n; i++) p.Add(T0.AddSeconds(i * ThermalProfile.SampleSeconds), i % 10 < 8 ? idle : load, i % 10 < 8 ? idle - 5 : load - 10);
        return p;
    }

    [Fact]
    public void With_too_little_data_it_says_so_and_suggests_nothing()
    {
        var r = CurveAdvisor.Suggest(Watched(0.5), 0.5);
        Assert.False(r.Ready);
        Assert.Contains("learning", r.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(r.Suggestion);
        Assert.False(CurveAdvisor.Suggest(new ThermalProfile(), 0.5).Ready);
    }

    [Fact]
    public void With_enough_data_it_suggests_two_curves_and_explains()
    {
        var r = CurveAdvisor.Suggest(Watched(3), 0.35);
        Assert.True(r.Ready);
        Assert.NotEmpty(r.Suggestion!.Notes);
        Assert.Contains(r.Suggestion.Notes, n => n.Contains("usually runs between"));
        Assert.Contains(r.Suggestion.Notes, n => n.Contains("90 °C"));
    }

    [Theory]
    [InlineData(0)] [InlineData(0.2)] [InlineData(0.35)] [InlineData(0.5)] [InlineData(0.8)] [InlineData(1)]
    public void Every_suggestion_obeys_the_manual_curve_rules_whatever_the_machine_and_preference(double preference)
    {
        foreach (var idle in new[] { 25.0, 35, 45, 55, 65, 75, 88 })
            foreach (var load in new[] { 30.0, 60, 80, 95 })
            {
                var r = CurveAdvisor.Suggest(Watched(2.5, idle, load), preference);
                Assert.True(r.Ready);
                Assert.Null(ManualCurveRules.Check(r.Suggestion!.Cpu));
                Assert.Null(ManualCurveRules.Check(r.Suggestion.Case));
                Assert.Equal(100, new FanCurve(r.Suggestion.Cpu).Evaluate(90));
            }
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(-5)] [InlineData(99)] [InlineData(double.PositiveInfinity)]
    public void A_wild_preference_is_clamped_and_still_safe(double preference)
    {
        var r = CurveAdvisor.Suggest(Watched(3), preference);
        Assert.Null(ManualCurveRules.Check(r.Suggestion!.Cpu));
    }

    [Fact]
    public void A_cooler_preference_means_faster_fans_at_the_same_temperature()
    {
        var profile = Watched(3);
        var quiet = new FanCurve(CurveAdvisor.Suggest(profile, 0).Suggestion!.Cpu);
        var cool = new FanCurve(CurveAdvisor.Suggest(profile, 1).Suggestion!.Cpu);
        foreach (var t in new[] { 35.0, 50, 60, 70, 80 }) Assert.True(cool.Evaluate(t) >= quiet.Evaluate(t), $"at {t} °C");
        Assert.True(cool.Evaluate(60) > quiet.Evaluate(60));
    }

    [Fact]
    public void A_hotter_running_pc_gets_its_floor_held_to_a_higher_temperature()
    {
        double Knee(double idle) { var pts = CurveAdvisor.Suggest(Watched(3, idle, idle + 10), 0.35).Suggestion!.Cpu; return pts[1].TempC; }
        Assert.True(Knee(60) > Knee(35));
    }

    [Theory]
    [InlineData(0)] [InlineData(0.25)] [InlineData(0.5)] [InlineData(1)]
    public void A_suggestion_never_shows_a_speed_the_service_would_not_run(double preference)
    {
        var s = CurveAdvisor.Suggest(Watched(3, 40, 55), preference).Suggestion!;
        Assert.True(s.Cpu.All(p => p.Percent >= ReleaseRules.CpuFloor), "CPU fan floor");
        Assert.True(s.Case.All(p => p.Percent >= ReleaseRules.SystemFloor), "system fan floor");
        Assert.Null(ManualCurveRules.Check(s.Cpu));
        Assert.Null(ManualCurveRules.Check(s.Case));
    }

    [Fact]
    public void The_fans_never_go_below_the_floor_of_the_built_in_modes()
    {
        var pts = CurveAdvisor.Suggest(Watched(3, 30, 40), 0).Suggestion!.Cpu;
        Assert.True(pts.All(p => p.Percent >= 20));
    }

    [Fact]
    public void Describe_reads_naturally()
    {
        var text = CurveAdvisor.Describe([new CurvePoint(30, 20), new CurvePoint(50, 20), new CurvePoint(70, 50), new CurvePoint(90, 100)]);
        Assert.Equal("20% up to 50 °C, 50% at 70 °C, 100% at 90 °C", text);
    }

    [Fact]
    public void A_missing_case_history_falls_back_on_the_cpu_one()
    {
        var p = new ThermalProfile();
        for (var i = 0; i < 1000; i++) p.Add(T0.AddSeconds(i * 10), 50, null);
        Assert.True(CurveAdvisor.Suggest(p, 0.5).Ready);
    }
}
