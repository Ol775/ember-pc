using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ember.Core;

namespace Ember.App;

/// <summary>Settings, Schedule and alerts (beta builds): time windows that change the fan mode and lights, and Windows notifications when something is wrong.</summary>
public partial class MainWindow
{
    readonly ScheduleStore _scheduleStore = new(ScheduleStore.DefaultPath);
    readonly AlertStore _alertStore = new(AlertStore.DefaultPath);
    readonly ScheduleRunner _scheduleRunner = new();
    readonly AlertEngine _alertEngine = new();
    ScheduleSet _schedule = ScheduleSet.Default;
    AlertSettings _alerts = AlertSettings.Default;
    DispatcherTimer? _scheduleTimer;
    TextBlock? _scheduleStatus, _automationNote;
    StackPanel? _ruleList;
    Button? _addRule;

    static readonly (string Label, DayOfWeek Day)[] WeekDays =
        [("Mon", DayOfWeek.Monday), ("Tue", DayOfWeek.Tuesday), ("Wed", DayOfWeek.Wednesday), ("Thu", DayOfWeek.Thursday), ("Fri", DayOfWeek.Friday), ("Sat", DayOfWeek.Saturday), ("Sun", DayOfWeek.Sunday)];
    static readonly FanMode?[] ScheduleModes = [null, FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme];

    void HideAutomation()
    {
        NavAutomation.Visibility = Visibility.Collapsed;
        ScheduleBox.Tag = "off";
        AlertsBox.Tag = "off";
    }

    void BuildAutomation()
    {
        _schedule = _scheduleStore.Load();
        _alerts = _alertStore.Load();
        _automationNote = Caption("");

        // Schedule
        ScheduleHost.Children.Add(Caption("Change the fan mode or the lights while a time window lasts, for example quiet fans and dim lights at night. The mode and scene are put back when the window ends, unless you changed them yourself in between. Nothing changes while a game is running."));
        ScheduleHost.Children.Add(LightsUi.Switch("Use the schedule", _schedule.Enabled, on => { SetSchedule(_schedule with { Enabled = on }); RunSchedule(); }));
        _scheduleStatus = Caption("");
        ScheduleHost.Children.Add(_scheduleStatus);
        _ruleList = new StackPanel();
        ScheduleHost.Children.Add(_ruleList);
        _addRule = LightsUi.Pill("Add a window", AddRule);
        _addRule.HorizontalAlignment = HorizontalAlignment.Left;
        ScheduleHost.Children.Add(_addRule);
        ScheduleHost.Children.Add(_automationNote);
        RebuildRules();

        // Alerts
        AlertsHost.Children.Add(Caption("A Windows notification when a part stays hot or a fan that was turning stops. Fan control and its safety rules do not depend on this; it only tells you."));
        AlertsHost.Children.Add(LightsUi.Switch("Show alerts", _alerts.Enabled, on => SetAlerts(_alerts with { Enabled = on })));
        AlertsHost.Children.Add(LightsUi.Labelled("CPU alert at", AlertSettings.MinTempC, AlertSettings.MaxTempC, _alerts.CpuTempC, v => $"{v:0} °C", v => SetAlerts(_alerts with { CpuTempC = (int)v }), 1));
        AlertsHost.Children.Add(LightsUi.Labelled("Graphics card alert at", AlertSettings.MinTempC, AlertSettings.MaxTempC, _alerts.GpuTempC, v => $"{v:0} °C", v => SetAlerts(_alerts with { GpuTempC = (int)v }), 1));
        AlertsHost.Children.Add(LightsUi.Switch("Tell me when a fan stops", _alerts.FanStopped, on => SetAlerts(_alerts with { FanStopped = on })));
        var test = LightsUi.Pill("Send a test alert", () => _tray.Notify("Ember test alert", "This is how an Ember alert looks.", () => Dispatcher.InvokeAsync(ShowFromTray), System.Windows.Forms.ToolTipIcon.Warning));
        test.HorizontalAlignment = HorizontalAlignment.Left;
        AlertsHost.Children.Add(test);
        AlertsHost.Children.Add(Caption("Limits are in °C. A part must stay over its limit for a few seconds, and the same alert is not repeated for ten minutes. A fan on a cool PC may stop on purpose (zero-RPM mode), so a stopped fan is only reported when the PC is warm and the fan had been turning."));

        _scheduleTimer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) => RunSchedule(), Dispatcher);
        _scheduleTimer.Start();
    }

    void SetSchedule(ScheduleSet set)
    {
        _schedule = set;
        _automationNote!.Text = _scheduleStore.TrySave(set) ? "" : "Could not save the schedule; it applies until you close Ember.";
        UpdateScheduleStatus();
    }

    void SetAlerts(AlertSettings alerts)
    {
        _alerts = alerts;
        _automationNote!.Text = _alertStore.TrySave(alerts) ? "" : "Could not save the alert settings; they apply until you close Ember.";
    }

    void UpdateScheduleStatus()
    {
        if (_scheduleStatus is null) return;
        _scheduleStatus.Text = !_schedule.Enabled ? "The schedule is off."
            : _detection.ActiveGame is not null ? "A game is running, so the schedule waits until it ends."
            : _scheduleRunner.ActiveName is { } name ? $"Active now: {name}."
            : "No window is active now.";
    }

    void AddRule()
    {
        if (_schedule.Rules.Count >= ScheduleRule.MaxRules) return;
        var taken = _schedule.Rules.Select(r => r.Name).ToHashSet();
        var n = 1;
        while (taken.Contains("Night " + n)) n++;
        var rule = new ScheduleRule("Night " + n, new TimeOnly(22, 0), new TimeOnly(7, 0), Mode: FanMode.SuperQuiet);
        SetSchedule(_schedule with { Rules = [.. _schedule.Rules, rule] });
        RebuildRules();
    }

    void RemoveRule(int index)
    {
        var rules = _schedule.Rules.ToList();
        rules.RemoveAt(index);
        SetSchedule(_schedule with { Rules = rules });
        RebuildRules();
        RunSchedule();
    }

    void RebuildRules()
    {
        _ruleList!.Children.Clear();
        for (var i = 0; i < _schedule.Rules.Count; i++) _ruleList.Children.Add(RuleCard(i));
        _addRule!.IsEnabled = _schedule.Rules.Count < ScheduleRule.MaxRules;
        UpdateScheduleStatus();
    }

    void EditRule(int index, Func<ScheduleRule, ScheduleRule> change)
    {
        var rules = _schedule.Rules.ToList();
        rules[index] = change(rules[index]);
        SetSchedule(_schedule with { Rules = rules });
    }

    FrameworkElement RuleCard(int i)
    {
        var rule = _schedule.Rules[i];
        var card = new StackPanel();

        // name, on/off, remove
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var remove = LightsUi.Pill("Remove", () => RemoveRule(i), $"Remove the window {rule.Name}");
        DockPanel.SetDock(remove, Dock.Right);
        head.Children.Add(remove);
        var enabled = LightsUi.Switch("On", rule.Enabled, on => { EditRule(i, r => r with { Enabled = on }); RunSchedule(); });
        DockPanel.SetDock(enabled, Dock.Right);
        head.Children.Add(enabled);
        var name = new TextBox { Text = rule.Name, MaxLength = 60, Style = LightsUi.Sty("TextField"), Margin = new Thickness(0, 0, 12, 6) };
        System.Windows.Automation.AutomationProperties.SetName(name, "Name of the window");
        name.TextChanged += (_, _) => { if (name.Text.Trim().Length > 0) EditRule(i, r => r with { Name = name.Text.Trim() }); };
        head.Children.Add(name);
        card.Children.Add(head);

        // times
        var times = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        times.Children.Add(LightsUi.Text("From", muted: true));
        ((TextBlock)times.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        times.Children.Add(TimeBox("Start time, like 22:00", rule.Start, t => EditRule(i, r => r with { Start = t })));
        var to = LightsUi.Text("to", muted: true);
        to.VerticalAlignment = VerticalAlignment.Center;
        times.Children.Add(to);
        times.Children.Add(TimeBox("End time, like 07:00", rule.End, t => EditRule(i, r => r with { End = t })));
        card.Children.Add(times);

        // days
        var days = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (label, day) in WeekDays)
        {
            var bit = ScheduleRule.Bit(day);
            days.Children.Add(LightsUi.Switch(label, (rule.Days & bit) != 0, on => EditRule(i, r => r with { Days = on ? r.Days | bit : r.Days & ~bit })));
        }
        card.Children.Add(days);

        // fan mode
        var labels = ScheduleModes.Select(m => m is null ? "Fans unchanged" : FansViewState.Label(m.Value)).ToArray();
        card.Children.Add(LightsUi.Segments(labels, Math.Max(0, Array.IndexOf(ScheduleModes, rule.Mode)), k => EditRule(i, r => r with { Mode = ScheduleModes[k] }), "RuleMode" + i));

        // lights: unchanged, off, or one of the scenes
        var lights = LightOptions(rule);
        var current = Math.Max(0, lights.FindIndex(o => o.Scene == rule.Scene && o.Off == rule.LightsOff));
        Button? cycle = null;
        cycle = LightsUi.Pill(lights[current].Label, () =>
        {
            current = (current + 1) % lights.Count;
            var pick = lights[current];
            cycle!.Content = pick.Label;
            EditRule(i, r => r with { Scene = pick.Scene, LightsOff = pick.Off });
        }, "Lights during this window");
        cycle.HorizontalAlignment = HorizontalAlignment.Left;
        card.Children.Add(cycle);

        return Card(card);
    }

    List<(string Label, string? Scene, bool Off)> LightOptions(ScheduleRule rule)
    {
        var list = new List<(string, string?, bool)> { ("Lights unchanged (click to change)", null, false), ("Lights off", null, true) };
        var scenes = _lights.State.Scenes.Select(s => s.Name).ToList();
        if (rule.Scene is { } own && !scenes.Contains(own)) scenes.Add(own); // a scene that was deleted since stays visible
        list.AddRange(scenes.Select(s => ($"Scene: {s}", (string?)s, false)));
        return list;
    }

    TextBox TimeBox(string name, TimeOnly time, Action<TimeOnly> changed)
    {
        var box = new TextBox { Text = time.ToString("HH:mm"), Width = 70, MaxLength = 5, Style = LightsUi.Sty("TextField"), Margin = new Thickness(8, 0, 8, 0) };
        System.Windows.Automation.AutomationProperties.SetName(box, name);
        box.TextChanged += (_, _) =>
        {
            if (TimeOnly.TryParseExact(box.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                _automationNote!.Text = "";
                changed(t);
            }
            else _automationNote!.Text = "Type a time like 22:00.";
        };
        return box;
    }

    /// <summary>Applies a schedule window at its start and undoes it at its end. Waits while the service is not answering or a game is running.</summary>
    async void RunSchedule()
    {
        try
        {
            if (BuildFeatures.IsProduction || !_vm.Connected || _detection.ActiveGame is not null) { UpdateScheduleStatus(); return; }
            var lights = _lights.State;
            var actions = _scheduleRunner.Step(_schedule, DateTime.Now, new ScheduleView(_vm.FansPage.Mode, lights.CurrentScene, lights.On));
            if (actions.Any) DiagLog.Info("Schedule", $"{_scheduleRunner.ActiveName ?? "Window ended"}: mode {actions.Mode}, scene {actions.Scene}, lights {actions.LightsOn}");
            if (actions.Scene is { } scene) _lights.SelectScene(scene);
            if (actions.LightsOn is { } on) _lights.SetOn(on);
            if (actions.Mode is { } mode) await SendAsync(new SetMode(mode));
            UpdateScheduleStatus();
        }
        catch (Exception ex) { DiagLog.Info("Schedule", "Could not apply: " + ex.GetType().Name); }
    }

    /// <summary>Sends a Windows notification for each new problem in the latest readings (several at once become one message).</summary>
    void RunAlerts(DashboardSummary summary, bool stale)
    {
        if (BuildFeatures.IsProduction) return;
        var alerts = _alertEngine.Evaluate(DateTimeOffset.UtcNow, summary, stale, _alerts);
        if (alerts.Count == 0) return;
        foreach (var a in alerts) DiagLog.Info("Alert", a.Key);
        var title = alerts.Count == 1 ? alerts[0].Title : "Ember: " + alerts.Count + " problems";
        _tray.Notify(title, string.Join("\n", alerts.Select(a => a.Text)), () => Dispatcher.InvokeAsync(ShowFromTray), System.Windows.Forms.ToolTipIcon.Warning);
    }
}
