using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ember.Core;
using Ember.Lighting;
using LColor = Ember.Lighting.Color;
using WColor = System.Windows.Media.Color;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// The redesigned Lights page: a heading with the brightness and the master switch, scene chips and the schedule, the live preview with
/// the effect gallery on the left, and the layers with the settings of the chosen layer on the right.
/// </summary>
public sealed partial class LightsPage
{
    int _galleryFilter;   // 0 all, 1 colour, 2 reactive, 3 sync
    WrapPanel? _saveRow;
    readonly StackPanel _gallery = new();
    readonly Border _scheduleChip = new() { CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(1), Padding = new Thickness(11, 4, 12, 5), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 12) };
    readonly TextBlock _scheduleText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    static T WithMargin<T>(T e, double l, double t, double r, double b) where T : FrameworkElement { e.Margin = new Thickness(l, t, r, b); return e; }

    /// <summary>Shown under the scenes when a schedule is on and has lights rules: "Schedule: Aurora from 18:00, off at 01:00".</summary>
    public void SetSchedule(ScheduleSet schedule)
    {
        var parts = schedule.Enabled
            ? schedule.Rules.Where(r => r.Enabled && (r.Scene is not null || r.LightsOff)).Select(r => r.LightsOff ? $"off at {r.Start:HH:mm}" : $"{r.Scene} from {r.Start:HH:mm}").ToList()
            : [];
        _scheduleText.Text = "Schedule: " + string.Join(", ", parts);
        _scheduleChip.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- the page ----

    void BuildNewSkeleton()
    {
        var m = _m!;
        var s = m.State;
        _root.Margin = new Thickness(32, 30, 32, 28);

        _onSwitch = Switch("", s.On, on => { m.SetOn(on); _rendered = m.State; });
        _onSwitch.Margin = new Thickness(0);
        AutomationProperties.SetName(_onSwitch, "Lights on");
        _brightness = new Slider { Style = Sty("PillSlider"), Minimum = 0, Maximum = 100, Value = s.Current.Brightness * 100, Width = 170, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_brightness, "Brightness");
        var readout = Ui.Text($"{s.Current.Brightness * 100:0}%", 13, mono: true);
        readout.Width = 42;
        readout.TextAlignment = TextAlignment.Right;
        readout.Margin = new Thickness(8, 0, 14, 0);
        _brightness.ValueChanged += (_, e) =>
        {
            readout.Text = $"{e.NewValue:0}%";
            if (!_updating) { m.SetBrightness(e.NewValue / 100); _rendered = m.State; }
        };

        // Heading: the title on the left; brightness, the identify button and the master switch on the right.
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(WithMargin(Ui.Icon("M16 12a4 4 0 1 0-8 0 4 4 0 0 0 8 0z M12 2v3 M12 19v3 M2 12h3 M19 12h3", 16, "MutedBrush"), 0, 0, 12, 0));
        controls.Children.Add(_brightness);
        controls.Children.Add(readout);
        var identify = Pill("Identify devices", () => { foreach (var d in m.Devices.Where(d => !d.Disabled)) _ = m.IdentifyAsync(d.Id); }, "Flash every device white a few times so you can see which is which");
        identify.Margin = new Thickness(0, 0, 14, 0);
        controls.Children.Add(identify);
        controls.Children.Add(_onSwitch);
        var head = Ui.Heading("Lights", null, controls);
        ((DockPanel)head).Margin = new Thickness(0, 0, 0, 14);
        _root.Children.Add(head);

        // Scenes: chips, the schedule line, then a small row to save or delete.
        _root.Children.Add(_scenes);
        _scheduleChip.Child = _scheduleText;
        _scheduleChip.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        _scheduleChip.SetResourceReference(Border.BorderBrushProperty, "SoftCardBorderBrush");
        _scheduleText.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        _root.Children.Add(_scheduleChip);

        _sceneName = new TextBox { Style = Sty("TextField"), Tag = "Name for a new scene", Width = 200, MaxLength = 60, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(_sceneName, "Name for a new scene");
        var save = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
        _saveRow = save;
        save.Children.Add(_sceneName);
        save.Children.Add(Pill("Save as new scene", SaveScene));
        save.Children.Add(Pill("Delete this scene", () => { m.DeleteScene(m.State.CurrentScene); }));
        _root.Children.Add(save);

        // Left: the live preview (where devices are placed) and the effect gallery.
        var frame = new Border { CornerRadius = new CornerRadius(12), Child = _canvas, Padding = new Thickness(0), BorderThickness = new Thickness(1), Margin = new Thickness(0, 12, 0, 0) };
        frame.SetResourceReference(Border.BackgroundProperty, "WindowBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        _canvas.Height = 250;
        _canvas.SizeChanged += (_, _) => LayoutVisuals();
        var arrange = Pill("Arrange devices", () => m.Arrange(), "Arrange devices on the canvas");
        arrange.Margin = new Thickness(0);
        var preview = new StackPanel();
        preview.Children.Add(CardHeader("Live preview", arrange));
        preview.Children.Add(frame);
        preview.Children.Add(WithMargin(Ui.Muted("Drag a device to where it sits in your room, or select it and use the arrow keys. Effects are painted across this canvas.", 12), 0, 8, 0, 0));

        var filter = Segments(["All", "Colour", "Reactive", "Sync"], _galleryFilter, i => { _galleryFilter = i; BuildGallery(); }, "EffectKind");
        filter.Margin = new Thickness(0);
        var galleryBody = new StackPanel();
        galleryBody.Children.Add(CardHeader("Add an effect", filter));
        galleryBody.Children.Add(_gallery);
        BuildGallery();

        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        left.Children.Add(WithMargin(Ui.Card(preview, name: "Live preview"), 0, 0, 0, 16));
        left.Children.Add(WithMargin(Ui.Card(galleryBody, name: "Add an effect"), 0, 0, 0, 16));

        // Right: the layers, then the settings of the chosen layer.
        var layersBody = new StackPanel();
        layersBody.Children.Add(CardHeader("Layers", Ui.Muted("Top layer wins", 12)));
        layersBody.Children.Add(_layers);
        var settings = new StackPanel();
        settings.Children.Add(_editor);
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(WithMargin(Ui.Card(layersBody, name: "Layers"), 0, 0, 0, 16));
        right.Children.Add(WithMargin(Ui.Card(settings, name: "Layer settings"), 0, 0, 0, 16));

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star), MinWidth = 340 });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        Grid.SetColumn(right, 1);
        columns.Children.Add(left);
        columns.Children.Add(right);
        _root.Children.Add(columns);

        // Below: the notes and status of each integration, painting single LEDs and the Logitech option, then the device list.
        var notes = new StackPanel();
        notes.Children.Add(Ui.Muted("Lights start switched off, so Ember never takes over a device until you ask. Turning them off sends one dark frame; closing Ember leaves them as they are.", 12.5));
        notes.Children.Add(_status);
        _root.Children.Add(Panel("Status", notes));

        var tools = new StackPanel();
        var logi = Switch("Logitech: one colour for every Logitech device", s.LogitechOneColour, on => { m.SetLogitechOneColour(on); _rendered = m.State; });
        logi.Margin = new Thickness(0, 0, 0, 8);
        logi.ToolTip = "Off (recommended): the keyboard is lit key by key and the mouse is left to G HUB. On: the mouse and keyboard all take one colour, because G HUB cannot do both at once.";
        tools.Children.Add(logi);
        tools.Children.Add(BuildPaintTools());
        _root.Children.Add(Panel("Paint and Logitech", tools));
        _root.Children.Add(Panel("Devices", BuildDevicesBox()));
    }

    /// <summary>The top line of a card: the small capital heading on the left and a control on the right.</summary>
    static FrameworkElement CardHeader(string title, FrameworkElement? right)
    {
        var head = new DockPanel();
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        head.Children.Add(Ui.Label(title));
        return head;
    }

    // ---- scenes ----

    void RebuildSceneChips(LightingState state)
    {
        var wrap = new WrapPanel();
        foreach (var name in state.Scenes.Select(s => s.Name))
        {
            var n = name;
            var chip = new RadioButton { Content = n, GroupName = "Scene", IsChecked = n == state.CurrentScene, Style = Sty("SceneChip") };
            AutomationProperties.SetName(chip, "Scene " + n);
            chip.Click += (_, _) => _m!.SelectScene(n);
            wrap.Children.Add(chip);
        }
        var add = new Button { Content = "+ New", Style = Sty("SceneChipPlain"), ToolTip = "Name a new scene, then press Save as new scene" };
        AutomationProperties.SetName(add, "New scene");
        add.Click += (_, _) =>
        {
            // "+ New" opens the row where a new scene is named and saved (or the current one deleted); pressing it again closes the row.
            if (_saveRow is null) return;
            _saveRow.Visibility = _saveRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (_saveRow.Visibility == Visibility.Visible) _sceneName?.Focus();
        };
        wrap.Children.Add(add);
        _scenes.Children.Add(wrap);
    }

    // ---- layers ----

    /// <summary>One layer in the list: a handle, its name and what it is, and its switch. The chosen layer has an accent edge.</summary>
    FrameworkElement NewLayerRow(Layer l, bool selected)
    {
        var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var pick = new Button { Style = Sty("LayerRow"), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        if (selected) pick.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");
        var texts = new StackPanel { Margin = new Thickness(18, 0, 56, 0) };
        texts.Children.Add(Ui.Text(l.Name, 14));
        texts.Children.Add(Ui.Muted($"{l.Effect.Label} · {l.Blend}", 11.5));
        var handle = Ui.Text("⋮⋮", 14, "MutedBrush");
        handle.Opacity = 0.5;
        handle.HorizontalAlignment = HorizontalAlignment.Left;
        var content = new Grid();
        content.Children.Add(handle);
        content.Children.Add(texts);
        pick.Content = content;
        AutomationProperties.SetName(pick, "Edit layer " + l.Name);
        pick.Click += (_, _) => { _selectedLayer = l.Id; RebuildLayers(_m!.State); };
        row.Children.Add(pick);

        var on = Switch("", l.Enabled, v => EditLayer(l.Id, x => x with { Enabled = v }));
        AutomationProperties.SetName(on, "Layer " + l.Name + " on");
        on.Margin = new Thickness(0, 0, 14, 0);
        on.HorizontalAlignment = HorizontalAlignment.Right;
        on.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(on);
        return row;
    }

    /// <summary>The first line of the settings card: the layer's name in the accent colour and the buttons that move or remove it.</summary>
    FrameworkElement NewEditorHeader(Layer layer)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, run, name) in new (string, Action, string)[]
        {
            ("↑", () => MoveLayer(layer.Id, +1), "Move " + layer.Name + " up"),
            ("↓", () => MoveLayer(layer.Id, -1), "Move " + layer.Name + " down"),
            ("Remove", () => RemoveLayer(layer.Id), "Remove layer " + layer.Name),
        })
        {
            var p = Pill(label, run, name);
            p.Margin = new Thickness(6, 0, 0, 0);
            p.Padding = new Thickness(label.Length == 1 ? 12 : 14, 5, label.Length == 1 ? 12 : 14, 5);
            actions.Children.Add(p);
        }
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(actions, Dock.Right);
        head.Children.Add(actions);
        head.Children.Add(Ui.Label(layer.Name, "AccentTextBrush"));
        return head;
    }

    // ---- the effect gallery ----

    sealed record Tile(string Label, int Kind, Brush Thumb, Func<Effect> Make);

    static LinearGradientBrush Gradient(params string[] colours)
    {
        var stops = colours.Select((c, i) => new GradientStop((WColor)ColorConverter.ConvertFromString(c), colours.Length == 1 ? 0 : (double)i / (colours.Length - 1))).ToArray();
        var b = new LinearGradientBrush(new GradientStopCollection(stops), new Point(0, 0.4), new Point(1, 0.6));
        b.Freeze();
        return b;
    }

    static Brush Solid(string colour)
    {
        var b = new SolidColorBrush((WColor)ColorConverter.ConvertFromString(colour));
        b.Freeze();
        return b;
    }

    static Brush Stripes(string on, string off)
    {
        var b = new LinearGradientBrush(new GradientStopCollection
        {
            new((WColor)ColorConverter.ConvertFromString(on), 0), new((WColor)ColorConverter.ConvertFromString(on), 0.5),
            new((WColor)ColorConverter.ConvertFromString(off), 0.5), new((WColor)ColorConverter.ConvertFromString(off), 1),
        }, new Point(0, 0), new Point(0.25, 0)) { SpreadMethod = GradientSpreadMethod.Repeat };
        b.Freeze();
        return b;
    }

    IReadOnlyList<Tile> Tiles() =>
    [
        new("Static", 1, Solid("#ffb04d"), () => new StaticEffect(new LColor(1.0, 0.45, 0.1))),
        new("Breathing", 1, new RadialGradientBrush((WColor)ColorConverter.ConvertFromString("#ff9a5c"), (WColor)ColorConverter.ConvertFromString("#2b1208")), () => new BreathingEffect(new LColor(1.0, 0.45, 0.1))),
        new("Wave", 1, Gradient("#ff7a2f", "#b14dff", "#4fd1c5"), () => new WaveEffect(new LColor(1.0, 0.45, 0.1), new LColor(0.2, 0.3, 1.0))),
        new("Fire", 1, Gradient("#7a1200", "#ff4a00", "#ffd24d"), () => new FireEffect(new LColor(1.0, 0.2, 0.0), new LColor(1.0, 0.9, 0.3))),
        new("Rain", 1, Gradient("#10243a", "#3b82f6", "#10243a"), () => new RainEffect(new LColor(0.2, 0.6, 1.0))),
        new("Temperature", 2, Gradient("#4fd1c5", "#ffb04d", "#ff3b30"), () => new TemperatureGlowEffect(TempSensor.Cpu, new LColor(0.1, 0.4, 1.0), new LColor(1.0, 0.1, 0.05))),
        new("Ripples", 2, Gradient("#0e0c0b", "#3b82f6", "#0e0c0b"), () => new RippleEffect(new LColor(0.2, 0.6, 1.0))),
        new("Reactive keys", 2, Stripes("#ff7a2f", "#2b2420"), () => new ReactiveKeysEffect(new LColor(1.0, 1.0, 1.0), new LColor(1.0, 0.3, 0.05))),
        new("Key group", 2, Stripes("#ffb04d", "#3a322d"), () => new KeyGroupEffect(KeyGroup.FunctionRow, new StaticEffect(new LColor(1.0, 0.45, 0.1)))),
        new("Key highlight", 2, Gradient("#2b2420", "#ff3b30", "#2b2420"), () => new KeyHighlightEffect(new LColor(1.0, 0.15, 0.1))),
        new("Music pulse", 3, Gradient("#1a0a2e", "#ff3388"), () => new AudioPulseEffect(new LColor(0.05, 0.02, 0.1), new LColor(1.0, 0.2, 0.5), AudioBand.Bass)),
        new("Spectrum", 3, Gradient("#ff1a33", "#3366ff"), () => new AudioSpectrumEffect(new LColor(1.0, 0.1, 0.2), new LColor(0.2, 0.4, 1.0))),
        new("Bars", 3, Gradient("#1aff4d", "#ff1a1a"), () => new AudioBarsEffect(new LColor(0.1, 1.0, 0.3), new LColor(1.0, 0.1, 0.1))),
        new("Screen sync", 3, Gradient("#3b82f6", "#e0446a", "#ffb04d"), () => new ScreenSyncEffect()),
    ];

    void BuildGallery()
    {
        _gallery.Children.Clear();
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(-4, 8, -4, 0) };
        foreach (var tile in Tiles().Where(t => _galleryFilter == 0 || t.Kind == _galleryFilter))
        {
            var thumb = new Border { Height = 44, CornerRadius = new CornerRadius(8), Background = tile.Thumb, Margin = new Thickness(0, 0, 0, 7) };
            var label = new TextBlock { Text = tile.Label, FontSize = 12.5, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var content = new StackPanel();
            content.Children.Add(thumb);
            content.Children.Add(label);
            var button = new Button { Content = content, Style = Sty("FxTile"), Margin = new Thickness(4) };
            AutomationProperties.SetName(button, "Add a " + tile.Label + " layer");
            var make = tile.Make;
            button.Click += (_, _) => AddLayer(make());
            grid.Children.Add(button);
        }
        _gallery.Children.Add(grid);
    }
}
