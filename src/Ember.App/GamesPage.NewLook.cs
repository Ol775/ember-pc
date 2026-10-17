using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ember.Core;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>The redesigned Games page: a heading with the live game chip, the per-game switch, and a card for each launcher with a cover tile and a mode picker for every game.</summary>
public sealed partial class GamesPage
{
    readonly Border _modeCard = new();
    readonly StackPanel _columns = new();
    readonly Grid _grid = new();
    readonly StackPanel _left = new() { Margin = new Thickness(0, 0, 8, 0) }, _right = new() { Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _chipText = Ui.Muted("No game running", 13);
    Border? _liveChip;
    DateTimeOffset _logoScan;

    void BuildNewRoot()
    {
        _root.Margin = new Thickness(32, 30, 32, 28);
        _root.Children.Clear();
        (_scan.Parent as Panel)?.Children.Remove(_scan);   // the classic heading row held it

        // Heading: "Games" with its Beta tag, a sentence under it; on the right what is running now and Scan now.
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Fill = ThemeManager.Good };
        var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
        chipRow.Children.Add(dot);
        chipRow.Children.Add(_chipText);
        _liveChip = new Border { CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 6), Child = chipRow, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        _liveChip.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        _liveChip.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        _scan.Margin = new Thickness(0);
        _scan.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(_liveChip);
        right.Children.Add(_scan);

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(right, Dock.Right);
        head.Children.Add(right);
        var titles = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var h = Ui.Text("Games", 28, bold: true);
        h.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetHeadingLevel(h, AutomationHeadingLevel.Level2);
        title.Children.Add(h);
        var beta = BetaChip("Game detection is still being proven on real hardware.");
        beta.Margin = new Thickness(12, 4, 0, 0);
        title.Children.Add(beta);
        titles.Children.Add(title);
        titles.Children.Add(Ui.Muted("Games found in your launchers. Ember switches the fans sooner for these, even in a window.", 14).WithMargin(0, 4, 0, 0));
        titles.Children.Add(_status.WithMargin(0, 2, 0, 0));
        head.Children.Add(titles);
        _root.Children.Add(head);

        _detectionOff.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        _detectionOff.Margin = new Thickness(0, 0, 0, 12);
        _root.Children.Add(_detectionOff);

        // The per-game switch card.
        _modeCard.Child = _profileBox;
        _root.Children.Add(_modeCard);
        _modeCard.Margin = new Thickness(0, 0, 0, 16);

        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        Grid.SetColumn(_right, 1);
        _grid.Children.Add(_left);
        _grid.Children.Add(_right);
        _root.Children.Add(_grid);
    }

    static string[] LauncherOrder => GameLibraryScan.Launchers;

    void UpdateNew(GameLibrary library, bool scanning, string? activeGame)
    {
        _chipText.Text = activeGame is null ? "No game running" : "Playing: " + activeGame;
        if (_liveChip?.Child is StackPanel { Children: [System.Windows.Shapes.Ellipse e, ..] }) e.Fill = activeGame is null ? Ui.Res("MutedBrush") : ThemeManager.Good;

        if (library.ScannedAt != _logoScan) { _logoScan = library.ScannedAt; GameLogos.Forget(); }
        var key = scanning + "|" + library.ScannedAt.Ticks + "|" + library.Games.Count + "|" + _history.Version + "|" + _profiles.Enabled + string.Join(";", _profiles.Modes.Select(kv => kv.Key + kv.Value)) + string.Join(";", (_profiles.Scenes ?? new Dictionary<string, string>()).Select(kv => kv.Key + "=" + kv.Value)) + "|" + string.Join(",", _sceneNames());
        if (key == _key) return;
        _key = key;

        // "Per-game fan mode and lights": the switch card.
        var row = new DockPanel();
        var sw = Switch("", _profiles.Enabled, on => Change(_profiles.WithEnabled(on)));
        sw.Margin = new Thickness(0);
        sw.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(sw, "Use a fan mode and a lighting scene for each game");
        DockPanel.SetDock(sw, Dock.Right);
        row.Children.Add(sw);
        var text = new StackPanel();
        text.Children.Add(Ui.Text("Per-game fan mode and lights", 15, bold: true));
        text.Children.Add(Ui.Muted(_profiles.Enabled
            ? "Give a game its own fan mode and lighting scene. They apply when the game starts and end with it."
            : "Off. Every game uses the game mode from the Fans page. Switch on to give single games their own fan mode and lighting scene.", 13).WithMargin(0, 2, 0, 0));
        row.Children.Add(text);
        _modeCard.Child = row;
        _modeCard.CornerRadius = new CornerRadius(18);
        _modeCard.BorderThickness = new Thickness(1);
        _modeCard.Padding = new Thickness(20, 16, 20, 16);
        _modeCard.SetResourceReference(Border.BackgroundProperty, "SoftCardBrush");
        _modeCard.SetResourceReference(Border.BorderBrushProperty, "SoftCardBorderBrush");

        _left.Children.Clear();
        _right.Children.Clear();
        if (library.Games.Count == 0)
        {
            if (!scanning && library.ScannedAt != DateTimeOffset.MinValue)
                _left.Children.Add(Ui.Card(Ui.Muted("No games found. Ember looked for: " + string.Join(", ", LauncherOrder) + ". Games from other stores are still found when they run full screen.", 13)));
            return;
        }
        var groups = library.Games.GroupBy(g => g.Launcher).OrderBy(g => Array.IndexOf(LauncherOrder, g.Key)).ToList();
        // The biggest store stands alone on the left; the rest share the right column.
        var first = groups.OrderByDescending(g => g.Count()).First();
        _left.Children.Add(WithMargin(LauncherCard(first.Key.ToUpperInvariant() + " · " + (first.Count() == 1 ? "1 GAME" : first.Count() + " GAMES"), first, showLauncher: false), 0, 0, 0, 16));
        var others = groups.Where(g => g != first).ToList();
        var epic = others.FirstOrDefault(g => g.Key.StartsWith("Epic", StringComparison.OrdinalIgnoreCase));
        if (epic is not null)
        {
            _right.Children.Add(WithMargin(LauncherCard(epic.Key.ToUpperInvariant() + " · " + (epic.Count() == 1 ? "1 GAME" : epic.Count() + " GAMES"), epic, showLauncher: false), 0, 0, 0, 16));
            others.Remove(epic);
        }
        if (others.Count > 0)
            _right.Children.Add(WithMargin(LauncherCard(string.Join(" · ", others.Select(g => g.Key.ToUpperInvariant())), others.SelectMany(g => g), showLauncher: true), 0, 0, 0, 16));
        var note = Ui.Muted(library.ScannedAt == DateTimeOffset.MinValue
            ? "Games not listed here are still found when they run full screen and keep the graphics card busy."
            : $"Games not listed here are still found when they run full screen and keep the graphics card busy. Last scan {library.ScannedAt.LocalDateTime:HH:mm}.", 13);
        _right.Children.Add(Ui.Card(note, new Thickness(20, 16, 20, 16)));
    }

    /// <summary>One launcher: a small capital heading, then a row for every game with its cover tile, name, programs and fan mode.</summary>
    FrameworkElement LauncherCard(string title, IEnumerable<LibraryGame> games, bool showLauncher)
    {
        var body = new StackPanel();
        games = games.ToList();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var launcher in games.Select(g => g.Launcher).Distinct())
            if (BrandLogos.For(launcher) is { } mark)
            {
                var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(mark), Stretch = Stretch.Uniform, Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
                heading.Children.Add(shape);
            }
        heading.Children.Add(Ui.Label(title));
        body.Children.Add(heading);
        var first = true;
        foreach (var g in games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
        {
            var line = new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 9, 0, 9), Margin = new Thickness(0, first ? 8 : 0, 0, 0) };
            line.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
            first = false;
            var row = new DockPanel();
            if (_profiles.Enabled)
            {
                var picker = ModePicker(g.Name);
                picker.Margin = new Thickness(10, 0, 0, 0);
                picker.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(picker, Dock.Right);
                row.Children.Add(picker);
                var scenes = ScenePicker(g.Name);
                scenes.Margin = new Thickness(10, 0, 0, 0);
                scenes.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(scenes, Dock.Right);
                row.Children.Add(scenes);
            }
            var cover = CoverTile(g);
            DockPanel.SetDock(cover, Dock.Left);
            row.Children.Add(cover);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Ui.Text(g.Name, 14, bold: true);
            name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(name);
            var played = _history.Describe(g.Name, DateTimeOffset.Now);
            var sub = Ui.Muted(showLauncher ? g.Launcher + " · " + played.ToLowerInvariant().Replace("last played", "played") : played, 12);
            sub.TextWrapping = TextWrapping.NoWrap; sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.ToolTip = string.Join("\n", g.Exes.Take(15)) + (g.Exes.Count > 15 ? "\n…" : "");
            texts.Children.Add(sub);
            row.Children.Add(texts);
            line.Child = row;
            body.Children.Add(line);
        }
        return Ui.Card(body, new Thickness(20, 16, 20, 8), name: title);
    }

    /// <summary>The picture of a game: Steam's cover, or its program icon on a dark tile, or (with neither) a gradient from its name.</summary>
    static Border CoverTile(LibraryGame g)
    {
        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        if (GameLogos.For(g) is not { } logo) { tile.Background = Cover(g.Name); return tile; }
        if (logo.IsIcon)
        {
            tile.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
            tile.Child = new Image { Source = logo.Image, Margin = new Thickness(5) };
        }
        else tile.Background = new ImageBrush(logo.Image) { Stretch = Stretch.UniformToFill };
        return tile;
    }

    /// <summary>A cover for a game that has no picture: a gradient whose colours come from its name, so the same game always looks the same.</summary>
    static Brush Cover(string name)
    {
        var hash = 17;
        foreach (var c in name) hash = unchecked(hash * 31 + c);
        var hue = (uint)hash % 360;
        var brush = new LinearGradientBrush(Hsl(hue, 0.62, 0.62), Hsl((hue + 45) % 360, 0.68, 0.52), 45);
        brush.Freeze();
        return brush;
    }

    static Color Hsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
