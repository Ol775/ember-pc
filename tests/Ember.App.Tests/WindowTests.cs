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
}
