using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// The games Ember found in the launchers on this PC (Steam, Epic, GOG, Ubisoft Connect, EA app). A game on this list switches
/// fan game mode sooner, even in a window. Scanned when Ember starts, when this page is opened (if the last scan is old) and
/// on Scan now. It only reads; nothing here changes a game or a launcher.
/// </summary>
public sealed class GamesPage : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _list = new();
    readonly TextBlock _status = Text("", muted: true, size: 13);
    readonly TextBlock _now = Text("", size: 13);
    readonly Border _nowChip = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    readonly TextBlock _detectionOff = Text("Game detection is switched off. Turn on experimental features in Settings, Fan control, for Ember to react to games.", size: 13);
    readonly StackPanel _profileBox = new();
    Button _scan = null!;
    Func<Task> _scanNow = () => Task.CompletedTask;
    GameProfiles _profiles = GameProfiles.Default;
    Action<GameProfiles> _saveProfiles = _ => { };
    string? _key;

    public GamesPage()
    {
        Content = _root;
        Focusable = false;
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        _scan = Pill("Scan now", () => _ = _scanNow(), "Scan the game libraries now");
        _scan.Margin = new Thickness(0);
        DockPanel.SetDock(_scan, Dock.Right);
        head.Children.Add(_scan);
        _nowChip.Child = _now;
        _nowChip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        _now.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        DockPanel.SetDock(_nowChip, Dock.Right);
        _nowChip.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(_nowChip);
        var title = new TextBlock { Text = "Games", FontSize = 24, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(title);
        var beta = BetaChip("Game detection is still being proven on real hardware.");
        beta.Margin = new Thickness(10, 6, 0, 0);
        titleRow.Children.Add(beta);
        head.Children.Add(titleRow);
        _root.Children.Add(head);
        _root.Children.Add(_status);
        _status.Margin = new Thickness(0, 0, 0, 4);
        _root.Children.Add(Text("Games in your Steam, Epic Games, GOG, Ubisoft Connect and EA app libraries. Ember uses this list to switch the fans to your game mode sooner, and also when a game runs in a window. Games not listed here are still found when they run full screen and keep the graphics card busy.", muted: true, size: 13));
        _detectionOff.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        _root.Children.Add(WithMargin(_detectionOff, 0, 8, 0, 0));
        _root.Children.Add(WithMargin(_profileBox, 0, 12, 0, 0));
        _root.Children.Add(WithMargin(_list, 0, 12, 0, 0));
        ThemeManager.Changed += () => _key = null;
    }

    public void Attach(Func<Task> scanNow, GameProfiles profiles, Action<GameProfiles> saveProfiles)
    {
        _scanNow = scanNow;
        _profiles = profiles;
        _saveProfiles = saveProfiles;
    }

    void Change(GameProfiles p)
    {
        _profiles = p;
        _saveProfiles(p);
        _key = null;
    }

    static readonly FanMode[] Pickable = [FanMode.SuperQuiet, FanMode.Quiet, FanMode.Normal, FanMode.Performance, FanMode.Extreme, FanMode.Manual];

    /// <summary>A button showing the game's own fan mode; it opens a small list to change it.</summary>
    Button ModePicker(string game)
    {
        var current = _profiles.Modes.TryGetValue(game, out var m) ? m : (FanMode?)null;
        Button btn = null!;
        btn = Pill(current is { } c ? FansViewState.Label(c) : "Game mode", () =>
        {
            var box = new StackPanel();
            var pop = new System.Windows.Controls.Primitives.Popup { PlacementTarget = btn, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
            void Option(string label, FanMode? mode)
            {
                var o = Pill(label, () => { pop.IsOpen = false; Change(_profiles.With(game, mode)); }, label + " for " + game);
                o.Margin = new Thickness(0, 0, 0, 4);
                box.Children.Add(o);
            }
            Option("Default (game mode from Fans)", null);
            foreach (var mode in Pickable) Option(FansViewState.Label(mode), mode);
            var frame = new Border { Padding = new Thickness(8, 8, 8, 4), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = box, Margin = new Thickness(0, 4, 0, 0) };
            frame.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
            frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
            pop.Child = frame;
            pop.IsOpen = true;
        }, "Fan mode for " + game);
        btn.Margin = new Thickness(0, 0, 10, 0);
        btn.ToolTip = "The fan mode used while this game runs. Default uses the game mode from the Fans page.";
        return btn;
    }

    public void Update(GameLibrary library, bool scanning, string? activeGame, bool detectionOn)
    {
        _detectionOff.Visibility = detectionOn ? Visibility.Collapsed : Visibility.Visible;
        _nowChip.Visibility = activeGame is null ? Visibility.Collapsed : Visibility.Visible;
        _now.Text = activeGame is null ? "" : "Playing: " + activeGame;
        _scan.IsEnabled = !scanning;
        _scan.Content = scanning ? "Scanning…" : "Scan now";
        _status.Text = scanning ? "Scanning your game libraries…"
            : library.ScannedAt == DateTimeOffset.MinValue ? "Not scanned yet."
            : $"{library.Games.Count} {(library.Games.Count == 1 ? "game" : "games")} found. Last scan {library.ScannedAt.LocalDateTime:HH:mm}.";

        var key = scanning + "|" + library.ScannedAt.Ticks + "|" + library.Games.Count + "|" + _profiles.Enabled + string.Join(";", _profiles.Modes.Select(kv => kv.Key + kv.Value));
        if (key == _key) return;
        _key = key;

        _profileBox.Children.Clear();
        var sw = Switch("Use a fan mode for each game", _profiles.Enabled, on => Change(_profiles.WithEnabled(on)));
        sw.Margin = new Thickness(0, 0, 0, 2);
        _profileBox.Children.Add(sw);
        _profileBox.Children.Add(Text(_profiles.Enabled
            ? "Pick a fan mode on a game below. A game with no pick uses the game mode from the Fans page. The safety rules still apply."
            : "Off. Every game uses the game mode from the Fans page. Switch on to give single games their own fan mode.", muted: true, size: 13));

        _list.Children.Clear();
        if (library.Games.Count == 0)
        {
            if (!scanning && library.ScannedAt != DateTimeOffset.MinValue)
                _list.Children.Add(Text("No games found. Ember looked for: " + string.Join(", ", GameLibraryScan.Launchers) + ". Games from other stores are still found when they run full screen.", muted: true, size: 13));
            return;
        }
        foreach (var group in library.Games.GroupBy(g => g.Launcher).OrderBy(g => Array.IndexOf(GameLibraryScan.Launchers, g.Key)))
        {
            var rows = new StackPanel();
            foreach (var g in group.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            {
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                var exes = Text(g.Exes.Count == 1 ? g.Exes[0] : g.Exes[0] + " and " + (g.Exes.Count - 1) + " more", muted: true, size: 12);
                exes.ToolTip = string.Join("\n", g.Exes.Take(15)) + (g.Exes.Count > 15 ? "\n…" : "");
                DockPanel.SetDock(exes, Dock.Right);
                row.Children.Add(exes);
                if (_profiles.Enabled)
                {
                    var picker = ModePicker(g.Name);
                    picker.Margin = new Thickness(10, 0, 10, 0);
                    DockPanel.SetDock(picker, Dock.Right);
                    row.Children.Add(picker);
                }
                var name = Text(g.Name, size: 14);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.TextWrapping = TextWrapping.NoWrap;
                row.Children.Add(name);
                rows.Children.Add(row);
            }
            _list.Children.Add(Panel($"{group.Key}  ·  {group.Count()}", rows, new Thickness(0, 0, 0, 12)));
        }
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
}
