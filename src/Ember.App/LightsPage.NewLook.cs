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
        _solo = Pill("Solo layer", ToggleSolo, "Play only the chosen layer on the lights, to see what it does on its own");
        _solo.Margin = new Thickness(0, 0, 8, 0);
        var previewTools = new StackPanel { Orientation = Orientation.Horizontal };
        previewTools.Children.Add(_solo);
        previewTools.Children.Add(arrange);
        var preview = new StackPanel();
        preview.Children.Add(CardHeader("Live preview", previewTools));
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
        tools.MaxWidth = 760;   // the sliders of the paint tools are not meant to stretch across the whole window
        tools.HorizontalAlignment = HorizontalAlignment.Left;
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
        texts.Children.Add(Ui.Muted($"{l.Effect.Label} · {(l.Blend == BlendMode.Screen ? "Tint" : l.Blend.ToString())}", 11.5));
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

    /// <summary>Puts the layer's effect back to the starting look of its kind from the gallery (its name, blend, opacity and devices stay).</summary>
    void ResetLayer(Layer layer)
    {
        var fresh = Tiles().Select(t => t.Make()).FirstOrDefault(e => e.GetType() == layer.Effect.GetType());
        if (fresh is null) { SetNote("There is no starting look for this effect."); return; }
        EditLayer(layer.Id, x => x with { Effect = fresh }, rebuild: true);
    }

    /// <summary>The first line of the settings card: the layer's name in the accent colour and the buttons that move or remove it.</summary>
    FrameworkElement NewEditorHeader(Layer layer)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, run, name) in new (string, Action, string)[]
        {
            ("Reset", () => ResetLayer(layer), "Reset " + layer.Name + " to its starting look"),
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


    // ---- the layer editor of the redesign ----

    Button? _solo;

    /// <summary>The Solo button shows its state: lit while one layer is played alone on the lights and the preview.</summary>
    void UpdateSolo()
    {
        if (_solo is null || _m is null) return;
        var on = _m.SoloLayer is not null;
        _solo.SetResourceReference(Control.BackgroundProperty, on ? "AccentSoftBrush" : "PanelBrush");
        _solo.SetResourceReference(Control.ForegroundProperty, on ? "AccentTextBrush" : "TextBrush");
    }

    void ToggleSolo()
    {
        if (_m is null) return;
        _m.SoloLayer = _m.SoloLayer is null ? _selectedLayer : null;
        UpdateSolo();
    }

    void BuildNewEditor(Layer layer, LightingState state)
    {
        var id = layer.Id;
        if (_m!.SoloLayer is not null) _m.SoloLayer = id;   // the solo follows the chosen layer
        UpdateSolo();
        _editor.Children.Add(NewEditorHeader(layer));
        Action<Func<Effect, Effect>> apply = change => EditLayer(id, x => x with { Effect = change(x.Effect) });
        EffectFields(layer.Effect, id, apply);

        _editor.Children.Add(Labelled("Opacity", 0, 100, layer.Opacity * 100, v => $"{v:0}%", v => EditLayer(id, x => x with { Opacity = v / 100 }), 1));
        if (layer.Effect is GradientEffect g)
        {
            _editor.Children.Add(Text("Direction", muted: true));
            var arrows = new[] { GradientDirection.Right, GradientDirection.Left, GradientDirection.Up, GradientDirection.Down, GradientDirection.Out };
            _editor.Children.Add(Segments(["→", "←", "↑", "↓", "Out"], Array.IndexOf(arrows, g.Direction), i => apply(x => ((GradientEffect)x) with { Direction = arrows[i] }), "Direction" + id));
        }
        _editor.Children.Add(Text("Blend", muted: true));
        _editor.Children.Add(Segments(["Normal", "Add", "Multiply", "Tint"], BlendIndex(layer.Blend), i => EditLayer(id, x => x with { Blend = BlendFrom(i) }), "Blend" + id));
        _editor.Children.Add(AppliesTo(layer, state));

        _editor.Children.Add(WithMargin(Text("Layer name", muted: true), 0, 10, 0, 0));
        var name = new TextBox { Text = layer.Name, Style = Sty("TextField"), MaxLength = 40, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetName(name, "Layer name");
        name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0 && name.Text.Trim() != layer.Name) EditLayer(id, x => x with { Name = name.Text.Trim() }, rebuild: true); };
        _editor.Children.Add(name);
    }

    // The mockup's blend row reads Normal, Add, Multiply, Tint; Tint is the engine's Screen blend, which lightens what is below with the layer's colour.
    static int BlendIndex(BlendMode m) => m switch { BlendMode.Add => 1, BlendMode.Multiply => 2, BlendMode.Screen => 3, _ => 0 };
    static BlendMode BlendFrom(int i) => i switch { 1 => BlendMode.Add, 2 => BlendMode.Multiply, 3 => BlendMode.Screen, _ => BlendMode.Normal };

    /// <summary>One chip per device on the canvas: the layer is drawn on the lit ones. All lit means every device, now and later.</summary>
    FrameworkElement AppliesTo(Layer layer, LightingState state)
    {
        var devices = state.Current.Placements.Where(p => p.Enabled).Select(p => (p.DeviceId, Name: _m!.Devices.FirstOrDefault(d => d.Id == p.DeviceId)?.Name ?? p.DeviceId)).ToList();
        var box = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        box.Children.Add(Text("Applies to", muted: true));
        if (devices.Count == 0) { box.Children.Add(Text("No devices are on the canvas yet.", muted: true, size: 12)); return box; }
        var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var (deviceId, deviceName) in devices)
        {
            var applied = layer.Covers(deviceId);
            var chip = new Button { Content = deviceName, Style = Sty("ActionButton"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 4, 12, 4), FontSize = 12.5 };
            chip.SetResourceReference(Control.BackgroundProperty, applied ? "AccentSoftBrush" : "PanelBrush");
            chip.SetResourceReference(Control.ForegroundProperty, applied ? "AccentTextBrush" : "MutedBrush");
            AutomationProperties.SetName(chip, (applied ? "Stop drawing " : "Draw ") + layer.Name + (applied ? " on " : " on ") + deviceName);
            var captured = deviceId;
            chip.Click += (_, _) =>
            {
                var now = LayerScene(_m!.State).Layers.First(l => l.Id == layer.Id);
                var set = (now.Targets ?? devices.Select(d => d.DeviceId).ToList()).ToList();
                if (!set.Remove(captured)) set.Add(captured);
                if (set.Count == 0) { SetNote("A layer needs at least one device to draw on."); return; }
                var everyone = devices.All(d => set.Contains(d.DeviceId));
                EditLayer(layer.Id, x => x with { Targets = everyone ? null : set }, rebuild: true);
            };
            wrap.Children.Add(chip);
        }
        box.Children.Add(wrap);
        return box;
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
        new("Aurora wave", 1, Gradient("#ff7a2f", "#b14dff", "#4fd1c5"), () => GradientEffect.Default),
        new("Wave", 1, Gradient("#ff7a2f", "#3b82f6"), () => new WaveEffect(new LColor(1.0, 0.45, 0.1), new LColor(0.2, 0.3, 1.0))),
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
