using Ember.Core;

namespace Ember.Core.Tests;

public class ScheduleTests
{
    // 2026-10-16 is a Friday.
    static DateTime At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);
    static ScheduleRule Night(FanMode? mode = FanMode.SuperQuiet, string? scene = null, bool off = false, int days = ScheduleRule.EveryDay) =>
        new("Night", new TimeOnly(22, 0), new TimeOnly(7, 0), days, mode, scene, off);
    static ScheduleSet Set(params ScheduleRule[] rules) => new(true, rules);

    [Fact]
    public void A_window_that_runs_past_midnight_covers_the_evening_and_the_small_hours()
    {
        var r = Night();
        Assert.True(r.Covers(At(16, 23)));
        Assert.True(r.Covers(At(17, 3)));
        Assert.False(r.Covers(At(17, 7)));
        Assert.False(r.Covers(At(16, 12)));
        Assert.True(r.Covers(At(16, 22)));
    }

    [Fact]
    public void The_small_hours_belong_to_the_day_the_window_started()
    {
        var fridayNight = Night(days: ScheduleRule.Bit(DayOfWeek.Friday));
        Assert.True(fridayNight.Covers(At(16, 23)));   // Friday evening
        Assert.True(fridayNight.Covers(At(17, 2)));    // Saturday small hours
        Assert.False(fridayNight.Covers(At(17, 23)));  // Saturday evening
        Assert.False(fridayNight.Covers(At(16, 2)));   // Friday small hours belong to Thursday
    }

    [Fact]
    public void A_daytime_window_and_a_disabled_or_empty_window()
    {
        var work = new ScheduleRule("Work", new TimeOnly(9, 0), new TimeOnly(17, 0), Mode: FanMode.Quiet);
        Assert.True(work.Covers(At(16, 9)));
        Assert.False(work.Covers(At(16, 17)));
        Assert.False((work with { Enabled = false }).Covers(At(16, 12)));
        Assert.False((work with { End = work.Start }).Covers(At(16, 9)));
        Assert.False((work with { Mode = null }).ChangesAnything);
    }

    [Fact]
    public void A_window_starting_changes_things_once_and_leaves_them_alone_afterwards()
    {
        var runner = new ScheduleRunner();
        var set = Set(Night(FanMode.SuperQuiet, "Night glow"));
        var evening = new ScheduleView(FanMode.Normal, "Warm glow", true);

        var start = runner.Step(set, At(16, 22), evening);
        Assert.Equal(new ScheduleActions(FanMode.SuperQuiet, "Night glow", null), start);
        Assert.Equal("Night", runner.ActiveName);

        // someone picks a mode by hand during the window: the schedule does not fight it
        Assert.False(runner.Step(set, At(16, 23), new ScheduleView(FanMode.Performance, "Night glow", true)).Any);
    }

    [Fact]
    public void A_window_ending_restores_only_what_it_changed_and_nobody_changed_since()
    {
        var runner = new ScheduleRunner();
        var set = Set(Night(FanMode.SuperQuiet, "Night glow"));
        runner.Step(set, At(16, 22), new ScheduleView(FanMode.Normal, "Warm glow", true));

        var morning = runner.Step(set, At(17, 7), new ScheduleView(FanMode.SuperQuiet, "Night glow", true));
        Assert.Equal(new ScheduleActions(FanMode.Normal, "Warm glow", null), morning);
        Assert.Null(runner.ActiveName);

        // a mode the person chose by hand is kept at the end
        runner.Step(set, At(17, 22), new ScheduleView(FanMode.Normal, "Warm glow", true));
        var kept = runner.Step(set, At(18, 7), new ScheduleView(FanMode.Extreme, "Night glow", true));
        Assert.Null(kept.Mode);
        Assert.Equal("Warm glow", kept.Scene);
    }

    [Fact]
    public void Lights_off_is_undone_only_if_the_schedule_turned_them_off()
    {
        var runner = new ScheduleRunner();
        var set = Set(Night(mode: null, off: true));
        var off = runner.Step(set, At(16, 22), new ScheduleView(FanMode.Normal, "A", true));
        Assert.Equal(false, off.LightsOn);
        Assert.Equal(true, runner.Step(set, At(17, 7), new ScheduleView(FanMode.Normal, "A", false)).LightsOn);

        // lights that were already off stay off at the end
        runner.Step(set, At(17, 22), new ScheduleView(FanMode.Normal, "A", false));
        Assert.False(runner.Step(set, At(18, 7), new ScheduleView(FanMode.Normal, "A", false)).Any);
    }

    [Fact]
    public void Nothing_is_asked_for_a_value_that_is_already_right()
    {
        var runner = new ScheduleRunner();
        var set = Set(Night(FanMode.Quiet));
        Assert.False(runner.Step(set, At(16, 22), new ScheduleView(FanMode.Quiet, "A", true)).Any);
        Assert.Equal("Night", runner.ActiveName);
        Assert.False(runner.Step(set, At(17, 7), new ScheduleView(FanMode.Quiet, "A", true)).Any); // nothing was changed, so nothing to undo
    }

    [Fact]
    public void Switching_the_schedule_off_ends_the_window_and_restores()
    {
        var runner = new ScheduleRunner();
        var rules = new[] { Night(FanMode.SuperQuiet) };
        runner.Step(new ScheduleSet(true, rules), At(16, 23), new ScheduleView(FanMode.Normal, "A", true));
        var back = runner.Step(new ScheduleSet(false, rules), At(16, 23, 5), new ScheduleView(FanMode.SuperQuiet, "A", true));
        Assert.Equal(FanMode.Normal, back.Mode);
    }

    [Fact]
    public void The_first_rule_wins_when_windows_overlap()
    {
        var a = new ScheduleRule("A", new TimeOnly(20, 0), new TimeOnly(23, 0), Mode: FanMode.Quiet);
        var b = new ScheduleRule("B", new TimeOnly(21, 0), new TimeOnly(23, 0), Mode: FanMode.Extreme);
        Assert.Equal("A", Set(a, b).ActiveAt(At(16, 21, 30))!.Name);
        Assert.Equal("B", Set(b, a).ActiveAt(At(16, 21, 30))!.Name);
    }

    [Fact]
    public void Rules_are_saved_and_read_back_and_damage_is_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-sched-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ScheduleStore(path);
            Assert.Equal(ScheduleSet.Default, store.Load());
            var set = new ScheduleSet(true, [Night(FanMode.Quiet, "Night glow", true, ScheduleRule.Bit(DayOfWeek.Monday))]);
            Assert.True(store.TrySave(set));
            var back = store.Load();
            Assert.True(back.Enabled);
            var rule = Assert.Single(back.Rules);
            Assert.Equal(set.Rules[0], rule);

            File.WriteAllText(path, "{\"Enabled\":true,\"Rules\":[{\"Name\":\"x\",\"Start\":\"25:99\",\"End\":\"07:00\"},{\"Name\":\"ok\",\"Start\":\"08:00\",\"End\":\"09:00\",\"Mode\":\"Ai\"},7]}");
            var odd = store.Load();
            var kept = Assert.Single(odd.Rules);
            Assert.Equal("ok", kept.Name);
            Assert.Null(kept.Mode); // the AI mode cannot be scheduled

            File.WriteAllText(path, "not json");
            Assert.Equal(ScheduleSet.Default, store.Load());
        }
        finally { File.Delete(path); }
    }
}

public class AlertTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    static AlertSettings On(int cpu = 85, int gpu = 85, bool fan = true) => new(true, cpu, gpu, fan);
    static DashboardSummary Sum(double? cpu = 50, double? gpu = 50, params FanRow[] fans) => new(cpu, gpu, null, null, fans);

    [Fact]
    public void A_hot_part_is_reported_once_after_it_stays_hot()
    {
        var e = new AlertEngine();
        Assert.Empty(e.Evaluate(T0, Sum(cpu: 90), false, On()));                       // just reached
        var alerts = e.Evaluate(T0 + AlertEngine.HotFor, Sum(cpu: 91), false, On());
        var a = Assert.Single(alerts);
        Assert.Equal("cpu-hot", a.Key);
        Assert.Contains("91", a.Text);
        Assert.Empty(e.Evaluate(T0 + AlertEngine.HotFor + TimeSpan.FromSeconds(30), Sum(cpu: 92), false, On())); // not again
    }

    [Fact]
    public void A_brief_spike_is_not_reported()
    {
        var e = new AlertEngine();
        e.Evaluate(T0, Sum(cpu: 95), false, On());
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromSeconds(2), Sum(cpu: 60), false, On()));
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromSeconds(8), Sum(cpu: 60), false, On()));
    }

    [Fact]
    public void It_alerts_again_after_cooling_down_and_after_ten_minutes()
    {
        var e = new AlertEngine();
        e.Evaluate(T0, Sum(gpu: 90), false, On());
        Assert.Single(e.Evaluate(T0 + AlertEngine.HotFor, Sum(gpu: 90), false, On()));
        e.Evaluate(T0 + TimeSpan.FromMinutes(1), Sum(gpu: 60), false, On()); // cooled
        var later = T0 + TimeSpan.FromMinutes(2);
        e.Evaluate(later, Sum(gpu: 90), false, On());
        Assert.Single(e.Evaluate(later + AlertEngine.HotFor, Sum(gpu: 90), false, On()));

        // still hot ten minutes on: a reminder
        Assert.Single(e.Evaluate(later + AlertEngine.HotFor + AlertEngine.Repeat, Sum(gpu: 90), false, On()));
    }

    [Fact]
    public void A_reading_hovering_at_the_limit_does_not_flicker()
    {
        var e = new AlertEngine();
        e.Evaluate(T0, Sum(cpu: 86), false, On());
        Assert.Single(e.Evaluate(T0 + AlertEngine.HotFor, Sum(cpu: 86), false, On()));
        // 83 is under the limit but within the cooling margin, so it is still the same problem
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromSeconds(20), Sum(cpu: 83), false, On()));
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromSeconds(30), Sum(cpu: 86), false, On()));
    }

    [Fact]
    public void A_fan_that_was_turning_and_stopped_while_warm_is_reported()
    {
        var e = new AlertEngine();
        var spinning = Sum(70, 50, new FanRow("Fan #1", 900));
        var stopped = Sum(70, 50, new FanRow("Fan #1", 0));
        e.Evaluate(T0, spinning, false, On());
        e.Evaluate(T0 + TimeSpan.FromSeconds(1), stopped, false, On());
        var a = Assert.Single(e.Evaluate(T0 + TimeSpan.FromSeconds(1) + AlertEngine.StoppedFor, stopped, false, On()));
        Assert.Equal("fan-stopped:Fan #1", a.Key);
        Assert.Contains("Fan #1", a.Text);
    }

    [Fact]
    public void A_fan_that_never_turned_or_a_cool_PC_with_stopped_fans_is_not_reported()
    {
        var e = new AlertEngine();
        var neverTurned = Sum(70, 70, new FanRow("Empty header", 0));
        e.Evaluate(T0, neverTurned, false, On());
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromMinutes(1), neverTurned, false, On()));

        e.Evaluate(T0, Sum(40, 40, new FanRow("GPU fan", 1200)), false, On());
        var idle = Sum(40, 35, new FanRow("GPU fan", 0)); // zero-RPM idle on a cool PC
        e.Evaluate(T0 + TimeSpan.FromSeconds(1), idle, false, On());
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromMinutes(2), idle, false, On()));
    }

    [Fact]
    public void Nothing_is_reported_when_alerts_are_off_or_the_data_is_stale()
    {
        var e = new AlertEngine();
        var hot = Sum(cpu: 99);
        e.Evaluate(T0, hot, false, new AlertSettings(false));
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromMinutes(1), hot, false, new AlertSettings(false)));
        e.Evaluate(T0, hot, true, On());
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromMinutes(1), hot, true, On()));
        Assert.Empty(e.Evaluate(T0 + TimeSpan.FromMinutes(1), Sum(70, 50, new FanRow("F", 0)), false, On(fan: false)));
    }

    [Fact]
    public void Settings_are_saved_clamped_and_survive_damage()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-alerts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new AlertStore(path);
            Assert.Equal(AlertSettings.Default, store.Load());
            Assert.True(store.TrySave(new AlertSettings(true, 80, 90, false)));
            Assert.Equal(new AlertSettings(true, 80, 90, false), store.Load());
            File.WriteAllText(path, "{\"Enabled\":true,\"CpuTempC\":5,\"GpuTempC\":999}");
            Assert.Equal(new AlertSettings(true, AlertSettings.MinTempC, AlertSettings.MaxTempC, true), store.Load());
            File.WriteAllText(path, "{{{");
            Assert.Equal(AlertSettings.Default, store.Load());
        }
        finally { File.Delete(path); }
    }
}
