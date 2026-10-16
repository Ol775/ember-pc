using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ember.Core;
using Ember.Lighting;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// Devices and integrations: one card per source (sensors and fans, OpenRGB, Govee) and a table of every device Ember
/// knows about. It only reads state; nothing here changes hardware. Rebuilt only when what it shows changes.
/// </summary>
public sealed class DevicesPage : UserControl
{
    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    // Two columns, each row as tall as its tallest card (a UniformGrid would make every card as tall as the biggest one).
    readonly Grid _cards = new() { Margin = new Thickness(-6, 0, -6, 10) };
    readonly StackPanel _rows = new();
    string? _key;

    /// <summary>Integrations whose on/off lives somewhere else (Razer has its own setting): how to read and change it.</summary>
    public Dictionary<string, (Func<bool> Get, Action<bool> Set)> Overrides { get; } = [];

    public DevicesPage()
    {
        Content = _root;
        Focusable = false;
        _root.Children.Add(BuildHeading());
        _root.Children.Add(_cards);
        _root.Children.Add(Panel("All devices", _rows, new Thickness(0)));
        ThemeManager.Changed += () => _key = null;
    }

    public void Update(MainViewModel vm, LightingManager lights)
    {
        _lights = lights;
        var headers = vm.FansPage.Headers;
        var devices = lights.Devices;
        var status = lights.SourceStatus;
        var key = vm.FansPage.ServiceAvailable + "|" + string.Join(";", headers.Select(h => h.Id + h.Name + h.StateText)) + "#"
            + string.Join(";", devices.Select(d => d.Id + d.Name + d.Source + d.Disabled)) + "#" + string.Join(";", status)
            + "#" + string.Join(";", lights.Problems.Select(p => p.Key + p.Value));
        var streamDeck = BuildFeatures.IsProduction ? null : StreamDeckInstaller.Detect();
        key += "#" + streamDeck?.State + streamDeck?.Installed + streamDeck?.Bundled + _streamDeckNote + _serviceBusy + _serviceNote;
        if (key == _key) return;
        _key = key;

        var controllable = headers.Count(h => h.CanDisable);
        _cards.Children.Clear();
        _cards.Children.Add(Card("Sensors and fans", vm.FansPage.ServiceAvailable ? "Connected" : "Service not running", vm.FansPage.ServiceAvailable,
            "LibreHardwareMonitor, runs in the Ember service",
            $"{headers.Count} fan headers shown, {controllable} controlled by Ember" + (_serviceNote.Length > 0 ? "\n" + _serviceNote : ""),
            actions: BuildFeatures.NewLook ? null : ServiceActions()));
        _cards.Children.Add(Source("OpenRGB", "SDK server on localhost, started with Ember", devices, status, lights));
        _cards.Children.Add(Source("Govee", "LAN first; cloud only with your API key", devices, status, lights));
        _cards.Children.Add(Source("Logitech", "Keyboards and mice, through G HUB", devices, status, lights));
        _cards.Children.Add(Source("Hue", "Philips Hue lights, through the bridge on your network", devices, status, lights));
        _cards.Children.Add(Source("Nanoleaf", "Nanoleaf light panels, through the controller on your network", devices, status, lights));
        _cards.Children.Add(Source("Razer", "Razer Chroma devices, through Razer Synapse on this PC", devices, status, lights));
        _cards.Children.Add(Source("Corsair", "Corsair keyboards, mice, fans and more, through iCUE on this PC", devices, status, lights));
        _cards.Children.Add(Source("Meross", "Meross bulbs and light strips, found on your network", devices, status, lights));
        _cards.Children.Add(Source("Tapo", "Tapo bulbs and light strips, found on your network", devices, status, lights));
        if (streamDeck is not null) _cards.Children.Add(StreamDeckCard(streamDeck));

        LayOutCards();

        _rows.Children.Clear();
        _rows.Children.Add(Row("Name", "Source", "Type", "Status", header: true, use: Text("Identify and use", muted: true, size: 12)));
        foreach (var d in devices.OrderBy(d => d.Source).ThenBy(d => d.Name))
        {
            var problem = lights.Problems.TryGetValue(d.Id, out var p) ? p : null;
            var id = d.Id;
            var use = Switch("", !d.Disabled, on => lights.SetDeviceDisabled(id, !on));
            use.Margin = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(use, "Use " + d.Name + " in Ember");
            var identify = Pill("Identify", () => _ = lights.IdentifyAsync(id), "Identify " + d.Name);
            identify.Margin = new Thickness(0, 0, 14, 0);
            identify.ToolTip = "Flash this device white a few times so you can see which one it is";
            var useCell = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            useCell.Children.Add(identify);
            useCell.Children.Add(use);
            _rows.Children.Add(Row(d.Name, d.Source, "Lighting", d.Disabled ? "Disabled" : problem ?? (d.Realtime ? "Online" : "Online, slow (cloud)"), warn: d.Disabled || problem is not null || !d.Realtime, use: useCell));
        }
        if (_rows.Children.Count == 1) _rows.Children.Add(Text("Nothing found yet", muted: true));
    }

    string _serviceNote = "";
    bool _serviceBusy;

    /// <summary>A button that restarts the Ember services without closing the app (for after an update to the service or a stuck sensor).</summary>
    FrameworkElement ServiceActions()
    {
        var restart = Pill(_serviceBusy ? "Restarting…" : "Restart services", () => _ = RestartServicesAsync(), "Restart the Ember services");
        restart.IsEnabled = !_serviceBusy;
        restart.ToolTip = "Restarts the Ember service and watchdog. The fans go back to the board for a moment, and this window reconnects by itself.";
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(restart);
        return row;
    }

    async Task RestartServicesAsync()
    {
        _serviceBusy = true;
        _serviceNote = "Restarting the services…";
        _key = null;
        _serviceNote = await Task.Run(ServiceRestarter.Restart);
        _serviceBusy = false;
        _key = null;
    }

    void LayOutCards()
    {
        _cards.ColumnDefinitions.Clear();
        _cards.RowDefinitions.Clear();
        var columns = BuildFeatures.NewLook ? 3 : 2;
        for (var c = 0; c < columns; c++) _cards.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < (_cards.Children.Count + columns - 1) / columns; r++) _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < _cards.Children.Count; i++)
        {
            Grid.SetColumn(_cards.Children[i], i % columns);
            Grid.SetRow(_cards.Children[i], i / columns);
        }
    }

    FrameworkElement Source(string name, string blurb, IReadOnlyList<DeviceInfo> devices, IReadOnlyList<string> status, LightingManager lights)
    {
        var mine = devices.Where(d => d.Source.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var line = status.FirstOrDefault(s => s.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        var detail = line is null ? "Not available" : line[(name.Length + 1)..].Trim(' ', ':');
        var off = lights.IsSourceDisabled(name) || (Overrides.TryGetValue(name, out var o) && !o.Get());
        var toggle = Switch("", !off, on =>
        {
            if (Overrides.TryGetValue(name, out var ov)) ov.Set(on);
            else lights.SetSourceDisabled(name, !on);
            _key = null;
        });
        toggle.Margin = new Thickness(0, 0, 10, 0);
        AutomationProperties.SetName(toggle, "Use " + name + " in Ember");
        return Card(name, off ? "Off" : mine.Count > 0 ? "Connected" : "No devices", mine.Count > 0 && !off, blurb,
            off ? "Switched off in Ember." : mine.Count > 0 ? $"{mine.Count} {(mine.Count == 1 ? "device" : "devices")}: {string.Join(", ", mine.Take(4).Select(d => d.Name))}" : detail, toggle);
    }

    /// <summary>Integrations written and unit-tested but not yet tried on real devices: every build marks them Beta.</summary>
    static readonly HashSet<string> BetaIntegrations = ["Nanoleaf", "Razer", "Stream Deck", "Meross", "Tapo"];

    string _streamDeckNote = "";
    StreamDeckState? _streamDeckNoteState; // the card state the note was written in; the note goes away when the state moves on

    /// <summary>Beta builds only: installs or removes the Stream Deck plugin (keys for lights, fan modes and live temperatures).</summary>
    FrameworkElement StreamDeckCard(StreamDeckInstaller.Detected d)
    {
        if (_streamDeckNoteState is { } was && was != d.State) { _streamDeckNote = ""; _streamDeckNoteState = null; }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        void Act(string label, Func<string> action)
        {
            buttons.Children.Add(Pill(label, () => { _streamDeckNote = action(); _streamDeckNoteState = StreamDeckInstaller.Detect(true).State; _key = null; }, label + " the Stream Deck plugin"));
        }
        if (d.State == StreamDeckState.NotInstalled) Act("Install", StreamDeckInstaller.Install);
        if (d.State == StreamDeckState.NeedsUpdate) Act("Update", StreamDeckInstaller.Install);
        if (d.State is StreamDeckState.Installed or StreamDeckState.NeedsUpdate) Act("Remove", StreamDeckInstaller.Remove);
        var pill = d.State switch { StreamDeckState.Installed => "Installed", StreamDeckState.NeedsUpdate => "Update available", StreamDeckState.NotInstalled => "Not installed", _ => "Not found" };
        var detail = _streamDeckNote.Length > 0 ? _streamDeckNote : d.State switch
        {
            StreamDeckState.NotFound when d.Bundled is null => "The plugin file is not part of this installation.",
            StreamDeckState.NotFound => "The Stream Deck app from Elgato was not found on this PC.",
            StreamDeckState.NotInstalled => "Not in Stream Deck yet.",
            _ => "Plugin version " + d.Installed,
        };
        return Card("Stream Deck", pill, d.State == StreamDeckState.Installed, "Keys for lights, fan modes and live temperatures", detail, actions: buttons.Children.Count > 0 ? buttons : null);
    }

    static FrameworkElement BetaTag()
    {
        var tag = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Beta", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("AccentTextBrush") },
            ToolTip = "Written and tested in software, but not yet tried on real devices." };
        tag.Background = (Brush)Application.Current.FindResource("AccentSoftBrush");
        return tag;
    }

    LightingManager? _lights;

    FrameworkElement BuildHeading()
    {
        if (!BuildFeatures.NewLook)
        {
            var classic = new TextBlock { Text = "Devices and integrations", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
            AutomationProperties.SetHeadingLevel(classic, AutomationHeadingLevel.Level2);
            return classic;
        }
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Pill("Restart services", () => _ = RestartServicesAsync(), "Restart the Ember services"));
        var scan = Pill("Scan for devices", () => { _lights?.RefreshDevices(); _key = null; }, "Look for devices again");
        scan.Style = Sty("PrimaryButton");
        scan.Margin = new Thickness(0, 0, 0, 6);
        buttons.Children.Add(scan);
        DockPanel.SetDock(buttons, Dock.Right);
        head.Children.Add(buttons);
        var text = new StackPanel();
        var title = new TextBlock { Text = "Devices", FontSize = 24, FontWeight = FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        text.Children.Add(title);
        text.Children.Add(WithMargin(Text("Everything Ember can light or read. Switch an integration off and Ember lets go of it.", muted: true, size: 13), 0, 2, 0, 0));
        head.Children.Add(text);
        return head;
    }

    /// <summary>The mark of an integration: its logo where there is one, else its first letters, on a rounded tile.</summary>
    static FrameworkElement LogoTile(string title)
    {
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        tile.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        if (BrandLogos.For(title) is { } path)
        {
            var shape = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stretch = Stretch.Uniform, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            shape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextBrush");
            tile.Child = shape;
        }
        else
        {
            var letters = title switch { "Sensors and fans" => "Ss", "OpenRGB" => "Rg", "Govee" => "Gv", "Nanoleaf" => "Nl", "Meross" => "Me", _ => title[..Math.Min(2, title.Length)] };
            var t = new TextBlock { Text = letters, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            tile.Child = t;
        }
        return tile;
    }

    /// <summary>The redesigned card: logo, name and switch on top, the status as a small tag, then what it is and what it found.</summary>
    static FrameworkElement NewCard(string title, string pill, bool good, string blurb, string detail, FrameworkElement? toggle, FrameworkElement? actions)
    {
        var head = new DockPanel();
        var logo = LogoTile(title);
        DockPanel.SetDock(logo, Dock.Left);
        head.Children.Add(logo);
        if (toggle is not null) { toggle.Margin = new Thickness(0); toggle.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(toggle, Dock.Right); head.Children.Add(toggle); }
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Text(title, weight: FontWeights.SemiBold, size: 15));
        if (BetaIntegrations.Contains(title)) name.Children.Add(BetaTag());
        head.Children.Add(name);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0),
            Child = new TextBlock { Text = pill, FontSize = 11.5, FontWeight = FontWeights.SemiBold },
        };
        if (good) { chip.Background = ThemeManager.GoodSoft; ((TextBlock)chip.Child).Foreground = ThemeManager.Good; }
        else { chip.SetResourceReference(Border.BackgroundProperty, "TrackBrush"); ((TextBlock)chip.Child).SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); }

        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(chip);
        body.Children.Add(WithMargin(Text(blurb, muted: true, size: 12.5), 0, 8, 0, 0));
        if (detail.Length > 0) body.Children.Add(WithMargin(Text(detail, size: 12.5), 0, 4, 0, 0));
        if (actions is not null) body.Children.Add(WithMargin(actions, 0, 10, 0, 0));
        var box = Panel(title, body, new Thickness(6));
        box.Header = null;
        return box;
    }

    static FrameworkElement Card(string title, string pill, bool good, string blurb, string detail, FrameworkElement? toggle = null, FrameworkElement? actions = null)
    {
        if (BuildFeatures.NewLook) return NewCard(title, pill, good, blurb, detail, toggle, actions);
        var head = new DockPanel();
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 2),
            Background = good ? ThemeManager.GoodSoft : (Brush)Application.Current.FindResource("AccentSoftBrush"),
            Child = new TextBlock { Text = pill, FontSize = 12, Foreground = good ? ThemeManager.Good : (Brush)Application.Current.FindResource("AccentTextBrush") },
        };
        DockPanel.SetDock(chip, Dock.Right);
        head.Children.Add(chip);
        if (toggle is not null) { DockPanel.SetDock(toggle, Dock.Right); head.Children.Add(toggle); }
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(Text(title, weight: FontWeights.SemiBold, size: 15));
        if (BetaIntegrations.Contains(title)) titleRow.Children.Add(BetaTag());
        head.Children.Add(titleRow);
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(WithMargin(Text(blurb, muted: true, size: 13), 0, 8, 0, 0));
        body.Children.Add(WithMargin(Text(detail, size: 13), 0, 6, 0, 0));
        if (actions is not null) body.Children.Add(WithMargin(actions, 0, 10, 0, 0)); // buttons go under the text: in the header they cover the title on a narrow card
        var box = Panel(title, body, new Thickness(6));
        box.Header = null; // the card shows its own title
        return box;
    }

    static FrameworkElement Row(string a, string b, string c, string d, bool header = false, bool warn = false, FrameworkElement? use = null)
    {
        var g = new Grid { Margin = new Thickness(0, header ? 0 : 6, 0, 0) };
        foreach (var w in new[] { 2.0, 1.2, 1.0, 1.4 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        if (use is not null) { Grid.SetColumn(use, 4); use.HorizontalAlignment = HorizontalAlignment.Right; g.Children.Add(use); }
        var cells = new[] { a, b, c, d };
        for (var i = 0; i < 4; i++)
        {
            var t = Text(cells[i], muted: header || i is 1 or 2, size: header ? 12 : 14);
            if (i == 3 && !header)
                t.Foreground = warn ? (Brush)Application.Current.FindResource("AccentTextBrush") : ThemeManager.Good;
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }
}
