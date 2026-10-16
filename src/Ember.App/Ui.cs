using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Ember.App;

/// <summary>Small builders for the pages of the redesign, so every card, label and figure is drawn the same way as in the mockups.</summary>
static class Ui
{
    public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>A rounded card: the panel colour with a hairline edge. <paramref name="soft"/> gives the accent-tinted card the mockups use for the main figure.</summary>
    public static Border Card(UIElement child, Thickness? padding = null, bool soft = false, string? name = null)
    {
        var card = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(20, 16, 20, 16), Child = child };
        card.SetResourceReference(Border.BackgroundProperty, soft ? "SoftCardBrush" : "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, soft ? "SoftCardBorderBrush" : "PanelBorderBrush");
        if (name is not null) AutomationProperties.SetName(card, name);
        return card;
    }

    /// <summary>A text run: size, weight and colour (a resource key) in one call.</summary>
    public static TextBlock Text(string text, double size = 14, string brush = "TextBrush", bool bold = false, bool mono = false, bool semibold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (bold || semibold) t.FontWeight = FontWeights.Bold;
        if (mono) t.FontFamily = Fonts.Mono;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static TextBlock Muted(string text, double size = 13) => Text(text, size, "MutedBrush");

    /// <summary>The small capital heading of a card.</summary>
    public static TextBlock Label(string text, string brush = "MutedBrush")
    {
        var t = Text(text.ToUpperInvariant(), 11.5, brush, bold: true);
        t.TextWrapping = TextWrapping.NoWrap;
        return t;
    }

    /// <summary>The page title (28 px) with a sentence under it, and an optional control on the right.</summary>
    public static FrameworkElement Heading(string title, string? subtitle, FrameworkElement? right = null)
    {
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (right is not null)
        {
            right.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
        }
        var text = new StackPanel();
        var h = Text(title, 28, bold: true);
        h.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetHeadingLevel(h, AutomationHeadingLevel.Level2);
        text.Children.Add(h);
        if (!string.IsNullOrEmpty(subtitle)) text.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }.With(Foreground("MutedBrush")));
        head.Children.Add(text);
        return head;
    }

    static Action<TextBlock> Foreground(string key) => t => t.SetResourceReference(TextBlock.ForegroundProperty, key);

    static TextBlock With(this TextBlock t, Action<TextBlock> apply) { apply(t); return t; }

    /// <summary>A pill with an optional green dot, such as "Live, updated just now".</summary>
    public static Border Chip(string text, bool dot)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (dot) row.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = ThemeManager.Good, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(Text(text, 13, "MutedBrush"));
        var chip = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 6), Child = row, HorizontalAlignment = HorizontalAlignment.Right };
        chip.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        chip.SetResourceReference(Border.BorderBrushProperty, "PanelBorderBrush");
        return chip;
    }

    /// <summary>A line icon drawn from a path on a 24 by 24 grid.</summary>
    public static FrameworkElement Icon(string data, double size = 18, string brush = "AccentTextBrush", double stroke = 2)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data), StrokeThickness = stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        path.SetResourceReference(Shape.StrokeProperty, brush);
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    /// <summary>A rounded tile with an icon in it, as on the dashboard tiles.</summary>
    public static Border IconTile(string data, double size = 38)
    {
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Center, Child = Icon(data) };
        tile.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        return tile;
    }

    /// <summary>A horizontal bar: the filled part is <paramref name="fraction"/> of the width.</summary>
    public static Grid Bar(double fraction, string fill = "AccentBrush", double height = 6)
    {
        var grid = new Grid { Height = height };
        var track = new Border { CornerRadius = new CornerRadius(height / 2) };
        track.SetResourceReference(Border.BackgroundProperty, "PanelBorderBrush");
        grid.Children.Add(track);
        var bar = new Border { CornerRadius = new CornerRadius(height / 2), HorizontalAlignment = HorizontalAlignment.Left };
        bar.SetResourceReference(Border.BackgroundProperty, fill);
        grid.Children.Add(bar);
        grid.SizeChanged += (_, e) => bar.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return grid;
    }

    /// <summary>Two columns that share the width in the given proportions, with a 16 px gap between them.</summary>
    public static Grid Columns(params (FrameworkElement Child, double Weight)[] items)
    {
        var grid = new Grid();
        for (var i = 0; i < items.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(items[i].Weight, GridUnitType.Star), MinWidth = 0 });
            items[i].Child.Margin = new Thickness(i == 0 ? 0 : 8, 0, i == items.Length - 1 ? 0 : 8, 0);
            Grid.SetColumn(items[i].Child, i);
            grid.Children.Add(items[i].Child);
        }
        return grid;
    }

    /// <summary>At most <paramref name="max"/> points spread evenly along the line, always ending on the last one: a line that is a few hundred pixels wide gains nothing from more, and every point costs a curve segment each time it is drawn.</summary>
    public static IList<Point> Thin(IList<Point> p, int max = 160)
    {
        if (p.Count <= max) return p;
        var thin = new List<Point>(max);
        for (var i = 0; i < max - 1; i++) thin.Add(p[(int)((long)i * (p.Count - 1) / (max - 1))]);
        thin.Add(p[^1]);
        return thin;
    }

    /// <summary>A smooth curve through the points (a Catmull-Rom spline drawn as Bezier segments), optionally closed down to a baseline for an area fill.</summary>
    public static Geometry Smooth(IList<Point> p, double closeToY = double.NaN)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(p[0], !double.IsNaN(closeToY), !double.IsNaN(closeToY));
            for (var i = 0; i < p.Count - 1; i++)
            {
                var p0 = p[Math.Max(i - 1, 0)]; var p1 = p[i]; var p2 = p[i + 1]; var p3 = p[Math.Min(i + 2, p.Count - 1)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
            if (!double.IsNaN(closeToY))
            {
                ctx.LineTo(new Point(p[^1].X, closeToY), false, false);
                ctx.LineTo(new Point(p[0].X, closeToY), false, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    // The line icons of the mockups (24 by 24 grid).
    public const string BoltIcon = "M13 2 4 14h7l-1 8 9-12h-7z";
    public const string BulbIcon = "M9 18h6 M10 21h4 M12 3a6 6 0 0 0-4 10.5c.7.7 1 1.5 1 2.5h6c0-1 .3-1.8 1-2.5A6 6 0 0 0 12 3z";
    public const string GamepadIcon = "M7 7h10a5.5 5.5 0 0 1 0 11H7A5.5 5.5 0 0 1 7 7z M7 10v5 M4.5 12.5h5 M16 11h.01 M18 14h.01";
    public const string CheckIcon = "m5 12 5 5 9-10";
    public const string ClockIcon = "M3 12a9 9 0 1 0 18 0 9 9 0 1 0-18 0 M12 7v5l3 2";
}
