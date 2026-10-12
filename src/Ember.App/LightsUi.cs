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
