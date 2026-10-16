using System.Diagnostics;
using System.Windows;
using Xunit.Abstractions;

namespace Ember.App.Tests;

/// <summary>How much processor time and memory the window uses while it just sits there, with a reading arriving every second as the service sends them.</summary>
public class PerformanceTests(ITestOutputHelper output)
{
    static double CpuPercent(Action work, TimeSpan duration)
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var cpu0 = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        work();
        wall.Stop();
        process.Refresh();
        return (process.TotalProcessorTime - cpu0).TotalMilliseconds / wall.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
    }

    static void Sit(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        var i = 0;
        while (DateTime.UtcNow < end)
        {
            UiHost.Feed(cpuTemp: 55 + i % 7, cpuW: 60 + i % 20);
            Thread.Sleep(1000);
            i++;
        }
    }

    [UiFact]
    public void The_window_is_quiet_when_nothing_happens()
    {
        var window = UiHost.Window;
        UiHost.Run(() => window.NavDashboard.IsChecked = true);
        Sit(TimeSpan.FromSeconds(3));   // settle

        var visible = CpuPercent(() => Sit(TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(20));
        output.WriteLine($"Dashboard on screen: {visible:0.00} % of the machine's processor");

        UiHost.Run(() => window.NavPower.IsChecked = true);
        var power = CpuPercent(() => Sit(TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(20));
        output.WriteLine($"Power page on screen: {power:0.00} %");

        UiHost.Run(() => window.NavDashboard.IsChecked = true);
        UiHost.Run(() => window.Hide());
        var hidden = CpuPercent(() => Sit(TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(20));
        output.WriteLine($"Window hidden in the tray: {hidden:0.00} %");
        UiHost.Run(() => window.Show());

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        output.WriteLine($"Managed memory {GC.GetTotalMemory(true) / 1024.0 / 1024.0:0.0} MB, working set {Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0:0} MB");

        Assert.True(visible < 5, $"Dashboard used {visible:0.0} % of the processor");
        Assert.True(power < 5, $"Power page used {power:0.0} %");
        Assert.True(hidden < 2, $"The hidden window used {hidden:0.0} %");
        UiHost.ThrowIfErrors();
    }
}

public class RefreshCostTests(ITestOutputHelper output)
{
    [UiFact]
    public void One_redraw_of_each_page_is_cheap()
    {
        var window = UiHost.Window;
        foreach (var (name, nav) in new (string, Func<System.Windows.Controls.RadioButton>)[]
        {
            ("Dashboard", () => window.NavDashboard), ("Fans", () => window.NavFans), ("Lights", () => window.NavLights), ("Devices", () => window.NavDevices),
            ("Power", () => window.NavPower), ("Specs", () => window.NavSpecs), ("Settings", () => window.NavSettings),
        })
        {
            UiHost.Run(() => nav().IsChecked = true);
            UiHost.Feed();
            UiHost.Settle();
            var sw = Stopwatch.StartNew();
            const int N = 200;
            UiHost.Run(() => { for (var i = 0; i < N; i++) window.Refresh(); });
            output.WriteLine($"{name}: {sw.Elapsed.TotalMilliseconds / N:0.000} ms per redraw");
        }
    }
}

public class TrueIdleTests(ITestOutputHelper output)
{
    [UiFact]
    public void Nothing_runs_when_no_reading_arrives()
    {
        var window = UiHost.Window;
        foreach (var (name, nav) in new (string, Func<System.Windows.Controls.RadioButton>)[]
        {
            ("Dashboard", () => window.NavDashboard), ("Fans", () => window.NavFans), ("Lights", () => window.NavLights), ("Power", () => window.NavPower), ("Settings", () => window.NavSettings),
        })
        {
            UiHost.Run(() => nav().IsChecked = true);
            UiHost.Feed();
            Thread.Sleep(2000);
            var process = Process.GetCurrentProcess();
            process.Refresh();
            var cpu0 = process.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            Thread.Sleep(15000);
            process.Refresh();
            var percent = (process.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            output.WriteLine($"{name}, no readings: {percent:0.000} % of the machine");
        }
    }
}

public class WhereTheTimeGoesTests(ITestOutputHelper output)
{
    [UiFact]
    public void Time_spent_by_the_window_thread_by_kind_of_work()
    {
        var window = UiHost.Window;
        foreach (var (name, nav) in new (string, Func<System.Windows.Controls.RadioButton>)[]
        {
            ("Dashboard", () => window.NavDashboard), ("Fans", () => window.NavFans), ("Power", () => window.NavPower), ("Settings", () => window.NavSettings),
        })
        {
            UiHost.Run(() => nav().IsChecked = true);
            UiHost.Feed();
            Thread.Sleep(2000);
            var spent = new Dictionary<string, double>();
            var started = new Dictionary<System.Windows.Threading.DispatcherOperation, long>();
            var frames = 0;
            System.Windows.Threading.DispatcherHookEventHandler onStart = (_, e) => { lock (started) started[e.Operation] = Stopwatch.GetTimestamp(); };
            System.Windows.Threading.DispatcherHookEventHandler onDone = (_, e) =>
            {
                lock (started)
                    if (started.Remove(e.Operation, out var t0))
                    {
                        var key = e.Operation.Priority.ToString();
                        spent[key] = spent.GetValueOrDefault(key) + (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                    }
            };
            EventHandler onFrame = (_, _) => frames++;
            UiHost.Run(() =>
            {
                window.Dispatcher.Hooks.OperationStarted += onStart;
                window.Dispatcher.Hooks.OperationCompleted += onDone;
                System.Windows.Media.CompositionTarget.Rendering += onFrame;
            });
            Thread.Sleep(10000);
            UiHost.Run(() =>
            {
                window.Dispatcher.Hooks.OperationStarted -= onStart;
                window.Dispatcher.Hooks.OperationCompleted -= onDone;
                System.Windows.Media.CompositionTarget.Rendering -= onFrame;
            });
            output.WriteLine($"{name}: {frames} frames in 10 s; " + string.Join(", ", spent.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0} ms")));
        }
    }
}

public class ThreadsTests(ITestOutputHelper output)
{
    [UiFact]
    public void Which_threads_use_the_processor()
    {
        var window = UiHost.Window;
        foreach (var (name, nav) in new (string, Func<System.Windows.Controls.RadioButton>)[]
        {
            ("Dashboard", () => window.NavDashboard), ("Fans", () => window.NavFans), ("Power", () => window.NavPower), ("Settings", () => window.NavSettings),
        })
        {
            UiHost.Run(() => nav().IsChecked = true);
            UiHost.Feed();
            Thread.Sleep(3000);
            Dictionary<int, TimeSpan> Take() => Process.GetCurrentProcess().Threads.Cast<ProcessThread>().ToDictionary(t => t.Id, t => { try { return t.TotalProcessorTime; } catch { return TimeSpan.Zero; } });
            var before = Take();
            Thread.Sleep(10000);
            var after = Take();
            var top = after.Select(kv => (Id: kv.Key, Ms: (kv.Value - before.GetValueOrDefault(kv.Key)).TotalMilliseconds)).Where(x => x.Ms > 5).OrderByDescending(x => x.Ms).Take(6);
            output.WriteLine($"{name}: " + string.Join(", ", top.Select(x => $"thread {x.Id} {x.Ms:0} ms")) + $" (of 10000 ms)");
        }
    }
}

public class TimerExperiment(ITestOutputHelper output)
{
    [UiFact]
    public void Which_timer_costs_the_most()
    {
        var window = UiHost.Window;
        UiHost.Run(() => window.NavDashboard.IsChecked = true);
        UiHost.Feed();
        var timers = typeof(MainWindow).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(System.Windows.Threading.DispatcherTimer))
            .Select(f => (f.Name, Timer: (System.Windows.Threading.DispatcherTimer?)f.GetValue(window))).Where(t => t.Timer is not null).ToList();
        output.WriteLine("timers: " + string.Join(", ", timers.Select(t => $"{t.Name} every {t.Timer!.Interval.TotalMilliseconds:0} ms, running={t.Timer.IsEnabled}")));

        double Measure()
        {
            Thread.Sleep(1500);
            var p = Process.GetCurrentProcess();
            p.Refresh();
            var c0 = p.TotalProcessorTime;
            Thread.Sleep(8000);
            p.Refresh();
            return (p.TotalProcessorTime - c0).TotalMilliseconds / 8000 * 100;   // percent of one core
        }

        output.WriteLine($"everything running: {Measure():0.0} % of a core");
        UiHost.Run(() => { foreach (var t in timers) t.Timer!.Stop(); });
        output.WriteLine($"all window timers stopped: {Measure():0.0} % of a core");
        foreach (var t in timers)
        {
            UiHost.Run(() => t.Timer!.Start());
            output.WriteLine($"only {t.Name} on: {Measure():0.0} % of a core");
            UiHost.Run(() => t.Timer!.Stop());
        }
    }
}

public class LayoutCountTests(ITestOutputHelper output)
{
    [UiFact]
    public void How_often_does_layout_run_when_nothing_changes()
    {
        var window = UiHost.Window;
        foreach (var (name, nav) in new (string, Func<System.Windows.Controls.RadioButton>)[]
        {
            ("Dashboard", () => window.NavDashboard), ("Fans", () => window.NavFans), ("Power", () => window.NavPower), ("Settings", () => window.NavSettings),
        })
        {
            UiHost.Run(() => nav().IsChecked = true);
            UiHost.Feed();
            Thread.Sleep(2000);
            var count = 0;
            EventHandler onLayout = (_, _) => count++;
            UiHost.Run(() => window.LayoutUpdated += onLayout);
            Thread.Sleep(10000);
            UiHost.Run(() => window.LayoutUpdated -= onLayout);
            output.WriteLine($"{name}: layout ran {count} times in 10 s");
        }
    }
}

public class PowerLayoutBisect(ITestOutputHelper output)
{
    [UiFact]
    public void Which_part_of_the_power_update_causes_layout()
    {
        var window = UiHost.Window;
        UiHost.Run(() => window.NavPower.IsChecked = true);
        UiHost.Feed();
        Thread.Sleep(1500);
        var page = window.Power;
        foreach (var method in new[] { "UpdateHero", "UpdateCost", "DrawChart", "Update" })
        {
            var m = typeof(PowerPage).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!;
            var count = 0;
            EventHandler onLayout = (_, _) => count++;
            UiHost.Run(() => window.LayoutUpdated += onLayout);
            for (var i = 0; i < 10; i++) { UiHost.Run(() => m.Invoke(page, null)); Thread.Sleep(150); }
            UiHost.Run(() => window.LayoutUpdated -= onLayout);
            output.WriteLine($"{method}: {count} layouts in 10 calls");
        }
    }
}

public class UiThreadTimerExperiment(ITestOutputHelper output)
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    [UiFact]
    public void Ui_thread_time_with_each_timer_alone()
    {
        var window = UiHost.Window;
        UiHost.Run(() => window.NavDashboard.IsChecked = true);
        UiHost.Feed();
        var uiThreadId = (int)UiHost.Run(() => GetCurrentThreadId());
        var timers = typeof(MainWindow).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(System.Windows.Threading.DispatcherTimer))
            .Select(f => (f.Name, Timer: (System.Windows.Threading.DispatcherTimer?)f.GetValue(window))).Where(t => t.Timer is not null).ToList();

        double UiMs()
        {
            TimeSpan Now() => Process.GetCurrentProcess().Threads.Cast<ProcessThread>().First(t => t.Id == uiThreadId).TotalProcessorTime;
            Thread.Sleep(1000);
            var a = Now();
            Thread.Sleep(10000);
            return (Now() - a).TotalMilliseconds;
        }

        output.WriteLine($"all timers running: {UiMs():0} ms of UI thread time in 10 s");
        UiHost.Run(() => { foreach (var t in timers) t.Timer!.Stop(); });
        output.WriteLine($"no timer: {UiMs():0} ms");
        foreach (var t in timers)
        {
            UiHost.Run(() => t.Timer!.Start());
            output.WriteLine($"only {t.Name}: {UiMs():0} ms");
            UiHost.Run(() => t.Timer!.Stop());
        }
    }
}
