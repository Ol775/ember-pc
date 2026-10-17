using System.Windows;
using System.Windows.Controls;

namespace Ember.App.Tests;

public class WindowTests
{
    static readonly (string Name, Func<RadioButton> Nav)[] Pages =
    [
        ("Dashboard", () => UiHost.Window.NavDashboard), ("Fans", () => UiHost.Window.NavFans), ("Lights", () => UiHost.Window.NavLights),
        ("Devices", () => UiHost.Window.NavDevices), ("Games", () => UiHost.Window.NavGames), ("Power", () => UiHost.Window.NavPower),
        ("Specs", () => UiHost.Window.NavSpecs), ("Settings", () => UiHost.Window.NavSettings),
    ];

    static void Open(string name)
    {
        var nav = Pages.First(p => p.Name == name).Nav;
        if (name == "Games") UiHost.FeedGames();
        if (name == "Specs") UiHost.Run(() => UiHost.Window.Specs.LoadAsync()).Wait(TimeSpan.FromSeconds(60));
        UiHost.Run(() => nav().IsChecked = true);
        UiHost.Feed();
        UiHost.Settle();
    }

    [UiFact]
    public void Every_page_opens_and_draws_without_errors()
    {
        
        foreach (var (name, _) in Pages)
        {
            if (name == "Games" && !UiHost.Run(() => UiHost.Window.NavGames.IsVisible)) continue;
            Open(name);
            UiHost.Shot(name);
            var drawn = UiHost.Run(() => UiHost.Window.ActualWidth > 0 && UiHost.Window.Content is FrameworkElement { ActualHeight: > 0 });
            Assert.True(drawn, name + " did not draw");
        }
        UiHost.ThrowIfErrors();
    }

    [UiFact]
    public void Every_page_draws_at_other_window_sizes()
    {
        foreach (var (w, h) in new[] { (1040, 600), (1100, 700), (1500, 900), (1920, 1040) })
        {
            UiHost.Run(() => { UiHost.Window.Width = w; UiHost.Window.Height = h; });
            foreach (var (name, _) in Pages)
            {
                if (name == "Specs" || (name == "Games" && !UiHost.Run(() => UiHost.Window.NavGames.IsVisible))) continue;
                Open(name);
                UiHost.Shot($"{name}-{w}x{h}");
            }
        }
        UiHost.Run(() => { UiHost.Window.Width = 1280; UiHost.Window.Height = 800; });
        UiHost.ThrowIfErrors();
    }

    [UiFact]
    public void Every_settings_section_opens()
    {
        Open("Settings");
        foreach (var tag in new[] { "general", "appearance", "fan", "lights", "automation", "changelog", "legal", "updates", "about" })
        {
            var radio = UiHost.Run(() => FindSection(tag));
            if (radio is null) continue;
            UiHost.Run(() => radio.IsChecked = true);
            UiHost.Settle();
            UiHost.Shot("Settings-" + tag);
        }
        UiHost.ThrowIfErrors();
    }

    static RadioButton? FindSection(string tag)
    {
        var panel = (Panel)UiHost.Window.SetGeneral.Parent;
        return panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.Tag as string == tag && r.Visibility == Visibility.Visible);
    }

    [UiFact]
    public void Visiting_every_page_again_and_again_does_not_grow_memory()
    {
        
        void Lap()
        {
            foreach (var (name, _) in Pages)
            {
                if (name == "Games" && !UiHost.Run(() => UiHost.Window.NavGames.IsVisible)) continue;
                Open(name);
            }
        }
        for (var i = 0; i < 5; i++) Lap();   // warm up: pages build, fonts and templates load
        long Used() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
        var before = Used();
        for (var i = 0; i < 40; i++) Lap();
        var after = Used();
        var grown = (after - before) / 1024.0 / 1024.0;
        Assert.True(grown < 6, $"Managed memory grew by {grown:0.0} MB over 40 laps of every page");
        UiHost.ThrowIfErrors();
    }
    [UiFact]
    public void The_power_page_draws_each_range_from_history()
    {
        if (!Ember.Core.BuildFeatures.NewLook) return;   // the range buttons belong to the new look
        var window = UiHost.Window;
        var start = DateTimeOffset.UtcNow.AddMinutes(-70);
        UiHost.Run(() =>
        {
            for (var i = 0; i < 70 * 60; i++)
            {
                var wave = Math.Sin(i / 90.0) * 30 + Math.Sin(i / 11.0) * 8;
                window.Power.Feed(start.AddSeconds(i), new Ember.Core.PowerSummary(70 + wave, 26 + wave / 4, null));
            }
        });
        Open("Power");
        var segments = UiHost.Run(() => FindAll<System.Windows.Controls.RadioButton>(window.Power).Where(r => r.GroupName == "PowerRange").ToList());
        Assert.Equal(4, segments.Count);
        for (var i = 0; i < segments.Count; i++)
        {
            var radio = segments[i];
            UiHost.Run(() => { radio.IsChecked = true; radio.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); });
            UiHost.Settle();
            UiHost.Shot("Power-range-" + i);
        }
        UiHost.ThrowIfErrors();
    }

    static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();
        void Walk(DependencyObject o)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(o, i);
                if (c is T t) found.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return found;
    }
}
