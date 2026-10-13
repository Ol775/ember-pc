using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ember.Lighting;
using LColor = Ember.Lighting.Color;
using WColor = System.Windows.Media.Color;
using static Ember.App.LightsUi;

namespace Ember.App;

/// <summary>
/// The Lights page: master switch, scenes, the canvas where devices are placed, the effect layers and the device list.
/// The parts that follow the scene are rebuilt in code whenever the scene changes from somewhere else; edits made here
/// update only what they touch, so a slider is never replaced while it is being dragged.
/// </summary>
public sealed class LightsPage : UserControl
{
    sealed class DeviceVisual
    {
        public required Border Root;
        public required List<SolidColorBrush> Leds;
        public required TextBlock Label;
        public required Thumb Handle;
        public double X, Y, W, H;
        public UniformGrid? Grid;
        public int Columns, RealCount;
        public IReadOnlyList<string?>? Paint;
    }

    readonly StackPanel _root = new() { Margin = new Thickness(24) };
    readonly StackPanel _scenes = new(), _layers = new(), _editor = new(), _devices = new(), _status = new();
    readonly Canvas _canvas = new() { Height = 340, ClipToBounds = false, Background = Brushes.Transparent };
    readonly Dictionary<string, DeviceVisual> _visuals = [];
    CheckBox? _onSwitch;
    Slider? _brightness;
    TextBox? _sceneName;
    LightingManager? _m;
    LightingState? _rendered;
    string? _selectedLayer, _selectedDevice;
    bool _paintMode, _fillAll;
    readonly PaintLibrary _library = new(PaintLibrary.DefaultFolder);
    readonly StackPanel _libraryList = new();
    string _libraryName = "";
    LColor _paintColour = new(1.0, 0.45, 0.1), _paintEnd = new(0.2, 0.3, 1.0);
    double _paintAngle;
    bool _updating;

    public LightsPage()
    {
        Content = _root;
        Focusable = false;
        _root.Children.Add(new TextBlock { Text = "Lights", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        AutomationProperties.SetHeadingLevel(_root.Children[0], AutomationHeadingLevel.Level2);
    }

    public void Attach(LightingManager manager)
    {
        _m = manager;
        BuildSkeleton();
        manager.Changed += () => Dispatcher.InvokeAsync(OnChanged);
        OnChanged();
    }

    // ---- skeleton ----

    void BuildSkeleton()
    {
        var m = _m!;
        var s = m.State;

        _onSwitch = Switch("Lights on", s.On, on => { m.SetOn(on); _rendered = m.State; });
        _brightness = new Slider { Style = Sty("PillSlider"), Minimum = 0, Maximum = 100, Value = s.Current.Brightness * 100, Width = 240, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(_brightness, "Brightness");
        _brightness.ValueChanged += (_, e) => { if (!_updating) { m.SetBrightness(e.NewValue / 100); _rendered = m.State; } };
        var top = new StackPanel();
        var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_onSwitch);
        row.Children.Add(Text("Brightness", muted: true));
        row.Children.Add(_brightness);
        top.Children.Add(row);
        top.Children.Add(Text("Lights start switched off, so Ember never takes over a device until you ask. Turning them off sends one dark frame; closing Ember leaves them as they are.", muted: true));
        top.Children.Add(_status);
        var logi = Switch("Logitech: one colour for every Logitech device", s.LogitechOneColour, on => { m.SetLogitechOneColour(on); _rendered = m.State; });
        logi.Margin = new Thickness(0, 8, 0, 0);
        logi.ToolTip = "Off (recommended): the keyboard is lit key by key and the mouse is left to G HUB. On: the mouse and keyboard all take one colour, because G HUB cannot do both at once.";
        top.Children.Add(logi);
        _root.Children.Add(Panel("Lights", top));

        var sceneBox = new StackPanel();
        sceneBox.Children.Add(_scenes);
        _sceneName = new TextBox { Style = Sty("TextField"), Width = 200, MaxLength = 60, Margin = new Thickness(0, 0, 8, 6) };
        AutomationProperties.SetName(_sceneName, "Name for a new scene");
        var save = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        save.Children.Add(_sceneName);
        save.Children.Add(Pill("Save as new scene", SaveScene));
        save.Children.Add(Pill("Delete this scene", () => { m.DeleteScene(m.State.CurrentScene); }));
        sceneBox.Children.Add(save);
        _root.Children.Add(Panel("Scenes", sceneBox));

        var canvasBox = new StackPanel();
        canvasBox.Children.Add(Text("Drag a device to where it sits in your room, or select it and use the arrow keys (Ctrl with the arrows changes its size). Effects are painted across this canvas, so devices side by side show one picture.", muted: true));
        var frame = new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 10, 0, 0), Child = _canvas, Padding = new Thickness(0) };
        frame.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        _canvas.SizeChanged += (_, _) => LayoutVisuals();
        canvasBox.Children.Add(frame);
        var arrange = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        arrange.Children.Add(Pill("Arrange devices", () => m.Arrange(), "Arrange devices on the canvas"));
        arrange.Children.Add(Text("Gives each device its own row. New lamps found later are placed in free space; use this to tidy up.", muted: true, size: 12));
        canvasBox.Children.Add(arrange);
        var paintRow = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        paintRow.Children.Add(Switch("Paint single LEDs", _paintMode, on => { _paintMode = on; if (on && _selectedDevice is null) SetNote("Click a device on the canvas first, then click its LEDs."); }));
        paintRow.Children.Add(new Border { Width = 12 });
        paintRow.Children.Add(ColorField("Paint colour", _paintColour, c => _paintColour = c));
        paintRow.Children.Add(Pill("Clear paint on this device", ClearPaint));
        canvasBox.Children.Add(paintRow);
        canvasBox.Children.Add(Text("With painting on, click or drag over LEDs to give them a fixed colour and right-click to let the layers colour them again. Painted LEDs ignore the effects.", muted: true, size: 12));
        var fills = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        fills.Children.Add(ColorField("Gradient end", _paintEnd, c => _paintEnd = c));
        fills.Children.Add(new Border { Width = 12 });
        canvasBox.Children.Add(fills);
        canvasBox.Children.Add(Labelled("Gradient direction", 0, 360, _paintAngle, v => $"{v:0}°", v => _paintAngle = v, 5));
        canvasBox.Children.Add(Text("Fills and the library apply to", muted: true));
        canvasBox.Children.Add(Segments(["The selected device", "Every device on the canvas"], _fillAll ? 1 : 0, i => _fillAll = i == 1, "FillTarget"));
        var fillRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        fillRow.Children.Add(Pill("Fill with colour", () => Fill((n, cols) => PaintTools.Fill(n, _paintColour), null)));
        fillRow.Children.Add(Pill("Fill with gradient", () => Fill((n, cols) => PaintTools.Gradient(n, cols, _paintColour, _paintEnd, _paintAngle), (p, c) => CanvasPaint.Gradient(p, c.Counts, c.Columns, _paintColour, _paintEnd, _paintAngle))));
        fillRow.Children.Add(Pill("Fill with rainbow", () => Fill((n, cols) => PaintTools.Rainbow(n, cols, _paintAngle), (p, c) => CanvasPaint.Rainbow(p, c.Counts, c.Columns, _paintAngle))));
        fillRow.Children.Add(Pill("Flip left to right", MirrorSelected));
        fillRow.Children.Add(Pill("Export paint…", ExportPaint));
        fillRow.Children.Add(Pill("Import paint…", ImportPaint));
        canvasBox.Children.Add(fillRow);
        canvasBox.Children.Add(Text("A gradient runs from the paint colour to the gradient end colour in the direction above (0° is left to right, 90° is top to bottom). Exported paint is a small JSON file you can share or keep; importing fits it to the LEDs of the selected device.", muted: true, size: 12));
        canvasBox.Children.Add(Text("Paint library", weight: FontWeights.SemiBold));
        var libRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var libName = new TextBox { Text = _libraryName, Style = Sty("TextField"), Width = 200, MaxLength = PaintLibrary.MaxNameLength, Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(libName, "Paint library name");
        libName.TextChanged += (_, _) => _libraryName = libName.Text;
        libRow.Children.Add(libName);
        libRow.Children.Add(Pill("Save the selected device paint", () => LibrarySave()));
        canvasBox.Children.Add(libRow);
        canvasBox.Children.Add(_libraryList);
        canvasBox.Children.Add(Text("Saved pictures stay on this PC in your Ember folder, and can be used on any device: they are fitted to its number of LEDs. Copy the files in that folder to share them.", muted: true, size: 12));
        RebuildLibrary();
        _root.Children.Add(Panel("Canvas", canvasBox));

        var layerBox = new StackPanel();
        var add = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        add.Children.Add(Text("Add a layer:", muted: true));
        add.Children.Add(new Border { Width = 8 });
        add.Children.Add(Pill("Static colour", () => AddLayer(new StaticEffect(new LColor(1.0, 0.45, 0.1)))));
        add.Children.Add(Pill("Wave", () => AddLayer(new WaveEffect(new LColor(1.0, 0.45, 0.1), new LColor(0.2, 0.3, 1.0)))));
        add.Children.Add(Pill("Breathing", () => AddLayer(new BreathingEffect(new LColor(1.0, 0.45, 0.1)))));
        add.Children.Add(Pill("Temperature glow", () => AddLayer(new TemperatureGlowEffect(TempSensor.Cpu, new LColor(0.1, 0.4, 1.0), new LColor(1.0, 0.1, 0.05)))));
        add.Children.Add(Pill("Screen sync", () => AddLayer(new ScreenSyncEffect())));
        add.Children.Add(Pill("Music pulse", () => AddLayer(new AudioPulseEffect(new LColor(0.05, 0.02, 0.1), new LColor(1.0, 0.2, 0.5), AudioBand.Bass))));
        add.Children.Add(Pill("Music spectrum", () => AddLayer(new AudioSpectrumEffect(new LColor(1.0, 0.1, 0.2), new LColor(0.2, 0.4, 1.0)))));
        add.Children.Add(Pill("Spectrum bars", () => AddLayer(new AudioBarsEffect(new LColor(0.1, 1.0, 0.3), new LColor(1.0, 0.1, 0.1)))));
        add.Children.Add(Pill("Ripples", () => AddLayer(new RippleEffect(new LColor(0.2, 0.6, 1.0)))));
        add.Children.Add(Pill("Reactive keys", () => AddLayer(new ReactiveKeysEffect(new LColor(1.0, 1.0, 1.0), new LColor(1.0, 0.3, 0.05)))));
        add.Children.Add(Pill("Fire", () => AddLayer(new FireEffect(new LColor(1.0, 0.2, 0.0), new LColor(1.0, 0.9, 0.3)))));
        add.Children.Add(Pill("Rain", () => AddLayer(new RainEffect(new LColor(0.2, 0.6, 1.0)))));
        add.Children.Add(Pill("Key group", () => AddLayer(new KeyGroupEffect(KeyGroup.FunctionRow, new StaticEffect(new LColor(1.0, 0.45, 0.1))))));
        layerBox.Children.Add(add);
        layerBox.Children.Add(Text("Layers are stacked like in a photo editor: the top one is painted last.", muted: true));
        layerBox.Children.Add(_layers);
        layerBox.Children.Add(_editor);
        _root.Children.Add(Panel("Layers", layerBox));

        _root.Children.Add(Panel("Devices", BuildDevicesBox()));
    }

    FrameworkElement BuildDevicesBox()
    {
        var m = _m!;
        var box = new StackPanel();
        box.Children.Add(_devices);

        var s = m.State;
        box.Children.Add(Text("OpenRGB", weight: FontWeights.SemiBold));
        var bundled = MainWindow.BundledOpenRgb is not null;
        box.Children.Add(Text(bundled
            ? "OpenRGB comes with Ember and starts by itself when it is not already running. Leave the box below empty to use it, or choose your own OpenRGB.exe. Close other lighting apps that hold your devices (such as SignalRGB or vendor suites) if a device does not show up."
            : "Ember talks to PC lighting through OpenRGB, a separate program that must be running with its SDK server on. Choose its OpenRGB.exe below to let Ember start it. Close other lighting apps that hold your devices (such as SignalRGB or vendor suites) if a device does not show up.", muted: true));
        var path = new TextBox { Style = Sty("TextField"), Text = s.OpenRgbPath ?? "", Width = 380, Margin = new Thickness(0, 8, 8, 6) };
        AutomationProperties.SetName(path, "Path to OpenRGB.exe");
        path.LostFocus += (_, _) => { m.SetOpenRgb(path.Text, m.State.StartOpenRgb); _rendered = m.State; };
        var row = new WrapPanel();
        row.Children.Add(path);
        row.Children.Add(Pill("Browse…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "OpenRGB|OpenRGB.exe|Programs|*.exe", Title = "Choose OpenRGB.exe" };
            if (dlg.ShowDialog() == true) { path.Text = dlg.FileName; m.SetOpenRgb(dlg.FileName, m.State.StartOpenRgb); _rendered = m.State; }
        }));
        box.Children.Add(row);
        box.Children.Add(Switch("Start OpenRGB for me when it is not running", s.ShouldStartOpenRgb, on => { m.SetOpenRgb(m.State.OpenRgbPath, on); _rendered = m.State; }));
        box.Children.Add(Pill("Look for devices again", () => m.RefreshDevices()));
        return box;
    }

    // ---- changes ----

    void OnChanged()
    {
        var m = _m!;
        var state = m.State;
        if (!ReferenceEquals(state, _rendered))
        {
            _rendered = state;
            RebuildDynamic(state);
        }
        // Devices come and go without the scene changing (a source reconnects, a send fails).
        var key = string.Join("|", m.Devices.Select(d => d.Id + d.LedCount + d.Disabled)) + "#" + string.Join("|", m.Problems.Select(p => p.Key + p.Value));
        if (key != _deviceKey)
        {
            _deviceKey = key;
            RebuildDevices(state);
            RebuildVisuals(state);
        }
        UpdateStatus();
        UpdatePreview();
    }

    string _deviceKey = "";

    void RebuildDynamic(LightingState state)
    {
        _updating = true;
        try
        {
            if (_onSwitch is not null) _onSwitch.IsChecked = state.On;
            if (_brightness is not null) _brightness.Value = state.Current.Brightness * 100;
        }
        finally { _updating = false; }
        RebuildScenes(state);
        RebuildVisuals(state);
        RebuildLayers(state);
        RebuildDevices(state);
    }

    void RebuildScenes(LightingState state)
    {
        _scenes.Children.Clear();
        var names = state.Scenes.Select(s => s.Name).ToList();
        var wrap = new WrapPanel();
        var track = new Border { Style = Sty("SegmentTrack"), Margin = new Thickness(0, 0, 0, 0) };
        var inner = new WrapPanel();
        foreach (var name in names)
        {
            var n = name;
            var rb = new RadioButton { Content = n, GroupName = "Scene", IsChecked = n == state.CurrentScene, Style = Sty("Segment") };
            AutomationProperties.SetName(rb, "Scene " + n);
            rb.Click += (_, _) => _m!.SelectScene(n);
            inner.Children.Add(rb);
        }
        track.Child = inner;
        wrap.Children.Add(track);
        _scenes.Children.Add(wrap);
    }

    void SaveScene()
    {
        var name = _sceneName?.Text.Trim() ?? "";
        if (name.Length == 0) { SetNote("Type a name for the new scene first."); return; }
        if (!_m!.SaveSceneAs(name)) SetNote("That name is not allowed, or there are too many scenes.");
        else { SetNote(""); _sceneName!.Text = ""; }
    }

    string _note = "";
    void SetNote(string text) { _note = text; UpdateStatus(); }

    // ---- canvas ----

    void RebuildVisuals(LightingState state)
    {
        _canvas.Children.Clear();
        _visuals.Clear();
        var devices = _m!.Devices.ToDictionary(d => d.Id);
        foreach (var p in state.Current.Placements)
        {
            if (!devices.TryGetValue(p.DeviceId, out var info)) continue; // a device that is not here now is not drawn
            var columns = info?.Columns > 1 ? info.Columns : 0;
            var ledCount = columns > 0 ? Math.Clamp(info!.LedCount, 1, 400) : Math.Clamp(info?.LedCount ?? 8, 1, 60);
            var leds = new List<SolidColorBrush>();
            var grid = columns > 0
                ? new UniformGrid { Columns = columns, IsHitTestVisible = false }
                : new UniformGrid { Rows = p.Width >= p.Height ? 1 : 0, Columns = p.Width >= p.Height ? 0 : 1, IsHitTestVisible = false };
            for (var i = 0; i < ledCount; i++)
            {
                var brush = new SolidColorBrush(WColor.FromRgb(30, 26, 24));
                leds.Add(brush);
                grid.Children.Add(new Border { Background = brush, Margin = new Thickness(columns > 0 ? 0.5 : 1), CornerRadius = new CornerRadius(columns > 0 ? 1.5 : 3) });
            }
            var label = new TextBlock { Text = info?.Name ?? p.DeviceId, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false, Margin = new Thickness(6, 2, 6, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var handle = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Focusable = false };
            handle.Template = HandleTemplate();
            AutomationProperties.SetName(handle, "Resize " + label.Text);
            var dock = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(label, Dock.Top);
            dock.Children.Add(label);
            dock.Children.Add(grid);
            var host = new Grid();
            host.Children.Add(dock);
            host.Children.Add(handle);
            var root = new Border
            {
                Child = host, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Focusable = true, Cursor = Cursors.SizeAll,
                Background = new SolidColorBrush(WColor.FromArgb(200, 36, 31, 28)),
                Opacity = p.Enabled ? 1 : 0.4,
            };
            AutomationProperties.SetName(root, "Device on canvas: " + label.Text);
            var v = new DeviceVisual { Root = root, Leds = leds, Label = label, Handle = handle, X = p.X, Y = p.Y, W = p.Width, H = p.Height,
                Grid = grid, Columns = columns, RealCount = info?.LedCount ?? ledCount, Paint = p.Paint };
            _visuals[p.DeviceId] = v;
            Wire(p.DeviceId, v);
            _canvas.Children.Add(root);
        }
        LayoutVisuals();
        MarkSelection();
    }

    static ControlTemplate HandleTemplate()
    {
        var tri = new FrameworkElementFactory(typeof(System.Windows.Shapes.Polygon));
        tri.SetValue(System.Windows.Shapes.Polygon.PointsProperty, new PointCollection([new Point(14, 0), new Point(14, 14), new Point(0, 14)]));
        tri.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
        return new ControlTemplate(typeof(Thumb)) { VisualTree = tri };
    }

    void LayoutVisuals()
    {
        var w = _canvas.ActualWidth;
        var h = _canvas.ActualHeight;
        if (w <= 0 || h <= 0) return;
        foreach (var v in _visuals.Values)
        {
            Canvas.SetLeft(v.Root, v.X * w);
            Canvas.SetTop(v.Root, v.Y * h);
            v.Root.Width = Math.Max(24, v.W * w);
            v.Root.Height = Math.Max(24, v.H * h);
        }
    }

    void MarkSelection()
    {
        foreach (var (id, v) in _visuals)
            v.Root.BorderBrush = id == _selectedDevice ? Res("AccentBrush") : Res("PanelBorderBrush");
    }

    void Wire(string id, DeviceVisual v)
    {
        Point start = default;
        double sx = 0, sy = 0;
        var dragging = false;

        // Painting works like a brush: press and drag to paint every LED the pointer passes over.
        string? brushHex = null;
        var brushOn = false;

        v.Root.MouseRightButtonDown += (_, e) =>
        {
            if (!_paintMode || _selectedDevice != id || e.OriginalSource == v.Handle) return;
            brushOn = true; brushHex = null;
            v.Root.CaptureMouse();
            PaintAt(id, v, e.GetPosition(v.Grid), null);
            e.Handled = true;
        };
        v.Root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == v.Handle) return;
            _selectedDevice = id;
            MarkSelection();
            v.Root.Focus();
            if (_paintMode)
            {
                brushOn = true; brushHex = _paintColour.ToHex();
                v.Root.CaptureMouse();
                PaintAt(id, v, e.GetPosition(v.Grid), brushHex);
                e.Handled = true;
                return;
            }
            start = e.GetPosition(_canvas);
            sx = v.X; sy = v.Y;
            dragging = true;
            v.Root.CaptureMouse();
            e.Handled = true;
        };
        v.Root.MouseMove += (_, e) =>
        {
            if (brushOn) { PaintAt(id, v, e.GetPosition(v.Grid), brushHex); return; }
            if (!dragging) return;
            var p = e.GetPosition(_canvas);
            v.X = Math.Clamp(sx + (p.X - start.X) / _canvas.ActualWidth, 0, Math.Max(0, 1 - v.W));
            v.Y = Math.Clamp(sy + (p.Y - start.Y) / _canvas.ActualHeight, 0, Math.Max(0, 1 - v.H));
            LayoutVisuals();
        };
        void Release()
        {
            if (brushOn) { brushOn = false; v.Root.ReleaseMouseCapture(); return; }
            if (!dragging) return;
            dragging = false;
            v.Root.ReleaseMouseCapture();
            CommitPlacement(id, v);
        }
        v.Root.MouseLeftButtonUp += (_, _) => Release();
        v.Root.MouseRightButtonUp += (_, _) => { if (brushOn) Release(); };
        v.Handle.DragDelta += (_, e) =>
        {
            v.W = Math.Clamp(v.W + e.HorizontalChange / _canvas.ActualWidth, 0.04, 1 - v.X);
            v.H = Math.Clamp(v.H + e.VerticalChange / _canvas.ActualHeight, 0.04, 1 - v.Y);
            LayoutVisuals();
        };
        v.Handle.DragCompleted += (_, _) => CommitPlacement(id, v);
        v.Root.GotKeyboardFocus += (_, _) => { _selectedDevice = id; MarkSelection(); };
        v.Root.KeyDown += (_, e) =>
        {
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.05 : 0.01;
            var (dx, dy) = e.Key switch { Key.Left => (-step, 0.0), Key.Right => (step, 0.0), Key.Up => (0.0, -step), Key.Down => (0.0, step), _ => (0.0, 0.0) };
            if (dx == 0 && dy == 0) return;
            e.Handled = true;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                v.W = Math.Clamp(v.W + dx, 0.04, 1 - v.X);
                v.H = Math.Clamp(v.H + dy, 0.04, 1 - v.Y);
            }
            else
            {
                v.X = Math.Clamp(v.X + dx, 0, Math.Max(0, 1 - v.W));
                v.Y = Math.Clamp(v.Y + dy, 0, Math.Max(0, 1 - v.H));
            }
            LayoutVisuals();
            CommitPlacement(id, v);
        };
    }

    /// <summary>Paints (or, with null, clears) the LED under the pointer. The list is kept as long as the device has LEDs.</summary>
    void PaintAt(string id, DeviceVisual v, Point at, string? hex)
    {
        var w = v.Grid!.ActualWidth;
        var h = v.Grid.ActualHeight;
        if (w <= 0 || h <= 0 || v.RealCount <= 0) return;
        int index;
        if (v.Columns > 1 && v.RealCount % v.Columns == 0)
        {
            var rows = v.RealCount / v.Columns;
            index = Math.Clamp((int)(at.Y / h * rows), 0, rows - 1) * v.Columns + Math.Clamp((int)(at.X / w * v.Columns), 0, v.Columns - 1);
        }
        else
        {
            var f = v.W >= v.H ? at.X / w : at.Y / h;
            index = Math.Clamp((int)(f * v.RealCount), 0, v.RealCount - 1);
        }
        var count = v.RealCount;
        var current = v.Paint is { } pl && index < pl.Count ? pl[index] : null;
        if (current == hex) return;   // dragging over an LED that already has this colour changes nothing
        _m!.EditScene(sc => sc with
        {
            Placements = sc.Placements.Select(p =>
            {
                if (p.DeviceId != id) return p;
                var list = (p.Paint ?? []).ToList();
                while (list.Count < count) list.Add(null);
                list[index] = hex;
                v.Paint = list.All(x => x is null) ? null : list;
                return p with { Paint = v.Paint };
            }).ToList(),
        });
        _rendered = _m.State;
        UpdatePreview();
    }

    void ClearPaint()
    {
        if (_selectedDevice is not { } id) { SetNote("Select a device on the canvas first."); return; }
        _m!.EditScene(sc => sc with { Placements = sc.Placements.Select(p => p.DeviceId == id ? p with { Paint = null } : p).ToList() });
        if (_visuals.TryGetValue(id, out var v)) v.Paint = null;
        _rendered = _m.State;
        UpdatePreview();
    }

    // ---- several devices and the library ----

    /// <summary>The devices a fill reaches: the placed, enabled ones when "every device" is on, otherwise the selected one.</summary>
    List<string> FillTargets()
    {
        if (!_fillAll) return _selectedDevice is { } id && _visuals.ContainsKey(id) ? [id] : [];
        var placed = _m!.State.Current.Placements.Where(p => p.Enabled).Select(p => p.DeviceId);
        return placed.Where(_visuals.ContainsKey).ToList();
    }

    void SetPaintMany(IReadOnlyDictionary<string, IReadOnlyList<string?>?> paints)
    {
        foreach (var (id, paint) in paints)
            if (_visuals.TryGetValue(id, out var v)) v.Paint = paint is null || paint.All(x => x is null) ? null : paint;
        _m!.EditScene(sc => sc with
        {
            Placements = sc.Placements.Select(p => paints.ContainsKey(p.DeviceId) && _visuals.TryGetValue(p.DeviceId, out var v) ? p with { Paint = v.Paint } : p).ToList(),
        });
        _rendered = _m.State;
        UpdatePreview();
    }

    /// <summary>Gradients and rainbows run across the whole canvas when "every device" is on, so devices side by side share one picture.</summary>
    void Fill(Func<int, int, IReadOnlyList<string?>> single, Func<IReadOnlyList<DevicePlacement>, (Dictionary<string, int> Counts, Dictionary<string, int> Columns), Dictionary<string, IReadOnlyList<string?>>>? acrossCanvas)
    {
        var targets = FillTargets();
        if (targets.Count == 0) { SetNote(_fillAll ? "No devices are on the canvas yet." : "Select a device on the canvas first."); return; }
        SetNote("");
        var paints = new Dictionary<string, IReadOnlyList<string?>?>();
        if (_fillAll && acrossCanvas is not null)
        {
            var counts = targets.ToDictionary(t => t, t => _visuals[t].RealCount);
            var columns = targets.ToDictionary(t => t, t => _visuals[t].Columns);
            var placements = _m!.State.Current.Placements.Where(p => targets.Contains(p.DeviceId)).ToList();
            foreach (var (id, paint) in acrossCanvas(placements, (counts, columns))) paints[id] = paint;
        }
        else foreach (var id in targets) paints[id] = single(_visuals[id].RealCount, _visuals[id].Columns);
        SetPaintMany(paints);
    }

    void LibrarySave()
    {
        if (!Selected(out _, out var v)) return;
        if (v.Paint is null) { SetNote("Nothing is painted on the selected device yet, so there is nothing to save."); return; }
        if (PaintLibrary.Clean(_libraryName) is not { } name) { SetNote("Type a name for the picture first."); return; }
        SetNote(_library.Save(name, PaintTools.Resample(v.Paint, v.RealCount), v.Columns) ? "Saved " + name + " to the library." : "Could not save it: the library may be full or the folder is not writable.");
        RebuildLibrary();
    }

    void LibraryUse(string name, bool everyDevice)
    {
        var paint = _library.Load(name);
        if (paint is null) { SetNote("Could not read " + name + ": the file is missing or damaged."); RebuildLibrary(); return; }
        var targets = everyDevice ? _m!.State.Current.Placements.Where(p => p.Enabled).Select(p => p.DeviceId).Where(_visuals.ContainsKey).ToList()
            : _selectedDevice is { } id && _visuals.ContainsKey(id) ? [id] : [];
        if (targets.Count == 0) { SetNote(everyDevice ? "No devices are on the canvas yet." : "Select a device on the canvas first."); return; }
        SetNote("Applied " + name + " to " + targets.Count + " device" + (targets.Count == 1 ? "" : "s") + ", fitted to each one.");
        SetPaintMany(targets.ToDictionary(t => t, t => (IReadOnlyList<string?>?)PaintTools.Resample(paint, _visuals[t].RealCount)));
    }

    void RebuildLibrary()
    {
        _libraryList.Children.Clear();
        var entries = _library.List();
        if (entries.Count == 0) { _libraryList.Children.Add(Text("No saved pictures yet.", muted: true, size: 12)); return; }
        foreach (var entry in entries)
        {
            var e = entry;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text($"{e.Name}  ·  {e.Leds} LED{(e.Leds == 1 ? "" : "s")}", size: 13));
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Pill("Use on selected", () => LibraryUse(e.Name, false), "Use " + e.Name + " on the selected device"));
            actions.Children.Add(Pill("Use on all", () => LibraryUse(e.Name, true), "Use " + e.Name + " on every device"));
            actions.Children.Add(Pill("Rename", () =>
            {
                if (PaintLibrary.Clean(_libraryName) is not { } to) { SetNote("Type the new name in the name box first."); return; }
                SetNote(_library.Rename(e.Name, to) ? "Renamed " + e.Name + " to " + to + "." : "Could not rename it: that name is already used.");
                RebuildLibrary();
            }, "Rename " + e.Name + " to the name in the box"));
            actions.Children.Add(Pill("Delete", () => { SetNote(_library.Delete(e.Name) ? "Deleted " + e.Name + "." : "Could not delete " + e.Name + "."); RebuildLibrary(); }, "Delete " + e.Name));
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            _libraryList.Children.Add(row);
        }
    }

    // ---- paint tools ----

    bool Selected(out string id, out DeviceVisual v)
    {
        id = _selectedDevice ?? ""; v = null!;
        if (_selectedDevice is null || !_visuals.TryGetValue(_selectedDevice, out var found)) { SetNote("Select a device on the canvas first."); return false; }
        v = found;
        return true;
    }

    void SetPaint(string id, DeviceVisual v, IReadOnlyList<string?>? paint)
    {
        v.Paint = paint is null || paint.All(x => x is null) ? null : paint;
        _m!.EditScene(sc => sc with { Placements = sc.Placements.Select(p => p.DeviceId == id ? p with { Paint = v.Paint } : p).ToList() });
        _rendered = _m.State;
        UpdatePreview();
    }

    void MirrorSelected()
    {
        var targets = FillTargets().Where(t => _visuals[t].Paint is not null).ToList();
        if (targets.Count == 0) { SetNote("Nothing is painted yet on " + (_fillAll ? "any device." : "this device.")); return; }
        SetNote("");
        SetPaintMany(targets.ToDictionary(t => t, t => (IReadOnlyList<string?>?)PaintTools.Mirror(PaintTools.Resample(_visuals[t].Paint!, _visuals[t].RealCount), _visuals[t].Columns)));
    }

    void ExportPaint()
    {
        if (!Selected(out var id, out var v)) return;
        if (v.Paint is null) { SetNote("Nothing is painted on this device yet, so there is nothing to export."); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Ember paint|*.json|Colour list|*.txt", Title = "Export paint", FileName = "ember-paint" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var paint = PaintTools.Resample(v.Paint, v.RealCount);
            var text = dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                ? string.Join(Environment.NewLine, paint.Select(c => c ?? ""))
                : PaintFile.Export(paint, v.Columns, v.Label.Text);
            File.WriteAllText(dlg.FileName, text);
            SetNote("Exported " + paint.Count + " LEDs to " + Path.GetFileName(dlg.FileName) + ".");
        }
        catch (Exception ex) { SetNote("Could not save the file: " + ex.Message); }
    }

    void ImportPaint()
    {
        if (!Selected(out var id, out var v)) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Ember paint or colour list|*.json;*.txt|All files|*.*", Title = "Import paint" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var info = new FileInfo(dlg.FileName);
            if (info.Length > PaintFile.MaxBytes) { SetNote("That file is too large to be a paint file."); return; }
            if (PaintFile.TryImport(File.ReadAllText(dlg.FileName)) is not { } paint) { SetNote("That is not a paint file Ember can read. It should be an exported Ember paint file, or one #RRGGBB colour per line."); return; }
            SetNote("Imported " + paint.Count + " LEDs" + (paint.Count == v.RealCount ? "." : ", fitted to the " + v.RealCount + " LEDs of this device."));
            SetPaint(id, v, PaintTools.Resample(paint, v.RealCount));
        }
        catch (Exception ex) { SetNote("Could not read the file: " + ex.Message); }
    }

    void CommitPlacement(string id, DeviceVisual v)
    {
        _m!.EditScene(sc => sc with
        {
            Placements = sc.Placements.Select(p => p.DeviceId == id ? p with { X = v.X, Y = v.Y, Width = v.W, Height = v.H } : p).ToList(),
        });
        _rendered = _m.State;
    }

    void UpdatePreview()
    {
        var frame = _m!.Preview;
        foreach (var (id, v) in _visuals)
        {
            frame.TryGetValue(id, out var colours);
            for (var i = 0; i < v.Leds.Count; i++)
            {
                if (colours is null || colours.Length == 0)
                {
                    // Lights are off: still show which LEDs are painted, dimly, so painting can be seen.
                    var painted = v.Paint is { } pl && v.Leds.Count == v.RealCount && i < pl.Count ? LColor.TryParseHex(pl[i]) : null;
                    v.Leds[i].Color = painted is { } pc ? ToWpf(pc.Scaled(0.5)) : WColor.FromRgb(30, 26, 24);
                    continue;
                }
                var src = v.Leds.Count == colours.Length ? i : (int)((i + 0.5) / v.Leds.Count * colours.Length);
                v.Leds[i].Color = ToWpf(colours[Math.Clamp(src, 0, colours.Length - 1)]);
            }
        }
    }

    // ---- layers ----

    // When a device has lighting of its own, the layer editor can work on that instead of the scene everything else follows.
    string? _editingScene, _editingDevice;

    Scene LayerScene(LightingState state) =>
        _editingScene is { } name && state.Current.Placements.Any(p => p.Profile == name) && state.Scenes.FirstOrDefault(s => s.Name == name) is { } own ? own : state.Current;

    bool EditingOwn(LightingState state) => !ReferenceEquals(LayerScene(state), state.Current);

    void EditLayers(Func<Scene, Scene> edit)
    {
        var state = _m!.State;
        if (EditingOwn(state)) _m.EditSceneNamed(LayerScene(state).Name, edit); else _m.EditScene(edit);
    }

    void EditOwnLighting(string deviceId, string deviceName)
    {
        var name = _m!.GiveOwnLighting(deviceId, deviceName);
        if (name is null) { SetNote("There are too many scenes to add one for " + deviceName + ". Delete a scene first."); return; }
        _editingScene = name; _editingDevice = deviceName; _selectedLayer = null;
        SetNote(deviceName + " now has lighting of its own. The layers below are only for it; everything else keeps following the scene.");
        _rendered = _m.State;
        RebuildLayers(_m.State);
        RebuildDevices(_m.State);
    }

    void AddLayer(Effect effect)
    {
        var m = _m!;
        if (LayerScene(m.State).Layers.Count >= LightingState.MaxLayers) { SetNote("A scene can have up to 20 layers."); return; }
        var id = Guid.NewGuid().ToString("N")[..8];
        EditLayers(sc => sc with { Layers = [.. sc.Layers, new Layer(id, effect.Label, effect)] });
        _selectedLayer = id;
        _rendered = m.State;
        RebuildLayers(m.State);
    }

    void EditLayer(string id, Func<Layer, Layer> change, bool rebuild = false)
    {
        EditLayers(sc => sc with { Layers = sc.Layers.Select(l => l.Id == id ? change(l) : l).ToList() });
        _rendered = _m!.State;
        if (rebuild) RebuildLayers(_m.State);
    }

    void MoveLayer(string id, int delta)
    {
        EditLayers(sc =>
        {
            var list = sc.Layers.ToList();
            var i = list.FindIndex(l => l.Id == id);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= list.Count) return sc;
            (list[i], list[j]) = (list[j], list[i]);
            return sc with { Layers = list };
        });
        _rendered = _m!.State;
        RebuildLayers(_m.State);
    }

    void RemoveLayer(string id)
    {
        EditLayers(sc => sc with { Layers = sc.Layers.Where(l => l.Id != id).ToList() });
        if (_selectedLayer == id) _selectedLayer = null;
        _rendered = _m!.State;
        RebuildLayers(_m.State);
    }

    void RebuildLayers(LightingState state)
    {
        _layers.Children.Clear();
        var layers = LayerScene(state).Layers;
        if (EditingOwn(state))
        {
            var banner = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            banner.Children.Add(Text($"Editing the lights of {_editingDevice} only. Everything else keeps following the scene.", weight: FontWeights.SemiBold));
            banner.Children.Add(Pill("Back to the whole scene", () => { _editingScene = null; _selectedLayer = null; _rendered = _m!.State; RebuildLayers(_m.State); RebuildDevices(_m.State); }, "Back to the whole scene"));
            _layers.Children.Add(banner);
        }
        if (layers.Count == 0) _layers.Children.Add(Text("This scene has no layers, so every light stays dark. Add one above.", muted: true));
        if (_selectedLayer is null || layers.All(l => l.Id != _selectedLayer)) _selectedLayer = layers.LastOrDefault()?.Id;

        foreach (var layer in layers.Reverse())
        {
            var l = layer;
            var selected = l.Id == _selectedLayer;
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var on = Switch("", l.Enabled, v => EditLayer(l.Id, x => x with { Enabled = v }));
            AutomationProperties.SetName(on, "Layer " + l.Name + " on");
            on.Margin = new Thickness(0, 0, 8, 0);
            var pick = new Button { Style = Sty("ActionButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 0) };
            pick.Content = Text(l.Name + "  ·  " + l.Effect.Label, weight: selected ? FontWeights.SemiBold : FontWeights.Normal);
            AutomationProperties.SetName(pick, "Edit layer " + l.Name);
            pick.Click += (_, _) => { _selectedLayer = l.Id; RebuildLayers(_m!.State); };
            if (selected) pick.Background = Res("SegmentOnBrush");
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Pill("↑", () => MoveLayer(l.Id, +1), "Move " + l.Name + " up"));
            actions.Children.Add(Pill("↓", () => MoveLayer(l.Id, -1), "Move " + l.Name + " down"));
            actions.Children.Add(Pill("Remove", () => RemoveLayer(l.Id), "Remove layer " + l.Name));
            Grid.SetColumn(pick, 1);
            Grid.SetColumn(actions, 2);
            row.Children.Add(on);
            row.Children.Add(pick);
            row.Children.Add(actions);
            _layers.Children.Add(row);
        }
        RebuildEditor(state);
    }

    void RebuildEditor(LightingState state)
    {
        _editor.Children.Clear();
        var layer = LayerScene(state).Layers.FirstOrDefault(l => l.Id == _selectedLayer);
        if (layer is null) return;
        var id = layer.Id;
        _editor.Children.Add(new Border { Height = 1, Background = Res("PanelBorderBrush"), Margin = new Thickness(0, 12, 0, 12) });
        _editor.Children.Add(Text("Edit layer", weight: FontWeights.SemiBold));

        var name = new TextBox { Text = layer.Name, Style = Sty("TextField"), Width = 260, MaxLength = 40, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(name, "Layer name");
        name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) EditLayer(id, x => x with { Name = name.Text.Trim() }, rebuild: true); };
        _editor.Children.Add(name);

        _editor.Children.Add(Text("How it mixes with the layers below", muted: true));
        _editor.Children.Add(Segments(["Normal", "Add", "Screen", "Multiply"], (int)layer.Blend, i => EditLayer(id, x => x with { Blend = (BlendMode)i }), "Blend" + id));
        _editor.Children.Add(Labelled("Opacity", 0, 100, layer.Opacity * 100, v => $"{v:0}%", v => EditLayer(id, x => x with { Opacity = v / 100 }), 1));

        EffectFields(layer.Effect, id, change => EditLayer(id, x => x with { Effect = change(x.Effect) }));
    }

    /// <summary>The fields for one effect. <paramref name="apply"/> hands a change of the effect to whoever owns it (a layer, or the group effect around it).</summary>
    void EffectFields(Effect effect, string id, Action<Func<Effect, Effect>> apply)
    {
        switch (effect)
        {
            case StaticEffect e:
                _editor.Children.Add(ColorField("Colour", e.Color, c => apply(x => ((StaticEffect)x) with { Color = c })));
                break;
            case WaveEffect e:
                _editor.Children.Add(ColorField("First colour", e.A, c => apply(x => ((WaveEffect)x) with { A = c })));
                _editor.Children.Add(ColorField("Second colour", e.B, c => apply(x => ((WaveEffect)x) with { B = c })));
                _editor.Children.Add(Labelled("Speed", 0, 200, e.Speed * 100, v => $"{v / 100:0.00} /s", v => apply(x => ((WaveEffect)x) with { Speed = v / 100 }), 1));
                _editor.Children.Add(Labelled("Wavelength", 10, 400, e.Wavelength * 100, v => $"{v / 100:0.0} wide", v => apply(x => ((WaveEffect)x) with { Wavelength = v / 100 }), 1));
                _editor.Children.Add(Labelled("Direction", 0, 360, e.AngleDegrees, v => $"{v:0}°", v => apply(x => ((WaveEffect)x) with { AngleDegrees = v }), 1));
                break;
            case BreathingEffect e:
                _editor.Children.Add(ColorField("Colour", e.Color, c => apply(x => ((BreathingEffect)x) with { Color = c })));
                _editor.Children.Add(Labelled("One breath takes", 1, 20, e.PeriodSeconds, v => $"{v:0.#} s", v => apply(x => ((BreathingEffect)x) with { PeriodSeconds = v }), 0.5));
                break;
            case TemperatureGlowEffect e:
                _editor.Children.Add(Text("Follows the temperature of", muted: true));
                _editor.Children.Add(Segments(["CPU", "GPU"], (int)e.Source, i => apply(x => ((TemperatureGlowEffect)x) with { Source = (TempSensor)i }), "Sensor" + id));
                _editor.Children.Add(ColorField("When cool", e.Cold, c => apply(x => ((TemperatureGlowEffect)x) with { Cold = c })));
                _editor.Children.Add(ColorField("When hot", e.Hot, c => apply(x => ((TemperatureGlowEffect)x) with { Hot = c })));
                _editor.Children.Add(Labelled("Cool up to", 20, 100, e.MinC, v => $"{v:0} °C", v => apply(x => ((TemperatureGlowEffect)x) with { MinC = v }), 1));
                _editor.Children.Add(Labelled("Hot from", 20, 100, e.MaxC, v => $"{v:0} °C", v => apply(x => ((TemperatureGlowEffect)x) with { MaxC = v }), 1));
                break;
            case AudioPulseEffect e:
                _editor.Children.Add(Text("Follows this part of the sound", muted: true));
                _editor.Children.Add(Segments(["Everything", "Bass", "Middle", "Treble"], (int)e.Band, i => apply(x => ((AudioPulseEffect)x) with { Band = (AudioBand)i }), "Band" + id));
                _editor.Children.Add(ColorField("When quiet", e.Quiet, c => apply(x => ((AudioPulseEffect)x) with { Quiet = c })));
                _editor.Children.Add(ColorField("When loud", e.Loud, c => apply(x => ((AudioPulseEffect)x) with { Loud = c })));
                _editor.Children.Add(Labelled("Sensitivity", 10, 400, e.Sensitivity * 100, v => $"{v / 100:0.0}x", v => apply(x => ((AudioPulseEffect)x) with { Sensitivity = v / 100 }), 5));
                _editor.Children.Add(Text("Listens to what your PC is playing while this layer is on. Nothing is recorded or kept.", muted: true));
                break;
            case AudioSpectrumEffect e:
                _editor.Children.Add(ColorField("Low pitches", e.Low, c => apply(x => ((AudioSpectrumEffect)x) with { Low = c })));
                _editor.Children.Add(ColorField("High pitches", e.High, c => apply(x => ((AudioSpectrumEffect)x) with { High = c })));
                _editor.Children.Add(Labelled("Sensitivity", 10, 400, e.Sensitivity * 100, v => $"{v / 100:0.0}x", v => apply(x => ((AudioSpectrumEffect)x) with { Sensitivity = v / 100 }), 5));
                _editor.Children.Add(Text("Low pitches on the left of the canvas, high on the right. Listens to what your PC is playing while this layer is on. Nothing is recorded or kept.", muted: true));
                break;
            case AudioBarsEffect e:
                _editor.Children.Add(ColorField("Bottom of the bars", e.Low, c => apply(x => ((AudioBarsEffect)x) with { Low = c })));
                _editor.Children.Add(ColorField("Top of the bars", e.High, c => apply(x => ((AudioBarsEffect)x) with { High = c })));
                _editor.Children.Add(Labelled("Sensitivity", 10, 400, e.Sensitivity * 100, v => $"{v / 100:0.0}x", v => apply(x => ((AudioBarsEffect)x) with { Sensitivity = v / 100 }), 5));
                _editor.Children.Add(Text("Drawn on", muted: true));
                _editor.Children.Add(Segments(["Each device", "The whole canvas"], (int)e.Scope, i => apply(x => ((AudioBarsEffect)x) with { Scope = (EffectScope)i }), "BarsScope" + id));
                _editor.Children.Add(Text("One bar per pitch, low on the left. On a keyboard the bars rise up the keys; on a single strip they become brightness. Listens to what your PC is playing while this layer is on. Nothing is recorded or kept.", muted: true));
                break;
            case RippleEffect e:
                _editor.Children.Add(ColorField("Ring colour", e.Colour, c => apply(x => ((RippleEffect)x) with { Colour = c })));
                _editor.Children.Add(Labelled("A new ring every", 0.2, 10, e.IntervalSeconds, v => $"{v:0.0} s", v => apply(x => ((RippleEffect)x) with { IntervalSeconds = v }), 0.1));
                _editor.Children.Add(Labelled("Speed", 5, 200, e.Speed * 100, v => $"{v / 100:0.00} /s", v => apply(x => ((RippleEffect)x) with { Speed = v / 100 }), 1));
                _editor.Children.Add(Labelled("Ring width", 2, 60, e.Width * 100, v => $"{v:0}%", v => apply(x => ((RippleEffect)x) with { Width = v / 100 }), 1));
                _editor.Children.Add(Labelled("Centre across", 0, 100, e.CenterX * 100, v => $"{v:0}%", v => apply(x => ((RippleEffect)x) with { CenterX = v / 100 }), 1));
                _editor.Children.Add(Labelled("Centre down", 0, 100, e.CenterY * 100, v => $"{v:0}%", v => apply(x => ((RippleEffect)x) with { CenterY = v / 100 }), 1));
                _editor.Children.Add(Text("Drawn on", muted: true));
                _editor.Children.Add(Segments(["The whole canvas", "Each device"], (int)e.Scope == 1 ? 0 : 1, i => apply(x => ((RippleEffect)x) with { Scope = i == 0 ? EffectScope.Canvas : EffectScope.Device }), "RippleScope" + id));
                _editor.Children.Add(Text("Only the rings are coloured, so put this above another layer to see it spread over it.", muted: true));
                break;
            case ReactiveKeysEffect e:
                _editor.Children.Add(ColorField("Key colour", e.Colour, c => apply(x => ((ReactiveKeysEffect)x) with { Colour = c })));
                _editor.Children.Add(ColorField("Fades to", e.Tail, c => apply(x => ((ReactiveKeysEffect)x) with { Tail = c })));
                _editor.Children.Add(Labelled("Fade out over", 0.1, 4, e.FadeSeconds, v => $"{v:0.0} s", v => apply(x => ((ReactiveKeysEffect)x) with { FadeSeconds = v }), 0.1));
                _editor.Children.Add(Labelled("Glow size", 3, 40, e.Radius * 100, v => $"{v:0}%", v => apply(x => ((ReactiveKeysEffect)x) with { Radius = v / 100 }), 1));
                _editor.Children.Add(Text("Flashes where you press a key on a keyboard. While this layer is on, Ember notes that a key went down and where it sits on the keyboard, and nothing else: no text, nothing is saved or sent. Put it above another layer so the glow shows over it.", muted: true));
                break;
            case FireEffect e:
                _editor.Children.Add(ColorField("Flame colour", e.Base, c => apply(x => ((FireEffect)x) with { Base = c })));
                _editor.Children.Add(ColorField("Hottest colour", e.Tip, c => apply(x => ((FireEffect)x) with { Tip = c })));
                _editor.Children.Add(Labelled("Speed", 10, 500, e.Speed * 100, v => $"{v / 100:0.0}x", v => apply(x => ((FireEffect)x) with { Speed = v / 100 }), 5));
                _editor.Children.Add(Labelled("Heat", 10, 150, e.Intensity * 100, v => $"{v:0}%", v => apply(x => ((FireEffect)x) with { Intensity = v / 100 }), 1));
                _editor.Children.Add(Text("Drawn on", muted: true));
                _editor.Children.Add(Segments(["Each device", "The whole canvas"], (int)e.Scope, i => apply(x => ((FireEffect)x) with { Scope = (EffectScope)i }), "FireScope" + id));
                _editor.Children.Add(Text("Hot at the bottom and cooler towards the top. On a single strip the heat flickers along it. It covers the layers below, so put it at the bottom of the stack.", muted: true));
                break;
            case RainEffect e:
                _editor.Children.Add(ColorField("Drop colour", e.Colour, c => apply(x => ((RainEffect)x) with { Colour = c })));
                _editor.Children.Add(Labelled("How many drops", 0, 100, e.Density * 100, v => $"{v:0}%", v => apply(x => ((RainEffect)x) with { Density = v / 100 }), 1));
                _editor.Children.Add(Labelled("Speed", 5, 300, e.Speed * 100, v => $"{v / 100:0.00} /s", v => apply(x => ((RainEffect)x) with { Speed = v / 100 }), 1));
                _editor.Children.Add(Labelled("Trail length", 5, 100, e.Length * 100, v => $"{v:0}%", v => apply(x => ((RainEffect)x) with { Length = v / 100 }), 1));
                _editor.Children.Add(Text("Drawn on", muted: true));
                _editor.Children.Add(Segments(["Each device", "The whole canvas"], (int)e.Scope, i => apply(x => ((RainEffect)x) with { Scope = (EffectScope)i }), "RainScope" + id));
                _editor.Children.Add(Text("Drops fall down a keyboard or grid and run along a single strip. Only the drops are coloured, so put this above another layer.", muted: true));
                break;
            case KeyGroupEffect e:
                _editor.Children.Add(Text("Only on these keys", muted: true));
                var groups = Enum.GetValues<KeyGroup>();
                _editor.Children.Add(Segments(groups.Select(KeyGroups.Label).ToList(), Array.IndexOf(groups, e.Group), i => apply(x => ((KeyGroupEffect)x) with { Group = groups[i] }), "KeyGroup" + id));
                _editor.Children.Add(Text("What they show", muted: true));
                var kinds = new (string Label, Func<Effect> Make)[]
                {
                    ("Colour", () => new StaticEffect(new LColor(1.0, 0.45, 0.1))),
                    ("Wave", () => new WaveEffect(new LColor(1.0, 0.45, 0.1), new LColor(0.2, 0.3, 1.0))),
                    ("Breathing", () => new BreathingEffect(new LColor(1.0, 0.45, 0.1))),
                    ("Fire", () => new FireEffect(new LColor(1.0, 0.2, 0.0), new LColor(1.0, 0.9, 0.3))),
                    ("Rain", () => new RainEffect(new LColor(0.2, 0.6, 1.0))),
                };
                var current = e.Inner switch { StaticEffect => 0, WaveEffect => 1, BreathingEffect => 2, FireEffect => 3, RainEffect => 4, _ => -1 };
                _editor.Children.Add(Segments(kinds.Select(k => k.Label).ToList(), current, i => apply(x => ((KeyGroupEffect)x) with { Inner = kinds[i].Make() }), "KeyInner" + id));
                EffectFields(e.Inner, id + "i", change => apply(x => ((KeyGroupEffect)x) with { Inner = change(((KeyGroupEffect)x).Inner) }));
                _editor.Children.Add(Text("Works on keyboards only; other devices ignore it. Add one layer per group to give each part of the keyboard its own look.", muted: true));
                break;
            case ScreenSyncEffect:
                _editor.Children.Add(Text("Copies the colours of your main screen onto the canvas in four vertical strips, left to right. It looks at the screen only while this layer is on.", muted: true));
                break;
        }
    }

    // ---- devices and status ----

    void RebuildDevices(LightingState state)
    {
        _devices.Children.Clear();
        var devices = _m!.Devices;
        if (devices.Count == 0) _devices.Children.Add(Text("No devices found yet.", muted: true));
        var placed = state.Current.Placements.ToDictionary(p => p.DeviceId);
        foreach (var d in devices)
        {
            var info = d;
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(Text(info.Name));
            text.Children.Add(Text($"{info.Kind} · {info.LedCount} LED{(info.LedCount == 1 ? "" : "s")} · {info.Source}" + (info.Disabled ? " · disabled on the Devices page" : ""), muted: true, size: 12));
            if (_m.Problems.TryGetValue(info.Id, out var problem)) text.Children.Add(Text("Problem: " + problem, size: 12));
            var on = Switch("Use in this scene", placed.TryGetValue(info.Id, out var p) && p.Enabled, v =>
            {
                _m.EditScene(sc => sc with { Placements = sc.Placements.Select(x => x.DeviceId == info.Id ? x with { Enabled = v } : x).ToList() });
                _rendered = _m.State;
                RebuildVisuals(_m.State);
            });
            AutomationProperties.SetName(on, "Use " + info.Name + " in this scene");
            on.Margin = new Thickness(0);
            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(on);
            if (placed.TryGetValue(info.Id, out var placement))
            {
                var names = state.Scenes.Select(x => x.Name).Where(n => n != state.Current.Name).ToList();
                var own = placement.Profile is { } pn && names.Contains(pn) ? pn : null;
                var profile = Pill(own is null ? "Light profile: follows the scene" : "Light profile: " + own, () =>
                {
                    var next = own is null ? names.FirstOrDefault() : names.IndexOf(own) + 1 < names.Count ? names[names.IndexOf(own) + 1] : null;
                    _m.EditScene(sc => sc with { Placements = sc.Placements.Select(x => x.DeviceId == info.Id ? x with { Profile = next } : x).ToList() });
                    _rendered = _m.State;
                    RebuildDevices(_m.State);
                }, "Light profile for " + info.Name);
                var hasOwn = placement.Profile is { } pf && pf != state.Current.Name && state.Scenes.Any(x => x.Name == pf);
                var ownLights = Pill(hasOwn ? "Edit its own lighting" : "Give it its own lighting", () => EditOwnLighting(info.Id, info.Name), "Own lighting for " + info.Name);
                ownLights.Margin = new Thickness(0, 4, 0, 0);
                ownLights.ToolTip = "Lets this device show different effects from the rest. It starts as a copy of the current layers; the layer editor then works on it alone.";
                right.Children.Add(ownLights);
                if (hasOwn)
                {
                    var follow = Pill("Follow the scene again", () => { _m.FollowScene(info.Id); if (_editingScene == placement.Profile) _editingScene = null; _rendered = _m.State; RebuildLayers(_m.State); RebuildDevices(_m.State); }, "Follow the scene: " + info.Name);
                    follow.Margin = new Thickness(0, 4, 0, 0);
                    right.Children.Add(follow);
                }
                profile.Margin = new Thickness(0, 4, 0, 0);
                profile.ToolTip = names.Count == 0 ? "Save another scene first: a device can show any other scene as its own profile." : "Click to choose which scene this device shows: the current scene, or one of your other scenes.";
                right.Children.Add(profile);
            }
            if (info.Source == "Govee" && info.Realtime)
            {
                var steps = new[] { 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20 };
                var now = _m.SegmentsOf(info.Id);
                var seg = Pill(now <= 1 ? "Segments: whole lamp" : $"Segments: {now}", () =>
                {
                    var next = steps[(Array.IndexOf(steps, now) + 1) % steps.Length];
                    _m.SetSegments(info.Id, next);
                    _m.RefreshDevices();
                }, "Segments for " + info.Name);
                seg.Margin = new Thickness(0, 4, 0, 0);
                seg.ToolTip = "Experimental. For lamps that can light parts of themselves separately (light bars, strips): choose how many parts to drive. Whole lamp is one colour. If the lamp ignores it, set it back to whole lamp.";
                right.Children.Add(seg);
                var detect = Pill("Detect segments", async () =>
                {
                    SetNote("Looking up how many segments " + info.Name + " has…");
                    var n = await _m.DetectSegmentsAsync(info.Id);
                    SetNote(n is { } count
                        ? $"{info.Name} reports {count} segments; Ember will drive it that way. If the lamp ignores it, set Segments back to whole lamp."
                        : "Ember could not tell. It needs a Govee API key in Settings for lamps it does not know, or you can pick a number with the Segments button.");
                    RebuildDevices(_m.State);
                }, "Detect segments for " + info.Name);
                detect.Margin = new Thickness(0, 4, 0, 0);
                detect.ToolTip = "Asks Govee (and Ember's list of known lamps) how many parts this lamp can light separately, and sets it. Experimental.";
                right.Children.Add(detect);
            }
            Grid.SetColumn(right, 1);
            row.Children.Add(text);
            row.Children.Add(right);
            _devices.Children.Add(row);
        }
    }

    string _statusKey = "";

    void UpdateStatus()
    {
        var key = string.Join("|", _m!.SourceStatus) + _m.SaveFailed + _note;
        if (key == _statusKey) return;
        _statusKey = key;
        _status.Children.Clear();
        foreach (var line in _m!.SourceStatus) _status.Children.Add(Text(line, muted: true, size: 12));
        if (_m.SaveFailed) _status.Children.Add(Text("Could not save your lighting settings; they apply until you close Ember.", size: 12));
        if (_note.Length > 0) _status.Children.Add(Text(_note, size: 12));
        _status.Margin = new Thickness(0, 8, 0, 0);
    }
}
