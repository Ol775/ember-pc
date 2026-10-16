using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ember.Lighting;
using LColor = Ember.Lighting.Color;
using WColor = System.Windows.Media.Color;

namespace Ember.App;

/// <summary>Small builders for the Lights page, which is made in code because most of it follows the scene.</summary>
static class LightsUi
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);
    public static Style Sty(string key) => (Style)Application.Current.FindResource(key);

    public static WColor ToWpf(LColor c)
    {
        var (r, g, b) = c.ToBytes();
        return WColor.FromRgb(r, g, b);
    }

    public static TextBlock Text(string text, bool muted = false, double size = 14, FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush");
        if (weight is { } w) t.FontWeight = w;
        return t;
    }

    /// <summary>The small "Beta" tag shown on features that are written and tested but not yet proven on enough real hardware.</summary>
    public static FrameworkElement BetaChip(string tooltip)
    {
        var tag = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center, ToolTip = tooltip,
            Child = new TextBlock { Text = "Beta", FontSize = 11, FontWeight = FontWeights.SemiBold } };
        ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
        tag.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        return tag;
    }

    public static Button Pill(string label, Action onClick, string? name = null)
    {
        var b = new Button { Content = label, Style = Sty("ActionButton"), Margin = new Thickness(0, 0, 8, 6) };
        AutomationProperties.SetName(b, name ?? label);
        b.Click += (_, _) => onClick();
        return b;
    }

    public static CheckBox Switch(string label, bool on, Action<bool> changed)
    {
        var c = new CheckBox { Content = label, IsChecked = on, Style = Sty("SwitchBox"), Margin = new Thickness(0, 0, 16, 6) };
        c.Click += (_, _) => changed(c.IsChecked == true);
        return c;
    }

    /// <summary>A row of options on a rounded track; one is chosen.</summary>
    public static FrameworkElement Segments(IReadOnlyList<string> options, int selected, Action<int> picked, string group)
    {
        var track = new Border { Style = Sty("SegmentTrack"), Margin = new Thickness(0, 0, 0, 6) };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var rb = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected, Style = Sty("Segment") };
            rb.Click += (_, _) => picked(index);
            panel.Children.Add(rb);
        }
        track.Child = panel;
        return track;
    }

    /// <summary>"Name  [slider]  value": the slider reports the value as it moves.</summary>
    public static FrameworkElement Labelled(string name, double min, double max, double value, Func<double, string> format, Action<double> changed, double step = 0)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var label = Text(name, muted: true);
        var readout = Text(format(value));
        readout.TextAlignment = TextAlignment.Right;
        var slider = new Slider { Style = Sty("PillSlider"), Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(8, 0, 8, 0) };
        if (step > 0) { slider.SmallChange = step; slider.LargeChange = step * 5; }
        AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, e) =>
        {
            var v = step > 0 ? Math.Round(e.NewValue / step) * step : e.NewValue;
            readout.Text = format(v);
            changed(v);
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(label);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    static readonly string[] Palette =
    [
        "#FF3B30", "#FF7A2F", "#FFB020", "#FFE14D", "#7CE05B", "#2FD28A", "#2FD2D2", "#4EA8DE",
        "#3B6BFF", "#7A4DFF", "#C84DFF", "#FF4DA6", "#FFFFFF", "#C8C8C8", "#808080", "#000000",
    ];

    /// <summary>A swatch that opens a palette, next to a box for typing #RRGGBB.</summary>
    public static FrameworkElement ColorField(string name, LColor color, Action<LColor> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Text(name, muted: true));

        var swatchFill = new SolidColorBrush(ToWpf(color));
        var swatch = new Button
        {
            Width = 34, Height = 26, Margin = new Thickness(8, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand,
            Template = SwatchTemplate(), Tag = swatchFill,
        };
        AutomationProperties.SetName(swatch, name + " colour, choose from palette");
        var box = new TextBox { Text = color.ToHex(), Width = 90, Style = Sty("TextField"), Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(box, name + " as hex, for example #FF7A2F");

        void Apply(LColor c, bool setBox)
        {
            swatchFill.Color = ToWpf(c);
            if (setBox) box.Text = c.ToHex();
            changed(c);
        }

        var popup = new Popup { PlacementTarget = swatch, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        var grid = new UniformGrid { Columns = 8 };
        foreach (var hex in Palette)
        {
            var c = LColor.TryParseHex(hex)!.Value;
            var cell = new Button { Width = 28, Height = 28, Margin = new Thickness(3), Template = SwatchTemplate(), Tag = new SolidColorBrush(ToWpf(c)) };
            AutomationProperties.SetName(cell, hex);
            cell.Click += (_, _) => { popup.IsOpen = false; Apply(c, true); };
            grid.Children.Add(cell);
        }
        popup.Child = new Border { Background = Res("PanelBrush"), BorderBrush = Res("PanelBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8), Child = grid };
        swatch.Click += (_, _) => popup.IsOpen = true;
        box.TextChanged += (_, _) => { if (LColor.TryParseHex(box.Text) is { } c) Apply(c, false); };

        Grid.SetColumn(swatch, 1);
        Grid.SetColumn(box, 2);
        row.Children.Add(swatch);
        row.Children.Add(box);
        return row;
    }


    /// <summary>
    /// The colours of a gradient as a bar with a handle for every stop: drag a handle to move it, click a handle to choose it and
    /// change its colour below, click the bar to add a stop (up to six), and remove the chosen one while more than two remain.
    /// </summary>
    public static FrameworkElement StopBar(GradientEffect effect, Action<GradientEffect> changed)
    {
        const double Handle = 16, Height = 34;
        var stops = effect.Stops.ToList();
        var selected = 0;
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 6) };
        var bar = new Border { Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(Handle / 2, 5, Handle / 2, 5) };
        var canvas = new Canvas { Height = Height, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Arrow };
        var holder = new Grid { Height = Height };
        holder.Children.Add(bar);
        holder.Children.Add(canvas);
        var detail = new StackPanel();
        root.Children.Add(holder);
        root.Children.Add(detail);
        AutomationProperties.SetName(canvas, "Gradient colours: click to add a stop, drag a handle to move it");

        void Paint()
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            foreach (var s in stops.OrderBy(s => s.At)) brush.GradientStops.Add(new GradientStop(ToWpf(s.Colour), Math.Clamp(s.At, 0, 1)));
            bar.Background = brush;
        }

        void Notify() => changed(effect with { Stops = stops.OrderBy(s => s.At).Select(s => s).ToList() });

        void Highlight()
        {
            for (var i = 0; i < canvas.Children.Count; i++)
                if (canvas.Children[i] is Border b) b.BorderBrush = i == selected ? Res("AccentBrush") : Brushes.White;
        }

        void Layout()
        {
            canvas.Children.Clear();
            var width = Math.Max(1, holder.ActualWidth - Handle);
            for (var i = 0; i < stops.Count; i++)
            {
                var index = i;
                var thumb = new Border
                {
                    Width = Handle, Height = Height, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(ToWpf(stops[i].Colour)), Cursor = System.Windows.Input.Cursors.SizeWE,
                };
                thumb.BorderBrush = index == selected ? Res("AccentBrush") : Brushes.White;
                AutomationProperties.SetName(thumb, $"Colour stop {index + 1} of {stops.Count}");
                Canvas.SetLeft(thumb, Math.Clamp(stops[i].At, 0, 1) * width);
                // Choosing a handle only recolours the rings: the handle itself stays, so it keeps the mouse while it is dragged.
                thumb.MouseLeftButtonDown += (_, e) => { selected = index; thumb.CaptureMouse(); e.Handled = true; Highlight(); Detail(); };
                thumb.MouseMove += (_, e) =>
                {
                    if (!thumb.IsMouseCaptured) return;
                    var at = Math.Clamp((e.GetPosition(canvas).X - Handle / 2) / width, 0, 1);
                    stops[index] = stops[index] with { At = at };
                    Canvas.SetLeft(thumb, at * width);
                    Paint();
                    Notify();
                };
                thumb.MouseLeftButtonUp += (_, _) => thumb.ReleaseMouseCapture();
                // Keyboard: arrows move the stop (Shift: larger steps), Delete removes it while more than two remain.
                thumb.Focusable = true;
                thumb.KeyDown += (_, e) =>
                {
                    var step = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0 ? 0.05 : 0.01;
                    switch (e.Key)
                    {
                        case System.Windows.Input.Key.Left or System.Windows.Input.Key.Right:
                            var at = Math.Clamp(stops[index].At + (e.Key == System.Windows.Input.Key.Left ? -step : step), 0, 1);
                            stops[index] = stops[index] with { At = at };
                            Canvas.SetLeft(thumb, at * width);
                            Paint();
                            Notify();
                            e.Handled = true;
                            break;
                        case System.Windows.Input.Key.Delete when stops.Count > 2:
                            stops.RemoveAt(index);
                            selected = Math.Min(selected, stops.Count - 1);
                            Paint(); Layout(); Detail(); Notify();
                            e.Handled = true;
                            break;
                    }
                };
                canvas.Children.Add(thumb);
            }
        }

        void Detail()
        {
            detail.Children.Clear();
            var row = new DockPanel();
            if (stops.Count > 2)
            {
                var remove = Pill("Remove stop", () => { stops.RemoveAt(selected); selected = Math.Min(selected, stops.Count - 1); Paint(); Layout(); Detail(); Notify(); }, "Remove the chosen colour stop");
                remove.Margin = new Thickness(0);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }
            row.Children.Add(ColorField("Stop " + (selected + 1), stops[selected].Colour, c => { stops[selected] = stops[selected] with { Colour = c }; Paint(); Layout(); Notify(); }));
            detail.Children.Add(row);
        }

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            if (stops.Count >= 6) return;
            var width = Math.Max(1, holder.ActualWidth - Handle);
            var at = Math.Clamp((e.GetPosition(canvas).X - Handle / 2) / width, 0, 1);
            stops.Add(new ColorStop(at, effect with { Stops = stops.OrderBy(s => s.At).ToList() } is var g ? g.At(at) : stops[0].Colour));
            selected = stops.Count - 1;
            Paint(); Layout(); Detail(); Notify();
        };
        holder.SizeChanged += (_, _) => Layout();
        Paint();
        Detail();
        return root;
    }

    static ControlTemplate SwatchTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(WColor.FromArgb(90, 255, 255, 255)));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Tag") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    public static GroupBox Panel(string header, UIElement content, Thickness? margin = null)
    {
        var g = new GroupBox { Header = header, Style = Sty("Panel"), Content = content, Margin = margin ?? new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetName(g, header);
        return g;
    }
}
